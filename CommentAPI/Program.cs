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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

// Tạo builder web app (hosting, Kestrel, cấu hình JSON, biến môi trường).
var builder = WebApplication.CreateBuilder(args);

// Mã hóa UTF-8 cho console: log tiếng Việt không bị thành dấu ch? trên Windows cmd/PowerShell mặc định.
Console.OutputEncoding = new UTF8Encoding(false);

// Bind cấu hình JWT từ section tương ứng (IOptions<JwtOptions> dùng ở AuthenticationService, v.v.).
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
// Đọc mạnh JWT để tạo SymmetricSecurityKey; thiếu cấu hình thì dừng sớm (lỗi rõ tại startup).
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
 ?? throw new InvalidOperationException("Jwt configuration is missing in appsettings.");

// Chuỗi kết nối SQL Server cho EF Core; bắt buộc có trong appsettings/secret.
var sqlConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

// Cấu hình IDistributedCache: ưu tiên Redis, bộ nhớ dự phòng; xem CommentApiDistributedCaching.cs.
builder.AddCommentApiDistributedCache();
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

// Tạo khóa ký từ chuỗi bí mật UTF-8 — độ dài bit phụ thuộc nội dung chuỗi (HMAC thường dùng 256+ bit secret).
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey));

// Schemes: Authenticate/Challenge mặc định dùng JWT Bearer; cấu hình validate + sự kiện sau khi par token hợp lệ.
builder.Services
    .AddAuthentication(options =>
    {
        // Khi [Authorize] xác thực, mặc định dùng scheme JWT Bearer.
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        // Khi 401/Challenge, cùng scheme Bearer.
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        // Tham số validate chữ ký, issuer, audience, lifetime; map role/name id claim.
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true, // Bắt buộc ký bằng signingKey
            IssuerSigningKey = signingKey, // Khóa HMAC từ appsettings
            ValidateIssuer = true, // Bật kiểm tra iss
            ValidIssuer = jwt.Issuer, // Giá trị iss hợp lệ
            ValidateAudience = true, // Bật kiểm tra aud
            ValidAudience = jwt.Audience, // Giá trị aud hợp lệ
            ValidateLifetime = true, // exp/nbf
            ClockSkew = TimeSpan.Zero, // Không dung sai nhiều phút
            RoleClaimType = System.Security.Claims.ClaimTypes.Role, // Tên claim map role
            NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier // Tên claim map user id (thường từ sub)
        };
        // Sự kiện: lọc loại token, đối chiếu security stamp với DB; OnChallenge trả JSON thống nhất.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                // Chỉ access token mới gọi API; refresh tách endpoint riêng.
                var tokenType = context.Principal?.FindFirstValue("token_type");
                if (tokenType != "access")
                {
                    context.Fail("Invalid token type."); // Từ chối refresh/khác loại
                    return;
                }

                // Lấy id user: NameIdentifier (map từ sub) hoặc claim sub thuần JWT.
                var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
                if (string.IsNullOrEmpty(userId))
                {
                    context.Fail("User identifier is missing from the token."); // Không định danh để tiếp tục
                    return;
                }

                // Đọc security stamp tùy chỉnh để vô hiệu token cũ khi user đổi mật/đổi stamp.
                var stampInToken = context.Principal?.FindFirstValue(JwtOptions.SecurityStampClaimType);
                if (string.IsNullOrEmpty(stampInToken))
                {
                    context.Fail("Session stamp in the token is invalid.");
                    return;
                }

                // So khớp với bảng AspNetUsers qua UserManager.
                var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<User>>();
                var user = await userManager.FindByIdAsync(userId);
                if (user is null)
                {
                    context.Fail("Account does not exist."); // User đã xoá/sai id
                    return;
                }

                var currentStamp = await userManager.GetSecurityStampAsync(user);
                if (currentStamp != stampInToken)
                {
                    context.Fail("Session is no longer valid."); // Token lệch với server (logout, đổi mk, v.v.)
                    return;
                }
            },
            OnChallenge = async context =>
            {
                context.HandleResponse(); // Tự ghi body JSON, không dùng body mặc định trống
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                var cid = RequestPerformanceMiddleware.GetCorrelationId(context.HttpContext);
                context.Response.Headers.Append(RequestPerformanceMiddleware.HeaderName, cid);
                RequestPerformanceMiddleware.AppendErrorSourceHeader(context.HttpContext,
                    "JwtBearerEvents.OnChallenge (Bearer challenge: missing or invalid token)");
                RequestPerformanceMiddleware.TryAppendSqlQueryCountHeader(context.HttpContext);
                await context.Response.WriteAsJsonAsync(new
                {
                    code = ApiErrorCodes.Unauthenticated, // Mã ổn định cho client
                    type = "AuthenticationFailed",
                    message = ApiMessages.Unauthenticated // Chuỗi thân thiện client
                });
            }
        };
    });

// Bật hệ thống [Authorize] trên controller/action.
builder.Services.AddAuthorization();

// 403 có body JSON: khi xác thực ok nhưng policy cấm (role), thay vì 403 rỗng mặc định.
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ForbiddenHandler>();

