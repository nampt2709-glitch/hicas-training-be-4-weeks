using System.IdentityModel.Tokens.Jwt; // JwtRegisteredClaimNames, đọc metadata token khi cấu hình.
using System.Security.Claims; // ClaimTypes khi gán role vào principal test/seed.
using System.Text; // Encoding.UTF8 cho khóa ký JWT SymmetricSecurityKey.
using System.Threading.RateLimiting; // FixedWindowRateLimiter, token bucket cho AddRateLimiter.
using ApartmentAPI; // ApiException, EnvLoader, RouteRateLimitOptions binding.
using ApartmentAPI.Configuration; // JwtSettings, AttachmentStorageOptions, Serilog path options.
using ApartmentAPI.Data; // AppDbContext, SeedData.
using ApartmentAPI.Entities; // User, Role — entity Identity trong DI.
using ApartmentAPI.Logging; // StructuredFileLogger đa sink tùy chỉnh.
using ApartmentAPI.Middleware; // ActivityId, exception handler, 403 JSON.
using ApartmentAPI.Repositories; // Đăng ký repository cụ thể vào DI.
using ApartmentAPI.Services; // UserService, RoleService, apartment services.
using ApartmentAPI.V1.Validators; // FluentValidation validators assembly (CreateApartmentValidator, …).
using Asp.Versioning; // AddApiVersioning, báo cáo phiên bản API.
using Asp.Versioning.ApiExplorer; // AddApiExplorer cho Swagger theo version.
using FluentValidation; // AbstractValidator — kiểu tham chiếu khi cấu hình pipeline.
using FluentValidation.AspNetCore; // AddFluentValidationAutoValidation.
using Microsoft.AspNetCore.Authentication.JwtBearer; // AddAuthentication().AddJwtBearer.
using Microsoft.AspNetCore.Authorization; // AddAuthorization, policy mặc định.
using Microsoft.AspNetCore.Authorization.Policy; // FallbackPolicy — xử lý 403 nhất quán.
using Microsoft.AspNetCore.Identity; // IdentityBuilder, AddIdentity Core.
using Microsoft.AspNetCore.Mvc; // Controllers, JSON options, ApiBehavior.
using Microsoft.AspNetCore.RateLimiting; // RateLimiterOptions, AddRateLimiter.
using Microsoft.Data.SqlClient; // SqlException — nhận diện lỗi trùng hoặc deadlock khi migrate/seed.
using Microsoft.EntityFrameworkCore; // UseSqlServer, Migrate, Database.EnsureCreated guard.
using Microsoft.Extensions.Options; // IOptions/JwtSettings trong delegate JwtBearer.
using Microsoft.IdentityModel.Tokens; // TokenValidationParameters, SymmetricSecurityKey.
using Microsoft.OpenApi.Models; // OpenApiInfo, Bearer security scheme cho Swagger.
using Serilog; // Log.Logger, WriteTo, Enrich.
using Serilog.Events; // LogEventLevel — tách mức file log.
using Swashbuckle.AspNetCore.SwaggerGen; // SwaggerGenOptions, filter document theo version.

// File Program.cs — composition root ASP.NET Core: đọc .env/JWT/quản lí DI, JWT Bearer + middleware tùy chỉnh, Serilog đa file, migrate + Seed khi khởi động.
Console.OutputEncoding = new UTF8Encoding(false); // Log ASCII an toàn trên một số console Windows.

EnvLoader.LoadEnvFilesBeforeHost(); // BƯỚC 1 — Merge biến môi trường từ .env vào tiến trình trước khi build host.

var builder = WebApplication.CreateBuilder(args); // BƯỚC 2 — ApplicationBuilder chuẩn (DI + config + logging bootstrap).

builder.WebHost.ConfigureKestrel(options => // Giới hạn body request (upload multipart).
{
    options.Limits.MaxRequestBodySize = 35 * 1024 * 1024; // Khớp FormOptions và RequestSizeLimit controller đính kèm.
});

EnvLoader.LoadEnvFile(builder.Environment.ContentRootPath); // BƯỚC — Nạp lại .env theo ContentRoot (thư mục project).

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName)); // Issuer/Audience/SigningKey JWT.
builder.Services.Configure<AttachmentStorageOptions>(
    builder.Configuration.GetSection(AttachmentStorageOptions.SectionName)); // Thư mục lưu file + giới hạn byte.
builder.Services.AddSingleton<IAttachmentFileStorage, AttachmentFileStorage>(); // Implement lưu vật lý Attachment.

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 35 * 1024 * 1024;
});
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt section missing in appsettings.");

var sqlConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(sqlConnectionString))
{
    throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is missing or empty. Copy ApartmentAPI/.env.example to ApartmentAPI/.env and set ConnectionStrings__DefaultConnection, or use user secrets.");
}

var saPwd = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD");
if (!string.IsNullOrWhiteSpace(saPwd))
{
    var csb = new SqlConnectionStringBuilder(sqlConnectionString) { Password = saPwd };
    sqlConnectionString = csb.ConnectionString;
}

builder.AddDistributedCaching(); // Extension: Redis/stack exchange hoặc fallback (xem DistributedCaching.cs).
builder.Services.AddSingleton<ICacheListEpochStore, CacheListEpochStore>(); // Invalidate list cache epoch.
builder.Services.AddSingleton<RequestTimingDbCommandInterceptor>(); // Interceptor EF: cộng dồn thời gian SQL + đếm truy vấn theo HTTP.
builder.Services.AddHttpContextAccessor(); // Inject HttpContext vào interceptor.
builder.Services.AddScoped<CacheResponseTracker>(); // HIT/MISS cho header X-Cache-Status.
builder.Services.AddScoped<IEntityResponseCache, EntityResponseCache>(); // Wrapper cache chi tiết entity.

builder.Services.AddDbContext<AppDbContext>((sp, options) => // DbContext scoping — một instance mỗi request trong scope điển hình.
    options.UseSqlServer(sqlConnectionString)
        .AddInterceptors(sp.GetRequiredService<RequestTimingDbCommandInterceptor>()));

builder.Services.AddIdentityCore<User>(options =>
    {
        options.Password.RequireNonAlphanumeric = false; // Mật khẩu training đơn giản hoá policy.
        options.Password.RequireUppercase = false;
        options.User.RequireUniqueEmail = false;
    })
    .AddRoles<Role>() // AspNetRole được map Role entity tuỳ biến.
    .AddEntityFrameworkStores<AppDbContext>(); // Bảng Users/Roles/UserRoles qua EF.

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)); // HS256 symmetric key đọc từ appsettings/.env.
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
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.NameIdentifier,
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
            OnChallenge = async context => // Thay thể challenge mặc định của Bearer bằng JSON + correlation/security log.
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                var cid = RequestPerformanceMiddleware.GetCorrelationId(context.HttpContext);
                context.Response.Headers.Append(RequestPerformanceMiddleware.HeaderName, cid);
                RequestPerformanceMiddleware.AppendErrorSourceHeader(context.HttpContext,
                    "JwtBearerEvents.OnChallenge (Bearer challenge: missing or invalid token)");
                RequestPerformanceMiddleware.TryAppendSqlQueryCountHeader(context.HttpContext);
                await context.Response.WriteAsJsonAsync(new
                {
                    code = ApiErrorCodes.Unauthenticated,
                    type = "AuthenticationFailed",
                    message = ApiMessages.Unauthenticated
                });
                StructuredFileLogger.Security(
                    cid,
                    "JwtBearerChallenge",
                    context.HttpContext.Request.Method,
                    context.HttpContext.Request.Path.Value ?? "",
                    StatusCodes.Status401Unauthorized,
                    "Bearer challenge: missing or invalid token");
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, ForbiddenHandler>(); // 403 JSON khi policy không pass.

builder.Services.AddRateLimiter(options => // Partition theo endpoint + IP; cấu hình RouteRateLimitConfiguration.
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var http = context.HttpContext;
        var cid = RequestPerformanceMiddleware.GetCorrelationId(http);
        var path = http.Request.Path.Value ?? "";
        var bodySnap = http.Items.TryGetValue(StructuredFileLogger.RequestBodyItemKey, out var rb) ? rb?.ToString() ?? "" : "";
        StructuredFileLogger.Errors(
            cid,
            StatusCodes.Status429TooManyRequests,
            http.Request.Method,
            path,
            ApiErrorCodes.RateLimitExceeded,
            "RateLimitExceeded",
            ApiMessages.RateLimitExceeded,
            null,
            bodySnap);
        http.Response.ContentType = "application/json";
        await http.Response.WriteAsJsonAsync(new
        {
            code = ApiErrorCodes.RateLimitExceeded,
            type = "RateLimitExceeded",
            message = ApiMessages.RateLimitExceeded
        }, cancellationToken);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var endpoint = httpContext.GetEndpoint() as RouteEndpoint;
        var routePattern = endpoint?.RoutePattern.RawText ?? httpContext.Request.Path.Value;
        var rule = RouteRateLimitConfiguration.Resolve(httpContext.Request.Method, routePattern);
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
        var partitionKey = $"{rule.Key}:{clientIp}";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rule.PermitLimit,
                Window = TimeSpan.FromSeconds(rule.WindowSeconds),
                QueueLimit = rule.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            });
    });
});

