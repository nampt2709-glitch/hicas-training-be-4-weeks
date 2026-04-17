using Microsoft.AspNetCore.Identity;

namespace CommentAPI.Data;

/// <summary>
/// Tạo vai trò Admin/User nếu chưa có. Không tạo tài khoản — admin do bạn tạo trực tiếp trên SQL Server / công cụ nội bộ.
/// </summary>
public static class SeedData
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        string[] roles = ["Admin", "User"];
        foreach (var roleName in roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(roleName) { Id = Guid.NewGuid() });
            }
        }
    }
}
