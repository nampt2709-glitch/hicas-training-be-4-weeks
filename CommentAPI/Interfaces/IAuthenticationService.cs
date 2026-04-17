using CommentAPI.DTOs;

namespace CommentAPI.Interfaces;

public interface IAuthenticationService
{
    Task<TokenResponseDto?> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default);
    Task<TokenResponseDto?> RefreshAsync(RefreshRequestDto request, CancellationToken cancellationToken = default);
    Task LogoutAsync(Guid userId, CancellationToken cancellationToken = default);
}
