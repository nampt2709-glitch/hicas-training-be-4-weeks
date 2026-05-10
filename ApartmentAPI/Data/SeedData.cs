using ApartmentAPI.Entities; // Role entity AspNetRoles map Entity Framework Identity.
using Microsoft.AspNetCore.Identity; // RoleManager để RoleExistsAsync / CreateAsync.

namespace ApartmentAPI.Data;

// Dữ liệu khởi đầu sau migration — chỉ vai trò hệ thống (Identity Role), không chèn User demo.
public static class SeedData
{
    // Đảm bảo các role cố định tồn tại; idempotent — có thể gọi lại sau mỗi deploy.
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var roleManager = services.GetRequiredService<RoleManager<Role>>();
        string[] roles = ["Admin", "User"]; // Vai trò căn bản để UserManager.AddToRoleAsync và policy phân quyền.

        foreach (var roleName in roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName)) // Chỉ tạo khi chưa có → tránh lỗi duplicate.
            {
                await roleManager.CreateAsync(new Role // Map sang bảng AspNetRoles trong SQL Server.
                {
                    Name = roleName,
                    Description = $"Role {roleName}", // Hiển thị quản trị / Swagger chú thích.
                });
            }
        }
    }
}
