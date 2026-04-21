using CommentAPI.Data;
using CommentAPI.Entities;
using CommentAPI.RecordGenerator;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Host chuẩn để đọc appsettings và đăng ký DbContext + Identity giống API (không chạy Kestrel).
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseLazyLoadingProxies()
        .UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentityCore<User>(options =>
    {
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.User.RequireUniqueEmail = false;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddScoped<BulkRecordGenerator>();

using var host = builder.Build();

// Đảm bảo schema và vai trò Admin/User tồn tại trước khi sinh dữ liệu.
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await SeedData.SeedAsync(scope.ServiceProvider);
}

// Vòng menu đơn giản: insert có kiểm tra trùng, cleanup dùng SQL có thứ tự an toàn.
while (true)
{
    Console.WriteLine();
    Console.WriteLine("CommentAPI.RecordGenerator — chọn thao tác:");
    Console.WriteLine("  1 = Insert ~100k bản ghi (Users + Posts + Comments), bỏ qua nếu đã có lô bulk");
    Console.WriteLine("  2 = Cleanup toàn bộ user/post/comment có email @bulkgen.recordgenerator.local");
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
