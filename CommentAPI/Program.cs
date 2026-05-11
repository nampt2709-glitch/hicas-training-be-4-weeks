using System.IdentityModel.Tokens.Jwt; // Tên claim JWT chuẩn (Sub, v.v.) khi đọc principal trong Program.
using System.Security.Claims; // ClaimTypes (NameIdentifier, Role) map vào HttpContext.User.
using System.Text; // Encoding.UTF8 cho SymmetricSecurityKey từ JwtOptions.SigningKey.
using Asp.Versioning; // ApiVersion, URL segment reader — versioning giống ApartmentAPI.
using Asp.Versioning.ApiExplorer; // IApiVersionDescriptionProvider: một document Swagger mỗi version.
using CommentAPI; // ApiErrorCodes, ApiMessages, SortByColumn, DistributedCaching, CacheListEpochStore, JwtOptions, v.v.
using CommentAPI.Configuration; // EnvLoader, ConfigureSwaggerOptions, RouteRateLimitConfiguration, SwaggerDefaultValues.
using CommentAPI.Data; // AppDbContext, SeedData.
using CommentAPI.Logging; // StructuredFileLogger — kênh Activities/ERRORS/SECURITY.
using CommentAPI.Entities; // User : IdentityUser<Guid> cho AddIdentityCore generic.
using CommentAPI.Interfaces; // Không dùng trực tiếp ở đây nhưng cùng assembly với service đăng ký.
using CommentAPI.Middleware; // RequestPerformance, ActivityAndAudit, GlobalExceptionHandler, JwtAuthentication, ForbiddenHandler.
using CommentAPI.Repositories; // IUserRepository, IPostRepository, ICommentRepository, IAuthenticationRepository (đăng ký scoped).
using CommentAPI.Services; // IUserService, IPostService, CommentService, AuthenticationService.
using CommentAPI.Validators; // CreateUserValidator — assembly quét FluentValidation.
using FluentValidation; // AbstractValidator baseline.
using FluentValidation.AspNetCore; // AddFluentValidationAutoValidation.
using Microsoft.AspNetCore.Authentication.JwtBearer; // JwtBearerDefaults, JwtBearerEvents, Bearer scheme.
using Microsoft.AspNetCore.Authorization; // AddAuthorization — [Authorize] trên controller.
using Microsoft.AspNetCore.Authorization.Policy; // IAuthorizationMiddlewareResultHandler (ForbiddenHandler).
using Microsoft.AspNetCore.Diagnostics; // Exception handler delegate (ở đây gọi UseExceptionHandler rỗng + GlobalExceptionHandler).
using Microsoft.AspNetCore.Identity; // AddIdentityCore, UserManager, IdentityRole.
using Microsoft.AspNetCore.Mvc; // ApiBehaviorOptions, BadRequestObjectResult, Controllers.
using Microsoft.AspNetCore.RateLimiting; // AddRateLimiter, PartitionedRateLimiter, FixedWindow.
using Microsoft.AspNetCore.Mvc.Controllers; // ControllerActionDescriptor — Swagger OrderActionsBy theo MetadataToken.
using Microsoft.Data.SqlClient; // SqlConnectionStringBuilder — ghi đè Password từ MSSQL_SA_PASSWORD.
using Microsoft.EntityFrameworkCore; // UseSqlServer, AddDbContext, AddInterceptors.
using Microsoft.Extensions.DependencyInjection; // Service lifetime helpers (ở DistributedCaching có dùng).
using Microsoft.Extensions.Logging; // MinimumLevel trong Serilog bootstrap.
using Microsoft.Extensions.Options; // IConfigureOptions — ConfigureSwaggerOptions.
using Microsoft.IdentityModel.Tokens; // SymmetricSecurityKey, TokenValidationParameters.
using Microsoft.OpenApi.Models; // OpenApiInfo, OpenApiSecurityScheme cho SwaggerGen.
using Serilog; // Log.Logger, LoggerConfiguration, RollingInterval.
using Serilog.Events; // LogEventLevel (Verbose override cho pipeline filters).
using Swashbuckle.AspNetCore.SwaggerGen; // SwaggerGenOptions, IDocumentFilter, IOperationFilter.
using System.Threading.RateLimiting; // RateLimitPartition, FixedWindowRateLimiterOptions, QueueProcessingOrder.

// =============================================================================
// File Program.cs: điểm vào CommentAPI — nạp .env, WebApplicationBuilder, DI (EF, Identity, JWT, cache, versioning,
// rate limit, Serilog, AutoMapper, repo/service), pipeline middleware, Migrate+Seed, MapControllers, Run.
// =============================================================================
// Mã hóa UTF-8 cho console: log tiếng Việt không bị thành dấu ch? trên Windows cmd/PowerShell mặc định.
Console.OutputEncoding = new UTF8Encoding(false);

