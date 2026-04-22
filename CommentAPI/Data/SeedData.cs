// RoleManager, IdentityRole — dùng tạo vai trò tĩnh khi vừa chạy migration.
using Microsoft.AspNetCore.Identity;

// RoleManager, IdentityRole — tạo bản ghi AspNetRoles nếu chưa tồn tại.
namespace CommentAPI.Data;

// Hạt giống sau migration: chỉ tạo role Admin, User; không tạo user mặc định (tránh mật khẩu cứng trong repo).
public static class SeedData
{
    // Hạt giống vai trò: lặp, kiểm tra tồn tại, tạo nếu thiếu; hủy mặc định theo mặc nạ CancellationToken ở API.
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default) // IServiceProvider lấy RoleManager từ scope/ root.
    {
        // Lấy RoleManager đã cấu hình ở AddIdentity, kiểu Guid khớp cấu hình bảng AspNetRoles.
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>(); // Nếu thiếu service, sớm lỗi tại thời điểm seed.

        // Hai vai trò cố định chuỗi, phù hợp [Authorize(Roles=...)] trên controller.
        string[] roles = ["Admin", "User"]; // Mảng bất biến, vòng lặp foreach ở dưới tạo từng tên nếu chưa có bản ghi.
        foreach (var roleName in roles) // Duyệt từng tên, kiểm tra ở SQL qua API RoleManager.
        {
            if (!await roleManager.RoleExistsAsync(roleName)) // Nếu RoleExists false thì tạo mới, tránh lỗi trùng.
            {
                // Tạo IdentityRole với Id Guid mới, tên từ vòng lặp; bảng AspNetRoles mỗi dòng là một vai trò.
                await roleManager.CreateAsync(new IdentityRole<Guid>(roleName) { Id = Guid.NewGuid() }); // Id ngẫu nhiên mỗi môi trường, khớp cột Id Guid.
            }
        }
    }
}
