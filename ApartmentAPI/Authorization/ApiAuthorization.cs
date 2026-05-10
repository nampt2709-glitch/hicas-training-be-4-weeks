namespace ApartmentAPI.Authorization;

// Hằng chuỗi role dùng thống nhất trong [Authorize(Roles = ...)] để tránh lệch tên vai trò với seed Identity.
public static class ApiAuthorization
{
    // Chỉ Admin: quản lý user Identity, quyền (role), danh sách refresh token — không cho User thường.
    public const string AdminOnly = "Admin";

    // Admin hoặc User: nghiệp vụ căn hộ, cư dân, hóa đơn, đính kèm, phản hồi (trừ route chỉnh sửa admin riêng).
    public const string AdminOrUser = "Admin,User";
}
