using CommentAPI.DTOs;
using FluentValidation;

namespace CommentAPI.Validators;

public sealed class CreateCommentValidator : AbstractValidator<CreateCommentDto>
{
    public CreateCommentValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Content is required.")
            .MaximumLength(50_000).WithMessage("Content must not exceed 50000 characters.");
        RuleFor(x => x.PostId)
            .NotEmpty().WithMessage("Post id is required.");
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User id is required.");
    }
}

public sealed class UpdateCommentValidator : AbstractValidator<UpdateCommentDto>
{
    public UpdateCommentValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Content is required.")
            .MaximumLength(50_000).WithMessage("Content must not exceed 50000 characters.");
    }
}
