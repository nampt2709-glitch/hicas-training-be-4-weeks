using ApartmentAPI.Entities;
using Microsoft.AspNetCore.Identity;

namespace ApartmentAPI.Data;

// Hạt giống sau migration: vai trò Admin / User cho Identity (Role tùy chỉnh).
public static class SeedData
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var roleManager = services.GetRequiredService<RoleManager<Role>>();
        string[] roles = ["Admin", "User"];
        foreach (var roleName in roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new Role
                {
                    Name = roleName,
                    Description = $"Vai trò {roleName}",
                });
            }
        }
    }
}