// Nạp .env trước CreateBuilder để ConnectionStrings__* có sẵn khi host build IConfiguration (tránh chuỗi rỗng từ appsettings).
EnvLoader.LoadEnvFilesBeforeHost();

// Tạo builder web app (hosting, Kestrel, cấu hình JSON, biến môi trường).
var builder = WebApplication.CreateBuilder(args);

// Dự phòng: .env trong ContentRoot nếu cwd khác và file chỉ nằm cạnh csproj.
EnvLoader.LoadEnvFile(builder.Environment.ContentRootPath);

// Bind cấu hình JWT từ section tương ứng (IOptions<JwtOptions> dùng ở AuthenticationService, v.v.).
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
// Đọc mạnh JWT để tạo SymmetricSecurityKey; thiếu cấu hình thì dừng sớm (lỗi rõ tại startup).
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
 ?? throw new InvalidOperationException("Jwt configuration is missing in appsettings.");

// Chuỗi kết nối SQL Server cho EF Core; bắt buộc có trong appsettings/secret/.env (không được rỗng).
var sqlConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(sqlConnectionString))
{
    throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is missing or empty. Set ConnectionStrings__DefaultConnection in .env (see CommentAPI/.env.example) or user secrets, and load .env before WebApplication.CreateBuilder.");
}

// Ghi đè Password bằng MSSQL_SA_PASSWORD từ file .env gốc solution (cùng nguồn với docker-compose) — tránh lệch mật khẩu trong CommentAPI/.env.
var saPwd = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD");
if (!string.IsNullOrWhiteSpace(saPwd))
{
    var csb = new SqlConnectionStringBuilder(sqlConnectionString) { Password = saPwd };
    sqlConnectionString = csb.ConnectionString;
}

// Cấu hình IDistributedCache: ưu tiên Redis, bộ nhớ dự phòng; xem DistributedCaching.cs.
builder.AddDistributedCaching();
builder.Services.AddSingleton<ICacheListEpochStore, CacheListEpochStore>(); // Epoch danh sách cmt/pst/usr — làm miss cache list sau CRUD.
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
                StructuredFileLogger.Security( // Nhóm SECURITY: challenge JWT (chưa/ sai token).
                    cid,
                    "JwtBearerChallenge",
                    context.HttpContext.Request.Method,
                    context.HttpContext.Request.Path.Value ?? "",
                    StatusCodes.Status401Unauthorized,
                    "Bearer challenge: missing or invalid token");
            }
        };
    });

// Bật hệ thống [Authorize] trên controller/action.
builder.Services.AddAuthorization();

// Rate limiting toàn cục theo từng endpoint route + method; cấu hình tập trung trong RouteRateLimitConfiguration.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var http = context.HttpContext; // Context hiện tại.
        var cid = RequestPerformanceMiddleware.GetCorrelationId(http); // Correlation cho log.
        var path = http.Request.Path.Value ?? ""; // Đường dẫn request.
        var bodySnap = http.Items.TryGetValue(StructuredFileLogger.RequestBodyItemKey, out var rb) ? rb?.ToString() ?? "" : ""; // Body đã Capture (nếu có).
        StructuredFileLogger.Errors( // Nhóm ERRORS: 429.
            cid,
            StatusCodes.Status429TooManyRequests,
            http.Request.Method,
            path,
            ApiErrorCodes.InvalidOperation,
            "RateLimitExceeded",
            ApiMessages.InvalidRequest,
            null,
            bodySnap);
        http.Response.ContentType = "application/json"; // JSON lỗi.
        await http.Response.WriteAsJsonAsync(new
        {
            code = ApiErrorCodes.InvalidOperation,
            type = "RateLimitExceeded",
            message = ApiMessages.InvalidRequest
        }, cancellationToken);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var endpoint = httpContext.GetEndpoint() as RouteEndpoint;
        var routePattern = endpoint?.RoutePattern.RawText ?? httpContext.Request.Path.Value;
        var rule = RouteRateLimitConfiguration.Resolve(httpContext.Request.Method, routePattern);
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";
        var partitionKey = $"{rule.Key}:{clientIp}";

        // Mỗi route của mỗi client IP có bộ đếm riêng, tránh một route nặng làm nghẽn route khác.
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

