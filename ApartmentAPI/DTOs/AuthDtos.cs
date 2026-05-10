namespace ApartmentAPI.DTOs;

// Body đăng nhập — UserName + Password thô; validator/middleware redact khi log.
public class LoginRequestDto
{ // Mở khối LoginRequestDto.
    public string UserName { get; set; } = string.Empty; // Tên đăng nhập Identity.
    public string Password { get; set; } = string.Empty; // Mật khẩu — không log thô.
} // Kết thúc LoginRequestDto.

// Body đăng ký — Name + UserName + Password; Email tuỳ chọn.
public class SignUpRequestDto
{ // Mở khối SignUpRequestDto.
    public string Name { get; set; } = string.Empty; // Họ tên hiển thị / claim.
    public string UserName { get; set; } = string.Empty; // UserName tài khoản mới.
    public string Password { get; set; } = string.Empty; // Mật khẩu ban đầu.
    public string? Email { get; set; } // Email nullable — có thể null nếu không thu thập.
} // Kết thúc SignUpRequestDto.

// Body refresh — refresh token chuỗi một lần; đổi lấy cặp access mới.
public class RefreshRequestDto
{ // Mở khối RefreshRequestDto.
    public string RefreshToken { get; set; } = string.Empty; // Token hash lưu DB / cookie tùy flow.
} // Kết thúc RefreshRequestDto.

// Phản hồi phát hành JWT — access + refresh + mốc hết hạn UTC.
public class TokenResponseDto
{ // Mở khối TokenResponseDto.
    public string AccessToken { get; set; } = string.Empty; // Bearer JWT ngắn hạn.
    public string RefreshToken { get; set; } = string.Empty; // Token làm mới dài hạn (opaque hoặc JWT tùy design).
    public DateTime AccessTokenExpiresAtUtc { get; set; } // Hết hạn access — client có thể refresh sớm.
    public DateTime RefreshTokenExpiresAtUtc { get; set; } // Hết hạn refresh — buộc đăng nhập lại.
} // Kết thúc TokenResponseDto.