// ProblemDetails + IExceptionHandler bắt buộc trên .NET 8 cho cấu hình exception middleware này.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Tự chạy validator FluentValidation theo từng request (gắn với model).
builder.Services.AddFluentValidationAutoValidation();
// Quét mọi validator trong assembly có CreateUserValidator (cùng project CommentAPI).
builder.Services.AddValidatorsFromAssemblyContaining<CreateUserValidator>();

// Chuẩn hoá 400: ModelState lỗi trả JSON có correlation id + từ điển lỗi từng field.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var http = context.HttpContext;
        var cid = RequestPerformanceMiddleware.GetCorrelationId(http);
        http.Response.Headers.Append(RequestPerformanceMiddleware.HeaderName, cid);
        var modelSource = context.ActionDescriptor?.DisplayName ?? "ApiController:ModelState";
        RequestPerformanceMiddleware.AppendErrorSourceHeader(http,
            $"Model binding / ModelState ({modelSource})");
        RequestPerformanceMiddleware.TryAppendSqlQueryCountHeader(http);
        var errors = context.ModelState
            .Where(p => p.Value != null && p.Value!.Errors.Count > 0)
            .ToDictionary(
                p => p.Key,
                p => p.Value!.Errors
                    .Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? e.Exception?.Message ?? "" : e.ErrorMessage)
                    .ToArray());
        return new BadRequestObjectResult(new
        {
            code = ApiErrorCodes.ModelValidationFailed, // Tách mã lỗi với ngoại lệ Fluent
            type = "ModelStateValidation",
            message = ApiMessages.ValidationFailed,
            errors
        });
    };
});

// Kích hoạt controller convention + routing attribute.
builder.Services.AddControllers();
// Tối thiểu cho Swashbuckle: khám phá endpoint tạo OpenAPI.
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

// Đăng ký bản đồ AutoMapper: MappingProfile trong assembly.
builder.Services.AddAutoMapper(cfg => cfg.AddProfile<MappingProfile>());

// Thời gian sống theo request: mỗi HTTP request = một scope, một DbContext, một bộ service/repo.
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IPostRepository, PostRepository>();
builder.Services.AddScoped<ICommentRepository, CommentRepository>();
builder.Services.AddScoped<IAuthenticationRepository, AuthenticationRepository>();

builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IPostService, PostService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();

// Dựng pipeline: host, middleware, endpoint — chưa lắng nghe.
var app = builder.Build();

// Một lần khi start: tạo scope, ApplyPendingMigration, seed role Admin/User nếu chưa có.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync(); // Áp mọi migration còn treo
    await SeedData.SeedAsync(scope.ServiceProvider); // Vai trò, không seed user demo tùy implement
}

// Header hiệu năng (thời gian, SQL, cache) + X-Correlation-ID; phải sớm trong pipeline.
app.UseRequestPerformance();
// IExceptionHandler pipeline: bắt mọi exception chưa xử, trả JSON; delegate rỗng nếu dùng cấu hình mặc định.
app.UseExceptionHandler(_ => { });

// OpenAPI JSON + UI; chỉ bật khi cần (dev/staging) — ở đây luôn bật tùy môi trường deploy.
app.UseSwagger();
app.UseSwaggerUI();

// Bắt buộc: UseRouting trước UseAuthentication/Authorization; custom JWT sau UseAuthentication.
app.UseRouting();
app.UseAuthentication(); // Đọc Bearer, gắn HttpContext.User
app.UseJwtAuthentication(); // Bắt buộc API /api (trừ login/refresh) có user đã xác thực
app.UseAuthorization(); // Áp dụng [Authorize], role, policy

// Trang gốc chuyển tới Swagger UI để thử nhanh API.
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

// Map controller attribute [Route], [ApiController], v.v.
app.MapControllers();

// Chạy Kestrel / IIS in-process, chặn cho tới khi process dừng.
app.Run();

// Bộ lọc tài liệu OpenAPI: gỡ Example/Examples/Default khỏi path và schema tránh Swagger tự điền sẵn mẫu.
internal sealed class RemoveSwaggerExamplesDocumentFilter : IDocumentFilter
{
    // Sau khi Swashbuckle tạo document, duyệt mọi operation và mọi schema, xoá mọi trường gợi ý mẫu.
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
            return; // Không có schema bổ sung thì xong
        }

        foreach (var schema in swaggerDoc.Components.Schemas.Values)
        {
            ClearSchemaExamples(schema);
        }
    }

    // Gỡ ví dụ ở tham số, body, từng mã trả; đệ quy qua schema lồng.
    private static void ClearOperationExamples(OpenApiOperation operation)
    {
        if (operation.Parameters != null)
        {
            foreach (var p in operation.Parameters)
            {
                p.Example = null; // Một giá trị mẫu duy nhất
                p.Examples = null; // Nhiều tên mẫu
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
                continue; // 204, redirect, v.v.
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

    private static void ClearSchemaExamples(OpenApiSchema? schema)
    {
        if (schema is null)
        {
            return; // Bỏ qua tham chiếu rỗng trong cấu trúc oneOf/anyof.
        }

        // Bỏ example và default để form Try it out trên Swagger không gợi sẵn (chuỗi mẫu, Guid=0000…, v.v.).
        schema.Example = null;
        schema.Default = null;

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
