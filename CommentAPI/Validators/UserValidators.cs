using CommentAPI.DTOs;
using FluentValidation; // Validator base.

namespace CommentAPI.Validators;

public sealed class CreateUserValidator : AbstractValidator<CreateUserDto> // Tạo user qua API Admin.
{
    public CreateUserValidator() // Rules.
    {
        RuleFor(x => x.Name) // Tên hiển thị.
            .NotEmpty().WithMessage("Name is required.") // Bắt buộc.
            .MaximumLength(256).WithMessage("Name must not exceed 256 characters."); // Trần DB/UI.
        RuleFor(x => x.UserName) // Login name.
            .NotEmpty().WithMessage("Username is required.") // Bắt buộc.
            .MaximumLength(256).WithMessage("Username must not exceed 256 characters."); // Giới hạn Identity.
        RuleFor(x => x.Password) // Mật khẩu ban đầu.
            .NotEmpty().WithMessage("Password is required.") // Bắt buộc.
            .MinimumLength(6).WithMessage("Password must be at least 6 characters.") // Ngưỡng tối thiểu đơn giản.
            .MaximumLength(128).WithMessage("Password must not exceed 128 characters."); // Trần đầu vào.
        RuleFor(x => x.Email) // Email tùy chọn nhưng nếu có phải hợp lệ.
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters.") // Độ dài.
            .EmailAddress().WithMessage("Email format is invalid.") // Định dạng.
            .When(x => !string.IsNullOrWhiteSpace(x.Email)); // Chỉ áp khi client gửi không rỗng.
    }
}

public sealed class UpdateUserValidator : AbstractValidator<UpdateUserDto> // Cập nhật tên (theo DTO hiện tại).
{
    public UpdateUserValidator() // Rules.
    {
        RuleFor(x => x.Name) // Chỉ validate Name.
            .NotEmpty().WithMessage("Name is required.") // Không được blank.
            .MaximumLength(256).WithMessage("Name must not exceed 256 characters."); // Trần.
    }
}

// Admin: đủ trường + roles; không xử lý đổi mật khẩu ở endpoint này.
public sealed class AdminUpdateUserValidator : AbstractValidator<AdminUpdateUserDto>
{
    public AdminUpdateUserValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(256).WithMessage("Name must not exceed 256 characters.");
        RuleFor(x => x.UserName)
            .NotEmpty().WithMessage("Username is required.")
            .MaximumLength(256).WithMessage("Username must not exceed 256 characters.");
        RuleFor(x => x.Email) // Tùy chọn: nếu gửi chuỗi không trắng thì phải là email hợp lệ.
            .MaximumLength(256).WithMessage("Email must not exceed 256 characters.")
            .EmailAddress().WithMessage("Email format is invalid.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Roles)
            .NotNull().WithMessage("Roles are required.")
            .NotEmpty().WithMessage("At least one role is required.");
        RuleForEach(x => x.Roles)
            .Must(r => r != null && (r.Trim().Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                                      r.Trim().Equals("User", StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Each role must be Admin or User.");
        RuleFor(x => x.Roles)
            .Must(roles => roles!
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .GroupBy(r => r.Trim(), StringComparer.OrdinalIgnoreCase)
                .All(g => g.Count() == 1))
            .When(x => x.Roles is { Count: > 0 })
            .WithMessage("Duplicate roles are not allowed.");
    }
}
