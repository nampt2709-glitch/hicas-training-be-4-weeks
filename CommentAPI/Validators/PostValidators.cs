using CommentAPI.DTOs; 
using FluentValidation; 

namespace CommentAPI.Validators; 

public sealed class CreatePostValidator : AbstractValidator<CreatePostDto> // Tạo bài: đủ title, content, user.
{
    public CreatePostValidator() // Rule registration.
    {
        RuleFor(x => x.Title) // Tiêu đề.
            .NotEmpty().WithMessage("Title is required.") // Bắt buộc.
            .MaximumLength(500).WithMessage("Title must not exceed 500 characters."); // Trần cột.
        RuleFor(x => x.Content) // Nội dung.
            .NotEmpty().WithMessage("Content is required.") // Bắt buộc.
            .MaximumLength(100_000).WithMessage("Content must not exceed 100000 characters."); // Trần lớn cho bài dài.
        RuleFor(x => x.UserId) // Chủ bài (FK).
            .NotEmpty().WithMessage("User id is required."); // Guid không được Empty.
    }
}

public sealed class UpdatePostValidator : AbstractValidator<UpdatePostDto> // User/author cập nhật.
{
    public UpdatePostValidator() // Rules.
    {
        RuleFor(x => x.Title) // Tiêu đề mới.
            .NotEmpty().WithMessage("Title is required.") // Bắt buộc.
            .MaximumLength(500).WithMessage("Title must not exceed 500 characters."); // Trần.
        RuleFor(x => x.Content) // Nội dung mới.
            .NotEmpty().WithMessage("Content is required.") // Bắt buộc.
            .MaximumLength(100_000).WithMessage("Content must not exceed 100000 characters."); // Trần.
    }
}

// Admin: cùng quy tắc độ dài, UserId tùy chọn (null = không đổi).
public sealed class AdminUpdatePostValidator : AbstractValidator<AdminUpdatePostDto> // Admin có thể đổi UserId.
{
    public AdminUpdatePostValidator() // Rules.
    {
        RuleFor(x => x.Title) // Title.
            .NotEmpty().WithMessage("Title is required.") // Required.
            .MaximumLength(500).WithMessage("Title must not exceed 500 characters."); // Max.
        RuleFor(x => x.Content) // Body.
            .NotEmpty().WithMessage("Content is required.") // Required.
            .MaximumLength(100_000).WithMessage("Content must not exceed 100000 characters."); // Max.
    }
}
