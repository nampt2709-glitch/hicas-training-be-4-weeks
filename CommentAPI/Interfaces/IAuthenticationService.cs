using CommentAPI.DTOs;

// Không gian tên: hợp đồng tầng dịch vụ, tách giao diện khỏi lớp cụ thể, — phục vụ thêm unit test, DI mô phỏng.
namespace CommentAPI.Interfaces;

// Hợp đồng: đăng nhập, làm mới, đăng xuất, — tầng ứng dụng, không nói tới cơ sở dữ liệu, — CancellationToken ủy từ HTTP.
public interface IAuthenticationService
{
    // Đăng ký: tạo tài khoản (role User), không email/OTP; trả cặp JWT như đăng nhập để client dùng ngay.
    Task<TokenResponseDto> SignUpAsync(SignUpRequestDto request, CancellationToken cancellationToken = default);

    Task<TokenResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default); // Nhận tên+ mật, trả cặp token + mốc hết hạn, — ngoại lệ tầng ở service nếu sai.
    Task<TokenResponseDto> RefreshAsync(RefreshRequestDto request, CancellationToken cancellationToken = default); // Dùng refresh, phát cặp access+refresh, — từ chối nếu token lạ, hết hạn, — revoke nếu cần.
    Task LogoutAsync(Guid userId, CancellationToken cancellationToken = default); // Vô hiệu refresh/ phiên, — tùy lưu trữ (store), — theo nghiệp vụ ở triển khai.
}