// API Versioning: segment URL api/v{version}/… + ApiExplorer (Swagger một document mỗi version) — giống ApartmentAPI.
builder.Services.AddApiVersioning(options =>
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
        var errSummary = string.Join("; ", errors.SelectMany(kv => kv.Value.Select(m => $"{kv.Key}: {m}"))); // Một dòng tóm tắt cho file ERRORS.
        var bodySnap = http.Items.TryGetValue(StructuredFileLogger.RequestBodyItemKey, out var rb) ? rb?.ToString() ?? "" : ""; // Body đã middleware capture.
        StructuredFileLogger.Errors( // Nhóm ERRORS: model binding / ModelState 400.
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
            code = ApiErrorCodes.ModelValidationFailed, // Tách mã lỗi với ngoại lệ Fluent.
            type = "ModelStateValidation",
            message = ApiMessages.ValidationFailed,
            correlationId = cid, // Trả correlation cho client khớp Activities/ERRORS.
            errors
        });
    };
});

// Kích hoạt controller convention + routing attribute; filter pipeline tối giản (PipelineFilters.cs).
builder.Services.AddControllers(options =>
{
    options.Filters.Add<CommentResourceFilter>();
    options.Filters.Add<CommentActionFilter>();
    options.Filters.Add<CommentExceptionFilter>();
    options.Filters.Add<CommentResultFilter>();
});
// Tối thiểu cho Swashbuckle: khám phá endpoint tạo OpenAPI.
builder.Services.AddEndpointsApiExplorer();

// Swagger: một OpenAPI document per version (ConfigureSwaggerOptions) + Bearer; giống ApartmentAPI.
builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
builder.Services.AddSwaggerGen(options =>
{
    // Swashbuckle mặc định OrderActionsBy theo RelativePath + HttpMethod → thứ tự alphabet của URI (vd. api/admin/... trước api/posts) và của verb (DELETE trước GET), không khớp thứ tự action trong controller. Gom theo controller (FullName) rồi MetadataToken của method (Roslyn thường tăng dần theo thứ tự khai báo trong class) để UI gần với file controller.
    options.OrderActionsBy(apiDesc =>
    {
        if (apiDesc.ActionDescriptor is ControllerActionDescriptor cad)
        {
            var typeKey = cad.ControllerTypeInfo.FullName ?? cad.ControllerName;
            return $"{typeKey}_{cad.MethodInfo.MetadataToken:X8}";
        }

        return $"{apiDesc.RelativePath}_{apiDesc.HttpMethod}";
    });
    options.OperationFilter<SwaggerDefaultValues>();
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

// Serilog: sáu file kênh + FiltersLog; Development thêm Console để xem SQL EF và log runtime trên terminal (UseSerilog thay provider mặc định — trước đây chỉ có file nên console trống).
var logsDir = Path.Combine(builder.Environment.ContentRootPath, "Logs");
Directory.CreateDirectory(logsDir);
const string fileTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";
const string filtersLogTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";
var serilogCfg = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Information)
    .MinimumLevel.Override("CommentResourceFilter", LogEventLevel.Verbose)
    .MinimumLevel.Override("CommentActionFilter", LogEventLevel.Verbose)
    .MinimumLevel.Override("CommentExceptionFilter", LogEventLevel.Verbose)
    .MinimumLevel.Override("CommentResultFilter", LogEventLevel.Verbose)
    .Enrich.FromLogContext()
    .Enrich.WithThreadId() // Tiện đối chiếu song song.
    .WriteTo.Logger(lc => lc // ERRORS → ErrorsLog.log.
        .Filter.ByIncludingOnly(e => StructuredFileLogger.IsChannel(e, StructuredFileLogger.ErrorsChannel))
        .WriteTo.File(Path.Combine(logsDir, "ErrorsLog.log"), rollingInterval: RollingInterval.Infinite, shared: true, outputTemplate: fileTemplate))
    .WriteTo.Logger(lc => lc // AUDIT → AuditLog.log.
        .Filter.ByIncludingOnly(e => StructuredFileLogger.IsChannel(e, StructuredFileLogger.AuditChannel))
        .WriteTo.File(Path.Combine(logsDir, "AuditLog.log"), rollingInterval: RollingInterval.Infinite, shared: true, outputTemplate: fileTemplate))
    .WriteTo.Logger(lc => lc // SECURITY → SecurityLog.log.
        .Filter.ByIncludingOnly(e => StructuredFileLogger.IsChannel(e, StructuredFileLogger.SecurityChannel))
        .WriteTo.File(Path.Combine(logsDir, "SecurityLog.log"), rollingInterval: RollingInterval.Infinite, shared: true, outputTemplate: fileTemplate))
    .WriteTo.Logger(lc => lc // WARNINGS → WarningsLog.log.
        .Filter.ByIncludingOnly(e => StructuredFileLogger.IsChannel(e, StructuredFileLogger.WarningsChannel))
        .WriteTo.File(Path.Combine(logsDir, "WarningsLog.log"), rollingInterval: RollingInterval.Infinite, shared: true, outputTemplate: fileTemplate))
    .WriteTo.Logger(lc => lc // FATALS + mức Fatal → FatalsLog.log.
        .Filter.ByIncludingOnly(e => StructuredFileLogger.IsChannel(e, StructuredFileLogger.FatalsChannel) || e.Level == LogEventLevel.Fatal)
        .WriteTo.File(Path.Combine(logsDir, "FatalsLog.log"), rollingInterval: RollingInterval.Infinite, shared: true, outputTemplate: fileTemplate))
    .WriteTo.Logger(lc => lc // ACTIVITIES → ActivitiesLog.log.
        .Filter.ByIncludingOnly(e => StructuredFileLogger.IsChannel(e, StructuredFileLogger.ActivitiesChannel))
        .WriteTo.File(Path.Combine(logsDir, "ActivitiesLog.log"), rollingInterval: RollingInterval.Infinite, shared: true, outputTemplate: fileTemplate))
    .WriteTo.Logger(lc => lc // ILogger từ pipeline filters (LogTrace / LogWarning) → FiltersLog.log.
        .Filter.ByIncludingOnly(StructuredFileLogger.IsPipelineFilterLog)
        .WriteTo.File(Path.Combine(logsDir, "FiltersLog.log"), rollingInterval: RollingInterval.Infinite, shared: true, outputTemplate: filtersLogTemplate));

if (builder.Environment.IsDevelopment())
{
    // BƯỚC — In ra console: SourceContext + message (gồm câu SQL từ EF).
    serilogCfg = serilogCfg.WriteTo.Console(
        restrictedToMinimumLevel: LogEventLevel.Information,
        outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}");
}

Log.Logger = serilogCfg.CreateLogger(); // Hoàn tất logger tĩnh.
builder.Host.UseSerilog(Log.Logger, dispose: true); // Host dùng cùng pipeline Serilog, flush khi shutdown.

// Dựng pipeline: host, middleware, endpoint — chưa lắng nghe.
var app = builder.Build();

// FATALS: bắt lỗi ngoài vòng pipeline (process sắp dừng).
AppDomain.CurrentDomain.UnhandledException += (_, e) => // Sự kiện CLR cuối cùng.
{
    if (e.ExceptionObject is Exception domainEx) // Có stack trace.
    {
        StructuredFileLogger.Fatals("AppDomain.UnhandledException", domainEx); // Ghi FatalsLog.
    }
    else // Không phải Exception.
    {
        StructuredFileLogger.Fatals($"AppDomain.UnhandledException: {e.ExceptionObject}", null); // Ghi mô tả thô.
    }

    Log.CloseAndFlush(); // Đẩy hết buffer file.
};

// Một lần khi start: tạo scope, ApplyPendingMigration, seed role Admin/User nếu chưa có.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync(); // Áp mọi migration còn treo
    await SeedData.SeedAsync(scope.ServiceProvider); // Vai trò, không seed user demo tùy implement
}