builder.Services.AddApiVersioning(options => // Đọc version từ segment URL /api/v{version}/...
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
}).AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>(); // IExceptionHandler chuẩn ASP.NET Core 8.

builder.Services.AddFluentValidationAutoValidation(); // Tự chạy AbstractValidator trước action.
builder.Services.AddValidatorsFromAssemblyContaining<CreateApartmentValidator>();

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var http = context.HttpContext;
        var cid = RequestPerformanceMiddleware.GetCorrelationId(http);
        http.Response.Headers.Append(RequestPerformanceMiddleware.HeaderName, cid);
        var modelSource = context.ActionDescriptor?.DisplayName ?? "ApiController:ModelState";
        RequestPerformanceMiddleware.AppendErrorSourceHeader(http, $"Model binding / ModelState ({modelSource})");
        RequestPerformanceMiddleware.TryAppendSqlQueryCountHeader(http);
        var errors = context.ModelState
            .Where(p => p.Value != null && p.Value!.Errors.Count > 0)
            .ToDictionary(
                p => p.Key,
                p => p.Value!.Errors
                    .Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? e.Exception?.Message ?? "" : e.ErrorMessage)
                    .ToArray());
        var errSummary = string.Join("; ", errors.SelectMany(kv => kv.Value.Select(m => $"{kv.Key}: {m}")));
        var bodySnap = http.Items.TryGetValue(StructuredFileLogger.RequestBodyItemKey, out var rb) ? rb?.ToString() ?? "" : "";
        StructuredFileLogger.Errors(
            cid,
            StatusCodes.Status400BadRequest,
            http.Request.Method,
            http.Request.Path.Value ?? "",
            ApiErrorCodes.ModelValidationFailed,
            "ModelStateValidation",
            errSummary,
            null,
            bodySnap);
        return new BadRequestObjectResult(new
        {
            code = ApiErrorCodes.ModelValidationFailed,
            type = "ModelStateValidation",
            message = ApiMessages.ValidationFailed,
            correlationId = cid,
            errors
        });
    };
});

builder.Services.AddAutoMapper(cfg => // Đăng ký profile V1 (entity↔DTO) + V2 (tương lai mở rộng).
{
    cfg.AddProfile<ApartmentAPI.V1.MappingProfile>();
    cfg.AddProfile<ApartmentAPI.V2.MappingProfile>();
});

builder.Services.AddScoped<IAuthenticationRepository, AuthenticationRepository>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();

builder.Services.AddScoped<IApartmentRepository, ApartmentRepository>();
builder.Services.AddScoped<IResidentRepository, ResidentRepository>();
builder.Services.AddScoped<IUtilityServiceRepository, UtilityServiceRepository>();
builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
builder.Services.AddScoped<IInvoiceItemRepository, InvoiceItemRepository>();
builder.Services.AddScoped<IFeedbackRepository, FeedbackRepository>();
builder.Services.AddScoped<IPostRepository, PostRepository>();
builder.Services.AddScoped<IAttachmentRepository, AttachmentRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

builder.Services.AddScoped<IApartmentService, ApartmentService>();
builder.Services.AddScoped<IResidentService, ResidentService>();
builder.Services.AddScoped<IUtilityCatalogService, UtilityCatalogService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IInvoiceItemService, InvoiceItemService>();
builder.Services.AddScoped<IFeedbackService, FeedbackService>();
builder.Services.AddScoped<IPostService, PostService>();
builder.Services.AddScoped<IAttachmentService, AttachmentService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IRoleService, RoleService>();

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ApartmentResourceFilter>();
    options.Filters.Add<ApartmentActionFilter>();
    options.Filters.Add<ApartmentExceptionFilter>();
    options.Filters.Add<ApartmentResultFilter>();
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
builder.Services.AddSwaggerGen(c =>
{
    c.OperationFilter<SwaggerDefaultValues>();
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Access token (JWT). Paste the raw token only; do not type the Bearer prefix."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        },
    });
});

