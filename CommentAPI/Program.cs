using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CommentAPI;
using CommentAPI.Data;
using CommentAPI.Entities;
using CommentAPI.Interfaces;
using CommentAPI.Middleware;
using CommentAPI.Repositories;
using CommentAPI.Services;
using CommentAPI.Validators;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);

// Đọc cấu hình JWT (issuer, audience, signing key, thời gian sống token).
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
 ?? throw new InvalidOperationException("Jwt configuration is missing in appsettings.");

var sqlConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

// Cấu hình TTL cache entity và chọn Redis nếu ping được, ngược lại dùng memory distributed cache.
builder.Services.Configure<CacheOptions>(builder.Configuration.GetSection(CacheOptions.SectionName));
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
var useRedis = false;
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    try
    {
        var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
        redisOptions.ConnectTimeout = 2000;
        redisOptions.SyncTimeout = 2000;
        redisOptions.AbortOnConnectFail = true;
        using var mux = ConnectionMultiplexer.Connect(redisOptions);
        mux.GetDatabase().Ping();
        useRedis = true;
    }
    catch (Exception ex)
    {
        using var lf = LoggerFactory.Create(logging =>
        {
            logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
            logging.AddConsole();
        });
        lf.CreateLogger("CommentAPI.Cache").LogWarning(ex, "Không kết nối được Redis; dùng cache trong bộ nhớ.");
    }
}

if (useRedis)
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "CommentAPI:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddSingleton(new CacheBackendDescriptor(useRedis ? "redis" : "memory"));
builder.Services.AddScoped<CacheResponseTracker>();
builder.Services.AddScoped<IEntityResponseCache, EntityResponseCache>();

// HttpContext cho interceptor đo thời gian SQL theo từng request.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<RequestTimingDbCommandInterceptor>();

// DbContext: user Identity, bài post và comment; interceptor cộng dồn thời gian lệnh SQL.
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options.UseLazyLoadingProxies()
        .UseSqlServer(sqlConnectionString)
        .AddInterceptors(sp.GetRequiredService<RequestTimingDbCommandInterceptor>()));

// Identity: băm mật khẩu qua UserManager; vai trò trong AspNetRoles / AspNetUserRoles.
builder.Services.AddIdentityCore<User>(options =>
    {
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.User.RequireUniqueEmail = false;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>();

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey));

// Bearer JWT: chỉ access token (token_type = access) được gọi API.
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var tokenType = context.Principal?.FindFirstValue("token_type");
                if (tokenType != "access")
                {
                    context.Fail("Invalid token type.");
                    return;
                }

                // Sau khi JWT hợp lệ, "sub" thường map sang NameIdentifier.
                var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
                if (string.IsNullOrEmpty(userId))
                {
                    context.Fail("User identifier is missing from the token.");
                    return;
                }

                var stampInToken = context.Principal?.FindFirstValue(JwtOptions.SecurityStampClaimType);
                if (string.IsNullOrEmpty(stampInToken))
                {
                    context.Fail("Session stamp in the token is invalid.");
                    return;
                }

                var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<User>>();
                var user = await userManager.FindByIdAsync(userId);
                if (user is null)
                {
                    context.Fail("Account does not exist.");
                    return;
                }

                var currentStamp = await userManager.GetSecurityStampAsync(user);
                if (currentStamp != stampInToken)
                {
                    context.Fail("Session is no longer valid.");
                    return;
                }
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                var cid = CorrelationMiddleware.GetCorrelationId(context.HttpContext);
                context.Response.Headers.Append(CorrelationMiddleware.HeaderName, cid);
                CorrelationMiddleware.AppendErrorSourceHeader(context.HttpContext,
                    "JwtBearerEvents.OnChallenge (Bearer challenge: missing or invalid token)");
                CorrelationMiddleware.TryAppendSqlQueryCountHeader(context.HttpContext);
                await context.Response.WriteAsJsonAsync(new
                {
                    code = ApiErrorCodes.Unauthenticated,
                    type = "AuthenticationFailed",
                    message = ApiMessages.Unauthenticated
                });
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ForbiddenHandler>();

