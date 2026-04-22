using CommentAPI.DTOs;
using FluentValidation; 

namespace CommentAPI.Validators; 

public sealed class LoginRequestValidator : AbstractValidator<LoginRequestDto> // Quy tắc cho body đăng nhập.
{
    public LoginRequestValidator() // Đăng ký rule trong constructor.
    {
        RuleFor(x => x.UserName) // Ràng buộc trường UserName.
            .NotEmpty().WithMessage("Username is required.") // Bắt buộc có giá trị.
            .MaximumLength(256).WithMessage("Username must not exceed 256 characters."); // Trần độ dài thực tế Identity.
        RuleFor(x => x.Password) // Ràng buộc mật khẩu.
            .NotEmpty().WithMessage("Password is required.") // Không được rỗng.
            .MaximumLength(512).WithMessage("Password must not exceed 512 characters."); // Giới hạn đầu vào (không phải policy hash).
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequestDto> // Quy tắc làm mới token.
{
    public RefreshRequestValidator() // Constructor rule set.
    {
        RuleFor(x => x.RefreshToken) // Token refresh phải có.
            .NotEmpty().WithMessage("Refresh token is required."); // Rỗng → 400 qua middleware validation.
    }
}
