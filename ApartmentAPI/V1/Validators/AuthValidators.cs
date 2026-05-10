using ApartmentAPI.DTOs; // LoginRequestDto, RefreshRequestDto, SignUpRequestDto.
using FluentValidation; // AbstractValidator, RuleFor, When.

namespace ApartmentAPI.V1.Validators;

// Đăng nhập — username + password dạng chuỗi giới hạn độ dài (không validate format user theo policy Identity ở đây).
public sealed class LoginRequestValidator : AbstractValidator<LoginRequestDto>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("Username is required.")
            .MaximumLength(256).WithMessage("Username must not exceed 256 characters.");
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MaximumLength(512).WithMessage("Password must not exceed 512 characters.");
    }
}

// Làm mới access — bắt buộc refresh token raw (client gửi body).
public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequestDto>
{
    public RefreshRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required.");
    }
}

// Đăng ký — name/username/password bắt buộc; email tuỳ chọn nhưng nếu có phải đúng định dạng.
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
