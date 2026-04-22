using CommentAPI.DTOs; 
using FluentValidation; 

namespace CommentAPI.Validators; 
public sealed class CreateCommentValidator : AbstractValidator<CreateCommentDto> // Tạo comment (thường Admin route).
{
    public CreateCommentValidator() // Rules.
    {
        RuleFor(x => x.Content) // Nội dung.
            .NotEmpty().WithMessage("Content is required.") // Bắt buộc.
            .MaximumLength(50_000).WithMessage("Content must not exceed 50000 characters."); // Trần hợp lý.
        RuleFor(x => x.PostId) // Thuộc post nào.
            .NotEmpty().WithMessage("Post id is required."); // Guid hợp lệ.
        RuleFor(x => x.UserId) // Tác giả.
            .NotEmpty().WithMessage("User id is required."); // Guid hợp lệ.
    }
}

public sealed class UpdateCommentValidator : AbstractValidator<UpdateCommentDto> // User sửa nội dung.
{
    public UpdateCommentValidator() // Rules.
    {
        RuleFor(x => x.Content) // Chỉ content.
            .NotEmpty().WithMessage("Content is required.") // Không rỗng.
            .MaximumLength(50_000).WithMessage("Content must not exceed 50000 characters."); // Trần.
    }
}

// Admin: gửi đủ PostId, UserId, ParentId (null nếu gốc) và nội dung cần hợp lệ.
public sealed class AdminUpdateCommentValidator : AbstractValidator<AdminUpdateCommentDto> // Admin chỉnh sửa đầy đủ liên kết.
{
    public AdminUpdateCommentValidator() // Rules.
    {
        RuleFor(x => x.Content) // Text.
            .NotEmpty().WithMessage("Content is required.") // Required.
            .MaximumLength(50_000).WithMessage("Content must not exceed 50000 characters."); // Max.
        RuleFor(x => x.PostId) // Scope post.
            .NotEmpty().WithMessage("Post id is required."); // Required FK.
        RuleFor(x => x.UserId) // Author may change.
            .NotEmpty().WithMessage("User id is required."); // Required FK.
    }
}
