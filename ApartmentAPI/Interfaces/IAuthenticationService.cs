using ApartmentAPI.DTOs; // Request/response DTO auth.

namespace ApartmentAPI.Interfaces;

// Nghiệp vụ đăng ký / đăng nhập / refresh / đăng xuất — orchestration repository + JWT.
public interface IAuthenticationService
{ // Mở khối IAuthenticationService.
    // Tạo user + phát hành cặp token ban đầu hoặc throw ApiException validation/conflict.
    Task<TokenResponseDto> SignUpAsync(SignUpRequestDto request, CancellationToken cancellationToken = default);
    // Xác thực UserName/Password — thành công trả TokenResponseDto; sai → LOGIN_FAILED.
    Task<TokenResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    // Đổi refresh hợp lệ lấy access mới (và có thể rotation refresh).
    Task<TokenResponseDto> RefreshAsync(RefreshRequestDto request, CancellationToken cancellationToken = default);
    // Vô hiệu hóa session theo userId (revoke tokens lưu DB).
    Task LogoutAsync(Guid userId, CancellationToken cancellationToken = default);
} // Kết thúc IAuthenticationService.
