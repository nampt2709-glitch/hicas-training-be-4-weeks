namespace CommentAPI.DTOs; // Không gian tên DTO: hợp đồng JSON cho thao tác user.

// Mô hình tạo user mới: tên, đăng nhập, mật khẩu, email tùy chọn — map sang Identity + bảng Users.
public class CreateUserDto
{
    public string Name { get; set; } = string.Empty; // Tên hiển thị nghiệp vụ, mặc định rỗng trước gán; validator bắt rỗng nếu cần.
    public string UserName { get; set; } = string.Empty; // Tên đăng nhập trùng quy ước Identity, mặc định rỗng.
    public string Password { get; set; } = string.Empty; // Mật khẩu thuần, service băm; không bao giờ lưu thuân vào DB.
    public string? Email { get; set; } // Email tùy chọn, null nghĩa là không cung cấp.
}

// Mô hình cập nhật: chỉ tên, các trường khác đổi ở endpoint hoặc Identity khác nếu có.
public class UpdateUserDto
{
    public string Name { get; set; } = string.Empty; // Tên mới, mặc định rỗng; cập nhật có điều kiện nghiệp vụ.
}

// Admin: cập nhật đầy đủ hồ sơ Identity + Name + roles; mật khẩu mới tùy chọn (để trống = giữ mật khẩu cũ).
public class AdminUpdateUserDto
{
    public string Name { get; set; } = string.Empty; // Tên hiển thị nghiệp vụ.
    public string UserName { get; set; } = string.Empty; // Tên đăng nhập (phải unique toàn hệ).
    public string? Email { get; set; } // Email; null hoặc chỉ khoảng trắng → dùng synthetic {UserName}@users.local giống CreateUser.
    public List<string> Roles { get; set; } = new(); // Vai trò mới thay thế hoàn toàn (ví dụ User, hoặc Admin+User).
    public string? NewPassword { get; set; } // Đặt lại mật khẩu; null/rỗng = không đổi.
}

// DTO trả user ra client: không gồm mật khẩu; có danh sách tên role dạng chuỗi.
public class UserDto
{
    public Guid Id { get; set; } // Id định danh, Guid đồng bộ bảng Users.
    public string Name { get; set; } = string.Empty; // Tên hiển thị, mặc định rỗng nếu chưa gán từ entity.
    public string UserName { get; set; } = string.Empty; // Tên đăng nhập trả về, đọc từ entity Identity.
    public string? Email { get; set; } // Email, có thể null; không bắt buộc cho mọi tài khoản.
    public List<string> Roles { get; set; } = new(); // Danh sách tên role (Admin, User, …) lấy từ AspNetUserRoles join Roles.
    public DateTime CreatedAt { get; set; } // Mốc tạo bản ghi user nghiệp vụ, dùng hiển thị.
}
