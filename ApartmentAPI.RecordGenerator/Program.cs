using ApartmentAPI.Data;
using ApartmentAPI.Entities;
using ApartmentAPI.RecordGenerator;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Host đọc appsettings; đăng ký DbContext + Identity giống API (không chạy Kestrel).
var builder = Host.CreateApplicationBuilder(args);

// BƯỚC 1 — DbContext SQL Server giống ApartmentAPI (không dùng lazy proxy — entity không khai báo virtual navigations).
var connectionString = builder.Configuration["ConnectionStrings:DefaultConnection"]
    ?? throw new InvalidOperationException("Thiếu ConnectionStrings:DefaultConnection trong appsettings.");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// BƯỚC 2 — Identity User/Role tùy chỉnh khớp ApartmentAPI (Role, không phải IdentityRole riêng).
builder.Services.AddIdentityCore<User>(options =>
    {
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.User.RequireUniqueEmail = false;
    })
    .AddRoles<Role>()
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddScoped<BulkRecordGenerator>();

using var host = builder.Build();

// BƯỚC 3 — Migration + seed vai trò Admin/User trước khi sinh dữ liệu.
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.SeedAsync(scope.ServiceProvider);
}

// BƯỚC 4 — Menu: chèn lô lớn hoặc cleanup theo marker/email.
while (true)
{
    Console.WriteLine();
    Console.WriteLine("ApartmentAPI.RecordGenerator — chọn thao tác:");
    Console.WriteLine("  1 = Insert lô lớn (~100k bản ghi nghiệp vụ + Identity), bỏ qua nếu đã có lô bulk");
    Console.WriteLine("  2 = Cleanup toàn bộ dữ liệu bulk (CreatedBy + user @bulkgen.recordgenerator.local)");
    Console.WriteLine("  0 = Thoát");
    Console.Write("> ");

    var line = Console.ReadLine();
    if (line is null || line.Trim() == "0")
    {
        break;
    }

    using var scope = host.Services.CreateScope();
    var gen = scope.ServiceProvider.GetRequiredService<BulkRecordGenerator>();

    try
    {
        if (line.Trim() == "1")
        {
            await gen.InsertIfNeededAsync(CancellationToken.None);
        }
        else if (line.Trim() == "2")
        {
            await gen.CleanupBulkAsync(CancellationToken.None);
        }
        else
        {
            Console.WriteLine("Lựa chọn không hợp lệ.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Lỗi: " + ex.Message);
    }
}
