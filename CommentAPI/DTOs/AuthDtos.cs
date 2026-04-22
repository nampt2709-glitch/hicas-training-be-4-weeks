namespace CommentAPI.DTOs;

// Lớp container cho yêu cầu đăng nhập: tên đăng nhập + mật khẩu, binding JSON từ body.
public class LoginRequestDto
{
    public string UserName { get; set; } = string.Empty; // Tên đăng nhập, chuỗi rỗng là mặc định an toàn trước gán từ client.
    public string Password { get; set; } = string.Empty; // Mật khẩu dạng văn bản thuần (HTTPS bảo vệ trên mạng), không lưu lại sau xử lý.
}

// DTO lấy thân refresh token: client gửi lại chuỗi refresh cũ để đổi access/refresh mới.
public class RefreshRequestDto
{
    public string RefreshToken { get; set; } = string.Empty; // Giá trị token refresh, mặc định rỗng; validator/ service kiểm tra không rỗng.
}

// Phản hồi sau khi phát hành token: access + refresh cùng mốc hết hạn UTC.
public class TokenResponseDto
{
    public string AccessToken { get; set; } = string.Empty; // JWT access, mặc định rỗng trước khi dịch vụ gán.
    public string RefreshToken { get; set; } = string.Empty; // Chuỗi refresh lưu phía client an toàn, mặc định rỗng.
    public DateTime AccessTokenExpiresAtUtc { get; set; } // Thời điểm hết hạn access theo giờ UTC, client có thể refresh sớm hơn.
    public DateTime RefreshTokenExpiresAtUtc { get; set; } // Thời điểm hết hạn refresh theo UTC, hết hạn thì cần đăng nhập lại.
}
