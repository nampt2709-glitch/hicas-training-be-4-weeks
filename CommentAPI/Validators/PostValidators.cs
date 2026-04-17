using CommentAPI.DTOs;
using FluentValidation;

namespace CommentAPI.Validators;

public sealed class CreatePostValidator : AbstractValidator<CreatePostDto>
{
    public CreatePostValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(500).WithMessage("Title must not exceed 500 characters.");
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Content is required.")
            .MaximumLength(100_000).WithMessage("Content must not exceed 100000 characters.");
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User id is required.");
    }
}

public sealed class UpdatePostValidator : AbstractValidator<UpdatePostDto>
{
    public UpdatePostValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(500).WithMessage("Title must not exceed 500 characters.");
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Content is required.")
            .MaximumLength(100_000).WithMessage("Content must not exceed 100000 characters.");
    }
}
