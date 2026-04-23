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

// Đăng ký: cùng ngưỡng với CreateUserValidator (tên, đăng nhập, mật khẩu, email tuỳ chọn).
public sealed class SignUpRequestValidator : AbstractValidator<SignUpRequestDto>
{
    public SignUpRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(256).WithMessage("Name must not exceed 256 characters.");
        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("Username is required.")
            .MaximumLength(256).WithMessage("Username must not exceed 256 characters.");
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(6).WithMessage("Password must be at least 6 characters.")
            .MaximumLength(128).WithMessage("Password must not exceed 128 characters.");
        RuleFor(x => x.Email)
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters.")
            .EmailAddress().WithMessage("Email format is invalid.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
