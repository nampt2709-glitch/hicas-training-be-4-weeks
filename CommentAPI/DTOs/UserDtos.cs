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

// Admin: cập nhật đầy đủ hồ sơ Identity + Name + roles; không hỗ trợ đổi mật khẩu ở route admin update.
public class AdminUpdateUserDto
{
    public string Name { get; set; } = string.Empty; // Tên hiển thị nghiệp vụ.
    public string UserName { get; set; } = string.Empty; // Tên đăng nhập (phải unique toàn hệ).
    public string? Email { get; set; } // Email; null hoặc chỉ khoảng trắng → dùng synthetic {UserName}@users.local giống CreateUser.
    public List<string> Roles { get; set; } = new(); // Vai trò mới thay thế hoàn toàn (ví dụ User, hoặc Admin+User).
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

// Bản ghi bất biến: một dòng từ SELECT chiếu cột, không gồm cột bảo mật; init-only qua hàm tạo record.
public sealed record UserPageRow( // Từ khóa record gắn với primary constructor, sinh Equals/GetHashCode theo từng cột thành phần.

    Guid Id, // Khóa user, cột Id trên bảng Users, Guid duy nhất toàn bảng.

    string Name, // Tên hiển thị, không rỗng theo ràng buộc cấu hình entity khi tạo; đọc từ cột Name.

    string UserName, // Tên đăng nhập, map Identity UserName, dùng hiển thị danh sách/ chi tiết.

    string? Email, // Email tùy chọn, null nếu cột rỗng theo tài khoản.

    DateTime CreatedAt // Mốc tạo tài khoản, kiểu DateTime, thường lưu UTC ở lớp nghiệp vụ/ DB.
);
// Mỗi tham số tương ứng cột/ biểu thức trả về từ truy vấn phía repository, theo thứ tự khai báo ở trên.
