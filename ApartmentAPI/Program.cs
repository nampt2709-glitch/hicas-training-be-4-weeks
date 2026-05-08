using System.Text;
using ApartmentAPI.Configuration;
using ApartmentAPI.Data;
using ApartmentAPI.Entities;
using ApartmentAPI.Middleware;
using ApartmentAPI.Repositories;
using ApartmentAPI.Services;
using ApartmentAPI.V1.Validators;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

// UTF-8 console: log tiếng Việt ổn định trên Windows.
Console.OutputEncoding = new UTF8Encoding(false);

// .env trước CreateBuilder — ConnectionStrings__DefaultConnection (cùng Docker SQL với CommentAPI).
EnvLoader.LoadEnvFilesBeforeHost();

// Host Web API: EF + Identity + AutoMapper + FluentValidation + Swagger + JWT (tùy chọn).
var builder = WebApplication.CreateBuilder(args);

EnvLoader.LoadEnvFile(builder.Environment.ContentRootPath);

// Cấu hình JWT (đọc từ appsettings); dùng khi bật Bearer.
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt section missing in appsettings.");

var sqlConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(sqlConnectionString))
{
    throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is missing or empty. Copy ApartmentAPI/.env.example to ApartmentAPI/.env and set ConnectionStrings__DefaultConnection (same host/port/sa as CommentAPI Docker), or use user secrets.");
}

// DbContext SQL Server + filter soft delete trong OnModelCreating.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(sqlConnectionString));

// Identity: User / Role (Guid); UserManager / RoleManager cho CRUD UsersController & RolesController.
builder.Services.AddIdentityCore<User>(options =>
    {
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.User.RequireUniqueEmail = false;
    })
    .AddRoles<Role>()
    .AddEntityFrameworkStores<AppDbContext>();

// JWT Bearer — mặc định controller không [Authorize] nên vẫn gọi được không cần token; sẵn sàng khi khóa route sau.
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey));
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
        };
    });

builder.Services.AddAuthorization();

// API Versioning: segment URL api/v{version}/... ; mặc định 1.0 khi không ghi rõ.
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

// ProblemDetails + xử lý ApiException / DbUpdateException thống nhất JSON.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<CreateApartmentValidator>();

builder.Services.AddAutoMapper(cfg =>
{
    // V2 hiện dùng chung DTO/validator/map V1; khi tách hợp đồng V2 thì bổ sung trong V2/MappingProfile.cs.
    cfg.AddProfile<ApartmentAPI.V1.MappingProfile>();
    cfg.AddProfile<ApartmentAPI.V2.MappingProfile>();
});

// Repository (scoped).
builder.Services.AddScoped<IApartmentRepository, ApartmentRepository>();
builder.Services.AddScoped<IResidentRepository, ResidentRepository>();
builder.Services.AddScoped<IUtilityServiceRepository, UtilityServiceRepository>();
builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
builder.Services.AddScoped<IInvoiceItemRepository, InvoiceItemRepository>();
builder.Services.AddScoped<IFeedbackRepository, FeedbackRepository>();
builder.Services.AddScoped<IAttachmentRepository, AttachmentRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();

// Service nghiệp vụ.
builder.Services.AddScoped<IApartmentService, ApartmentService>();
builder.Services.AddScoped<IResidentService, ResidentService>();
builder.Services.AddScoped<IUtilityCatalogService, UtilityCatalogService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IInvoiceItemService, InvoiceItemService>();
builder.Services.AddScoped<IFeedbackService, FeedbackService>();
builder.Services.AddScoped<IAttachmentService, AttachmentService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<IUserAppService, UserAppService>();
builder.Services.AddScoped<IRoleAppService, RoleAppService>();

builder.Services.AddControllers();
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
        Description = "JWT (nếu bật bảo vệ endpoint).",
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            },
            Array.Empty<string>()
        },
    });
});

var app = builder.Build();

// Áp migration + seed role Admin/User.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.SeedAsync(scope.ServiceProvider);
}

app.UseExceptionHandler(_ => { });
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
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