// ProblemDetails + IExceptionHandler bắt buộc trên .NET 8 cho cấu hình exception middleware này.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<CreateUserValidator>();

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var http = context.HttpContext;
        var cid = CorrelationMiddleware.GetCorrelationId(http);
        http.Response.Headers.Append(CorrelationMiddleware.HeaderName, cid);
        var modelSource = context.ActionDescriptor?.DisplayName ?? "ApiController:ModelState";
        CorrelationMiddleware.AppendErrorSourceHeader(http,
            $"Model binding / ModelState ({modelSource})");
        CorrelationMiddleware.TryAppendSqlQueryCountHeader(http);
        var errors = context.ModelState
            .Where(p => p.Value != null && p.Value!.Errors.Count > 0)
            .ToDictionary(
                p => p.Key,
                p => p.Value!.Errors
                    .Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? e.Exception?.Message ?? "" : e.ErrorMessage)
                    .ToArray());
        return new BadRequestObjectResult(new
        {
            code = ApiErrorCodes.ModelValidationFailed,
            type = "ModelStateValidation",
            message = ApiMessages.ValidationFailed,
            errors
        });
    };
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger: bỏ ví dụ; thêm bảo mật Bearer cho try-it-out.
builder.Services.AddSwaggerGen(options =>
{
    options.DocumentFilter<RemoveSwaggerExamplesDocumentFilter>();
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Access token (JWT). Paste the raw token only; do not type the Bearer prefix."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddAutoMapper(cfg => cfg.AddProfile<MappingProfile>());

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IPostRepository, PostRepository>();
builder.Services.AddScoped<ICommentRepository, CommentRepository>();
builder.Services.AddScoped<IAuthenticationRepository, AuthenticationRepository>();

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IPostService, PostService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();

var app = builder.Build();

// Chạy migration và seed vai trò (không tạo user mặc định — tạo admin trong database).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.SeedAsync(scope.ServiceProvider);
}

app.UseCorrelationId();
app.UseExceptionHandler(_ => { });

app.UseSwagger();
app.UseSwaggerUI();

// Thứ tự: routing → xác thực JWT → cổng JWT API → phân quyền theo role trên controller.
app.UseRouting();
app.UseAuthentication();
app.UseJwtAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.MapControllers();

app.Run();

/// <summary>
/// Xóa ví dụ khỏi OpenAPI để Swagger UI không điền sẵn giá trị mẫu.
/// </summary>
internal sealed class RemoveSwaggerExamplesDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        if (swaggerDoc.Paths != null)
        {
            foreach (var pathItem in swaggerDoc.Paths.Values)
            {
                foreach (var operation in pathItem.Operations.Values)
                {
                    ClearOperationExamples(operation);
                }
            }
        }

        if (swaggerDoc.Components?.Schemas == null)
        {
            return;
        }

        foreach (var schema in swaggerDoc.Components.Schemas.Values)
        {
            ClearSchemaExamples(schema);
        }
    }

    private static void ClearOperationExamples(OpenApiOperation operation)
    {
        if (operation.Parameters != null)
        {
            foreach (var p in operation.Parameters)
            {
                p.Example = null;
                p.Examples = null;
                if (p.Schema != null)
                {
                    ClearSchemaExamples(p.Schema);
                }
            }
        }

        if (operation.RequestBody?.Content != null)
        {
            foreach (var media in operation.RequestBody.Content.Values)
            {
                media.Example = null;
                media.Examples = null;
                if (media.Schema != null)
                {
                    ClearSchemaExamples(media.Schema);
                }
            }
        }

        if (operation.Responses == null)
        {
            return;
        }

        foreach (var response in operation.Responses.Values)
        {
            if (response.Content == null)
            {
                continue;
            }

            foreach (var media in response.Content.Values)
            {
                media.Example = null;
                media.Examples = null;
                if (media.Schema != null)
                {
                    ClearSchemaExamples(media.Schema);
                }
            }
        }
    }

    private static void ClearSchemaExamples(OpenApiSchema schema)
    {
        schema.Example = null;

        if (schema.Properties != null)
        {
            foreach (var prop in schema.Properties.Values)
            {
                ClearSchemaExamples(prop);
            }
        }

        if (schema.Items != null)
        {
            ClearSchemaExamples(schema.Items);
        }

        if (schema.AdditionalProperties != null && schema.AdditionalProperties.Reference == null)
        {
            ClearSchemaExamples(schema.AdditionalProperties);
        }

        foreach (var inner in schema.AllOf)
        {
            ClearSchemaExamples(inner);
        }

        foreach (var inner in schema.OneOf)
        {
            ClearSchemaExamples(inner);
        }

        foreach (var inner in schema.AnyOf)
        {
            ClearSchemaExamples(inner);
        }
    }
}