var logsDir = Path.Combine(builder.Environment.ContentRootPath, "Logs"); // Thư mục log file (Errors, Audit, Security, …).
Directory.CreateDirectory(logsDir);
const string fileTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";
const string filtersLogTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";
// Serilog: file sinks luôn bật; console chỉ Development để xem SQL EF (Microsoft.EntityFrameworkCore.Database.Command) và log runtime trên terminal.
var serilogCfg = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Information)
    .MinimumLevel.Override("ApartmentResourceFilter", LogEventLevel.Verbose)
    .MinimumLevel.Override("ApartmentActionFilter", LogEventLevel.Verbose)
    .MinimumLevel.Override("ApartmentExceptionFilter", LogEventLevel.Verbose)
    .MinimumLevel.Override("ApartmentResultFilter", LogEventLevel.Verbose)
    .Enrich.FromLogContext()
    .Enrich.WithThreadId()
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e => StructuredFileLogger.IsChannel(e, StructuredFileLogger.ErrorsChannel))
        .WriteTo.File(Path.Combine(logsDir, "ErrorsLog.log"), rollingInterval: RollingInterval.Infinite, shared: true, outputTemplate: fileTemplate))
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e => StructuredFileLogger.IsChannel(e, StructuredFileLogger.AuditChannel))
        .WriteTo.File(Path.Combine(logsDir, "AuditLog.log"), rollingInterval: RollingInterval.Infinite, shared: true, outputTemplate: fileTemplate))
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e => StructuredFileLogger.IsChannel(e, StructuredFileLogger.SecurityChannel))
        .WriteTo.File(Path.Combine(logsDir, "SecurityLog.log"), rollingInterval: RollingInterval.Infinite, shared: true, outputTemplate: fileTemplate))
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e => StructuredFileLogger.IsChannel(e, StructuredFileLogger.WarningsChannel))
        .WriteTo.File(Path.Combine(logsDir, "WarningsLog.log"), rollingInterval: RollingInterval.Infinite, shared: true, outputTemplate: fileTemplate))
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e => StructuredFileLogger.IsChannel(e, StructuredFileLogger.FatalsChannel) || e.Level == LogEventLevel.Fatal)
        .WriteTo.File(Path.Combine(logsDir, "FatalsLog.log"), rollingInterval: RollingInterval.Infinite, shared: true, outputTemplate: fileTemplate))
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e => StructuredFileLogger.IsChannel(e, StructuredFileLogger.ActivitiesChannel))
        .WriteTo.File(Path.Combine(logsDir, "ActivitiesLog.log"), rollingInterval: RollingInterval.Infinite, shared: true, outputTemplate: fileTemplate))
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(StructuredFileLogger.IsPipelineFilterLog)
        .WriteTo.File(Path.Combine(logsDir, "FiltersLog.log"), rollingInterval: RollingInterval.Infinite, shared: true, outputTemplate: filtersLogTemplate));

if (builder.Environment.IsDevelopment())
{
    // BƯỚC — Console: cùng định dạng gần với file pipeline để đọc SourceContext + SQL dễ hơn.
    serilogCfg = serilogCfg.WriteTo.Console(
        restrictedToMinimumLevel: LogEventLevel.Information,
        outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}");
}

Log.Logger = serilogCfg.CreateLogger();
builder.Host.UseSerilog(Log.Logger, dispose: true);

var app = builder.Build(); // Đóng băng pipeline — thêm middleware dưới đây.

AppDomain.CurrentDomain.UnhandledException += (_, e) => // Bắt lỗi ngoài pipeline (ví dụ background thread) → Fatals log.
{
    if (e.ExceptionObject is Exception domainEx)
        StructuredFileLogger.Fatals("AppDomain.UnhandledException", domainEx);
    else
        StructuredFileLogger.Fatals($"AppDomain.UnhandledException: {e.ExceptionObject}", null);
    Log.CloseAndFlush();
};

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync(); // Áp dụng EF migrations còn thiếu.
    await SeedData.SeedAsync(scope.ServiceProvider); // Vai trò Identity mặc định (Admin/User).
}

app.UseRequestPerformance(); // Correlation + đo thời gian request/SQL/count.
app.UseActivityAndAuditLogging(); // ACTIVITY/AUDIT file log (buffer response).
app.UseExceptionHandler(_ => { }); // Chuyển exception sang GlobalExceptionHandler.

app.UseSwagger(); // OpenAPI middleware (swagger.json theo nhóm phiên bản API).
var apiVersionDescriptionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
app.UseSwaggerUI(options =>
{
    foreach (var description in apiVersionDescriptionProvider.ApiVersionDescriptions)
    {
        var url = $"{description.GroupName}/swagger.json";
        var name = description.GroupName.ToUpperInvariant();
        options.SwaggerEndpoint(url, name);
    }
});

app.UseRouting();
app.UseRateLimiter(); // Partitioned FixedWindowLimiter theo RouteRateLimitConfiguration.
app.UseAuthentication(); // Jwt Bearer + OnTokenValidated.
app.UseJwtAuthentication(); // Middleware tùy: buộc user đã authenticate cho mọi /api/** trừ auth công khai.
app.UseAuthorization(); // Policy + ForbiddenHandler trên 403.
app.MapControllers(); // Controllers V1/V2 + AuthController.

try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}