// Header hiệu năng (thời gian, SQL, cache) + X-Correlation-ID; phải sớm trong pipeline.
app.UseRequestPerformance();
// ACTIVITIES + AUDIT: bọc response trước exception handler để body lỗi vẫn vào buffer ghi log.
app.UseActivityAndAuditLogging();
// IExceptionHandler pipeline: bắt mọi exception chưa xử, trả JSON; delegate rỗng nếu dùng cấu hình mặc định.
app.UseExceptionHandler(_ => { });

// OpenAPI JSON + UI: một endpoint Swagger JSON cho mỗi version (v1, v2, …).
app.UseSwagger();
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

// Bắt buộc: UseRouting trước UseAuthentication/Authorization; custom JWT sau UseAuthentication.
app.UseRouting();
app.UseRateLimiter(); // Áp dụng limiter sau khi routing để lấy đúng endpoint metadata.
app.UseAuthentication(); // Đọc Bearer, gắn HttpContext.User
app.UseJwtAuthentication(); // Bắt buộc API /api (trừ login/refresh) có user đã xác thực
app.UseAuthorization(); // Áp dụng [Authorize], role, policy

// Trang gốc chuyển tới Swagger UI để thử nhanh API.
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

// Map controller attribute [Route], [ApiController], v.v.
app.MapControllers();

// Chạy Kestrel / IIS in-process, chặn cho tới khi process dừng; luôn flush Serilog.
try
{
    app.Run(); // Block tới shutdown.
}
finally
{
    Log.CloseAndFlush(); // Ghi nốt buffer sink file.
}

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
    } // Kết thúc ClearSchemaExamples.
} // Kết thúc lớp RemoveSwaggerExamplesDocumentFilter (bộ lọc gỡ Example khỏi OpenAPI).
