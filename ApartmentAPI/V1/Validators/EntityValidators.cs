using System.Globalization; // InvariantCulture format message kích thước file upload.
using ApartmentAPI.V1.DTOs; // Toàn bộ Create/Update DTO + form model đính kèm.
using ApartmentAPI.Validators; // AttachmentBinarySignatures kiểm tra magic byte (namespace ApartmentAPI.Validators).
using FluentValidation; // AbstractValidator, RuleFor — quy tắc validate DTO/form.
using Microsoft.AspNetCore.Http; // IFormFile multipart.
using Microsoft.Extensions.Options; // IOptions AttachmentStorageOptions.

namespace ApartmentAPI.V1.Validators;

// Tập FluentValidation tự chạy nhờ AddFluentValidationAutoValidation — đăng ký assembly qua CreateApartmentValidator trong Program.cs.

// Validator tạo căn hộ: số phòng, tầng, diện tích, số cư dân tối đa.
public sealed class CreateApartmentValidator : AbstractValidator<CreateApartmentDto>
{
    public CreateApartmentValidator()
    {
        RuleFor(x => x.RoomNumber).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Floor).InclusiveBetween(-5, 200);
        RuleFor(x => x.Area).GreaterThan(0);
        RuleFor(x => x.MaxResidents).GreaterThanOrEqualTo(0);
    }
}

// Validator cập nhật căn hộ: cùng quy tắc với tạo.
public sealed class UpdateApartmentValidator : AbstractValidator<UpdateApartmentDto>
{
    public UpdateApartmentValidator()
    {
        RuleFor(x => x.RoomNumber).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Floor).InclusiveBetween(-5, 200);
        RuleFor(x => x.Area).GreaterThan(0);
        RuleFor(x => x.MaxResidents).GreaterThanOrEqualTo(0);
    }
}

// Validator cư dân: tên, CMND/CCCD, SĐT.
public sealed class CreateResidentValidator : AbstractValidator<CreateResidentDto>
{
    public CreateResidentValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(256);
        RuleFor(x => x.IdentityNumber).NotEmpty().MaximumLength(32);
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

// Cập nhật cư dân: khớp khối trường với CreateResidentDto.
public sealed class UpdateResidentValidator : AbstractValidator<UpdateResidentDto>
{
    public UpdateResidentValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(256);
        RuleFor(x => x.IdentityNumber).NotEmpty().MaximumLength(32);
        RuleFor(x => x.PhoneNumber).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

// Validator dịch vụ tiện ích: tên, giá, đơn vị.
public sealed class CreateUtilityServiceValidator : AbstractValidator<CreateUtilityServiceDto>
{
    public CreateUtilityServiceValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(64);
    }
}

// Cập nhật tiện ích: không đổi quy tắc khối tạo (name/price/unit).
public sealed class UpdateUtilityServiceValidator : AbstractValidator<UpdateUtilityServiceDto>
{
    public UpdateUtilityServiceValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Unit).NotEmpty().MaximumLength(64);
    }
}

// Validator hóa đơn: mã, tháng/năm, số tiền.
public sealed class CreateInvoiceValidator : AbstractValidator<CreateInvoiceDto>
{
    public CreateInvoiceValidator()
    {
        RuleFor(x => x.InvoiceCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.TotalAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PaidAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ApartmentId).NotEmpty();
    }
}

// Cập nhật hóa đơn: tái khẳng định FK ApartmentId và số học không âm.
public sealed class UpdateInvoiceValidator : AbstractValidator<UpdateInvoiceDto>
{
    public UpdateInvoiceValidator()
    {
        RuleFor(x => x.InvoiceCode).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.TotalAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PaidAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ApartmentId).NotEmpty();
    }
}

// Validator dòng hóa đơn: số lượng, đơn giá, thành tiền.
public sealed class CreateInvoiceItemValidator : AbstractValidator<CreateInvoiceItemDto>
{
    public CreateInvoiceItemValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.ServiceId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SubTotal).GreaterThanOrEqualTo(0);
    }
}

// Cập nhật dòng hóa đơn: giữ FK Invoice và Service không rỗng.
public sealed class UpdateInvoiceItemValidator : AbstractValidator<UpdateInvoiceItemDto>
{
    public UpdateInvoiceItemValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.ServiceId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SubTotal).GreaterThanOrEqualTo(0);
    }
}

// Validator phản hồi: nội dung, user.
public sealed class CreateFeedbackValidator : AbstractValidator<CreateFeedbackDto>
{
    public CreateFeedbackValidator()
    {
        RuleFor(x => x.Content).NotEmpty().MaximumLength(8000);
        RuleFor(x => x.UserId).NotEmpty();
    }
}

// Cập nhật feedback: chủ yếu nội dung và cờ Resolved/Pinned — không tái ép UserId từ DTO PUT.
public sealed class UpdateFeedbackValidator : AbstractValidator<UpdateFeedbackDto>
{
    public UpdateFeedbackValidator()
    {
        RuleFor(x => x.Content).NotEmpty().MaximumLength(8000);
    }
}

// Validator bài đăng: tiêu đề, nội dung, tác giả.
public sealed class CreatePostValidator : AbstractValidator<CreatePostDto>
{
    public CreatePostValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(512);
        RuleFor(x => x.Content).NotEmpty().MaximumLength(12000);
        RuleFor(x => x.UserId).NotEmpty();
    }
}

// Cập nhật bài đăng: không đổi UserId qua PUT cơ bản.
public sealed class UpdatePostValidator : AbstractValidator<UpdatePostDto>
{
    public UpdatePostValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(512);
        RuleFor(x => x.Content).NotEmpty().MaximumLength(12000);
    }
}

// Admin PUT feedback: đủ scalar; UserId không được Guid.Empty; ParentId tuỳ chọn (null = gốc).
public sealed class AdminUpdateFeedbackValidator : AbstractValidator<AdminUpdateFeedbackDto>
{
    public AdminUpdateFeedbackValidator()
    {
        RuleFor(x => x.Content).NotEmpty().MaximumLength(8000);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
    }
}

// Validator multipart tạo avatar: chỉ kiểm file (route đã cố định user đích).
public sealed class CreateAvatarAttachmentUploadValidator : AbstractValidator<AvatarAttachmentUploadModel>
{
    private static readonly HashSet<string> AllowedExt =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf" };

    public CreateAvatarAttachmentUploadValidator(IOptions<AttachmentStorageOptions> opt)
    {
        var max = opt.Value.MaxFileSizeBytes;

        RuleFor(x => x.File).NotNull().WithMessage(ApiMessages.AttachmentUploadFileRequired);

        RuleFor(x => x.File)
            .Must(f => f is { Length: > 0 })
            .When(x => x.File != null)
            .WithMessage(ApiMessages.AttachmentUploadFileEmpty);

        RuleFor(x => x.File)
            .Must(f => f == null || f!.Length <= max)
            .WithMessage(string.Format(CultureInfo.InvariantCulture, ApiMessages.AttachmentUploadFileTooLarge, max));

        RuleFor(x => x.File)
            .Must(f => f == null || IsAllowedContentType(f!))
            .WithMessage(ApiMessages.AttachmentUploadContentTypeNotAllowed);

        RuleFor(x => x.File)
            .Must(f => f == null || HasAllowedExtension(f!.FileName))
            .WithMessage(ApiMessages.AttachmentUploadExtensionNotAllowed);

        RuleFor(x => x.File)
            .MustAsync(async (f, ct) => f != null && await AttachmentBinarySignatures.IsValidUploadAsync(f, ct))
            .When(x => x.File != null)
            .WithMessage(ApiMessages.AttachmentUploadBinaryInvalid);
    }

    private static bool IsAllowedContentType(IFormFile f)
    {
        var ct = f.ContentType ?? "";
        return ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
               || ct.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAllowedExtension(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? "");
        return AllowedExt.Contains(ext);
    }
}

// Validator POST .../uploads/avatar — cùng quy tắc file với avatar; message client = ApiMessages (English).
public sealed class UploadsAvatarUploadValidator : AbstractValidator<UploadsAvatarUploadModel>
{
    private static readonly HashSet<string> AllowedExt =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf" };

    public UploadsAvatarUploadValidator(IOptions<AttachmentStorageOptions> opt)
    {
        var max = opt.Value.MaxFileSizeBytes;

        RuleFor(x => x.File).NotNull().WithMessage(ApiMessages.UploadAvatarNoFile);

        RuleFor(x => x.File)
            .Must(f => f is { Length: > 0 })
            .When(x => x.File != null)
            .WithMessage(ApiMessages.UploadAvatarNoFile);

        RuleFor(x => x.File)
            .Must(f => f == null || f!.Length <= max)
            .WithMessage(string.Format(CultureInfo.InvariantCulture, ApiMessages.AttachmentUploadFileTooLarge, max));

        RuleFor(x => x.File)
            .Must(f => f == null || UploadsAvatarIsAllowedContentType(f!))
            .WithMessage(ApiMessages.AttachmentUploadContentTypeNotAllowed);

        RuleFor(x => x.File)
            .Must(f => f == null || UploadsAvatarHasAllowedExtension(f!.FileName))
            .WithMessage(ApiMessages.AttachmentUploadExtensionNotAllowed);

        RuleFor(x => x.File)
            .MustAsync(async (f, ct) => f != null && await AttachmentBinarySignatures.IsValidUploadAsync(f, ct))
            .When(x => x.File != null)
            .WithMessage(ApiMessages.AttachmentUploadBinaryInvalid);
    }

    private static bool UploadsAvatarIsAllowedContentType(IFormFile f)
    {
        var ct = f.ContentType ?? "";
        return ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
               || ct.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool UploadsAvatarHasAllowedExtension(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? "");
        return AllowedExt.Contains(ext);
    }
}

// Validator multipart tạo file gắn feedback: cùng quy tắc file với avatar.
public sealed class CreateFeedbackAttachmentUploadValidator : AbstractValidator<FeedbackAttachmentUploadModel>
{
    private static readonly HashSet<string> AllowedExt =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf" };

    public CreateFeedbackAttachmentUploadValidator(IOptions<AttachmentStorageOptions> opt)
    {
        var max = opt.Value.MaxFileSizeBytes;

        RuleFor(x => x.File).NotNull().WithMessage(ApiMessages.AttachmentUploadFileRequired);

        RuleFor(x => x.File)
            .Must(f => f is { Length: > 0 })
            .When(x => x.File != null)
            .WithMessage(ApiMessages.AttachmentUploadFileEmpty);

        RuleFor(x => x.File)
            .Must(f => f == null || f!.Length <= max)
            .WithMessage(string.Format(CultureInfo.InvariantCulture, ApiMessages.AttachmentUploadFileTooLarge, max));

        RuleFor(x => x.File)
            .Must(f => f == null || IsAllowedContentType(f!))
            .WithMessage(ApiMessages.AttachmentUploadContentTypeNotAllowed);

        RuleFor(x => x.File)
            .Must(f => f == null || HasAllowedExtension(f!.FileName))
            .WithMessage(ApiMessages.AttachmentUploadExtensionNotAllowed);

        RuleFor(x => x.File)
            .MustAsync(async (f, ct) => f != null && await AttachmentBinarySignatures.IsValidUploadAsync(f, ct))
            .When(x => x.File != null)
            .WithMessage(ApiMessages.AttachmentUploadBinaryInvalid);
    }

    private static bool IsAllowedContentType(IFormFile f)
    {
        var ct = f.ContentType ?? "";
        return ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
               || ct.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAllowedExtension(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? "");
        return AllowedExt.Contains(ext);
    }
}

// Validator multipart tạo file gắn bài đăng: cùng quy tắc file với avatar/feedback.
public sealed class CreatePostAttachmentUploadValidator : AbstractValidator<PostAttachmentUploadModel>
{
    private static readonly HashSet<string> AllowedExt =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf" };

    public CreatePostAttachmentUploadValidator(IOptions<AttachmentStorageOptions> opt)
    {
        var max = opt.Value.MaxFileSizeBytes;

        RuleFor(x => x.File).NotNull().WithMessage(ApiMessages.AttachmentUploadFileRequired);

        RuleFor(x => x.File)
            .Must(f => f is { Length: > 0 })
            .When(x => x.File != null)
            .WithMessage(ApiMessages.AttachmentUploadFileEmpty);

        RuleFor(x => x.File)
            .Must(f => f == null || f!.Length <= max)
            .WithMessage(string.Format(CultureInfo.InvariantCulture, ApiMessages.AttachmentUploadFileTooLarge, max));

        RuleFor(x => x.File)
            .Must(f => f == null || IsAllowedContentType(f!))
            .WithMessage(ApiMessages.AttachmentUploadContentTypeNotAllowed);

        RuleFor(x => x.File)
            .Must(f => f == null || HasAllowedExtension(f!.FileName))
            .WithMessage(ApiMessages.AttachmentUploadExtensionNotAllowed);

        RuleFor(x => x.File)
            .MustAsync(async (f, ct) => f != null && await AttachmentBinarySignatures.IsValidUploadAsync(f, ct))
            .When(x => x.File != null)
            .WithMessage(ApiMessages.AttachmentUploadBinaryInvalid);
    }

    private static bool IsAllowedContentType(IFormFile f)
    {
        var ct = f.ContentType ?? "";
        return ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
               || ct.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAllowedExtension(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? "");
        return AllowedExt.Contains(ext);
    }
}

// Cập nhật avatar attachment: khi có file mới thì cùng quy tắc MIME/size/extension.
public sealed class UpdateAvatarAttachmentFormValidator : AbstractValidator<UpdateAvatarAttachmentFormModel>
{
    private static readonly HashSet<string> AllowedExt =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf" };

    public UpdateAvatarAttachmentFormValidator(IOptions<AttachmentStorageOptions> opt)
    {
        var max = opt.Value.MaxFileSizeBytes;

        When(x => x.File != null, () =>
        {
            RuleFor(x => x.File!)
                .Must(f => f.Length > 0)
                .WithMessage(ApiMessages.AttachmentUploadFileEmpty);

            RuleFor(x => x.File!)
                .Must(f => f.Length <= max)
                .WithMessage(string.Format(CultureInfo.InvariantCulture, ApiMessages.AttachmentUploadFileTooLarge, max));

            RuleFor(x => x.File!)
                .Must(IsAllowedContentType)
                .WithMessage(ApiMessages.AttachmentUploadContentTypeNotAllowed);

            RuleFor(x => x.File!)
                .Must(f => HasAllowedExtension(f.FileName))
                .WithMessage(ApiMessages.AttachmentUploadExtensionNotAllowed);

            RuleFor(x => x.File!)
                .MustAsync(async (f, ct) => await AttachmentBinarySignatures.IsValidUploadAsync(f, ct))
                .WithMessage(ApiMessages.AttachmentUploadBinaryInvalid);
        });
    }

    private static bool IsAllowedContentType(IFormFile f)
    {
        var ct = f.ContentType ?? "";
        return ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
               || ct.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAllowedExtension(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? "");
        return AllowedExt.Contains(ext);
    }
}

// Cập nhật attachment gắn feedback: bắt buộc feedbackId hợp lệ + quy tắc file nếu gửi file mới.
public sealed class UpdateFeedbackAttachmentFormValidator : AbstractValidator<UpdateFeedbackAttachmentFormModel>
{
    private static readonly HashSet<string> AllowedExt =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf" };

    public UpdateFeedbackAttachmentFormValidator(IOptions<AttachmentStorageOptions> opt)
    {
        var max = opt.Value.MaxFileSizeBytes;

        RuleFor(x => x.FeedbackId)
            .Must(id => id.HasValue && id.Value != Guid.Empty)
            .WithMessage(ApiMessages.AttachmentFeedbackIdRequired);

        When(x => x.File != null, () =>
        {
            RuleFor(x => x.File!)
                .Must(f => f.Length > 0)
                .WithMessage(ApiMessages.AttachmentUploadFileEmpty);

            RuleFor(x => x.File!)
                .Must(f => f.Length <= max)
                .WithMessage(string.Format(CultureInfo.InvariantCulture, ApiMessages.AttachmentUploadFileTooLarge, max));

            RuleFor(x => x.File!)
                .Must(IsAllowedContentType)
                .WithMessage(ApiMessages.AttachmentUploadContentTypeNotAllowed);

            RuleFor(x => x.File!)
                .Must(f => HasAllowedExtension(f.FileName))
                .WithMessage(ApiMessages.AttachmentUploadExtensionNotAllowed);

            RuleFor(x => x.File!)
                .MustAsync(async (f, ct) => await AttachmentBinarySignatures.IsValidUploadAsync(f, ct))
                .WithMessage(ApiMessages.AttachmentUploadBinaryInvalid);
        });
    }

    private static bool IsAllowedContentType(IFormFile f)
    {
        var ct = f.ContentType ?? "";
        return ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
               || ct.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAllowedExtension(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? "");
        return AllowedExt.Contains(ext);
    }
}

// Cập nhật attachment gắn bài đăng: bắt buộc postId hợp lệ + quy tắc file nếu gửi file mới.
public sealed class UpdatePostAttachmentFormValidator : AbstractValidator<UpdatePostAttachmentFormModel>
{
    private static readonly HashSet<string> AllowedExt =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf" };

    public UpdatePostAttachmentFormValidator(IOptions<AttachmentStorageOptions> opt)
    {
        var max = opt.Value.MaxFileSizeBytes;

        RuleFor(x => x.PostId)
            .Must(id => id.HasValue && id.Value != Guid.Empty)
            .WithMessage(ApiMessages.AttachmentPostIdRequired);

        When(x => x.File != null, () =>
        {
            RuleFor(x => x.File!)
                .Must(f => f.Length > 0)
                .WithMessage(ApiMessages.AttachmentUploadFileEmpty);

            RuleFor(x => x.File!)
                .Must(f => f.Length <= max)
                .WithMessage(string.Format(CultureInfo.InvariantCulture, ApiMessages.AttachmentUploadFileTooLarge, max));

            RuleFor(x => x.File!)
                .Must(IsAllowedContentType)
                .WithMessage(ApiMessages.AttachmentUploadContentTypeNotAllowed);

            RuleFor(x => x.File!)
                .Must(f => HasAllowedExtension(f.FileName))
                .WithMessage(ApiMessages.AttachmentUploadExtensionNotAllowed);

            RuleFor(x => x.File!)
                .MustAsync(async (f, ct) => await AttachmentBinarySignatures.IsValidUploadAsync(f, ct))
                .WithMessage(ApiMessages.AttachmentUploadBinaryInvalid);
        });
    }

    private static bool IsAllowedContentType(IFormFile f)
    {
        var ct = f.ContentType ?? "";
        return ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
               || ct.Equals("application/pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAllowedExtension(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? "");
        return AllowedExt.Contains(ext);
    }
}

// Validator refresh token: hash, hạn dùng, user.
public sealed class CreateRefreshTokenValidator : AbstractValidator<CreateRefreshTokenDto>
{
    public CreateRefreshTokenValidator()
    {
        RuleFor(x => x.TokenHash).NotEmpty().MaximumLength(512);
        RuleFor(x => x.UserId).NotEmpty();
    }
}

// Validator tài khoản Identity: username, email, mật khẩu.
public sealed class CreateUserValidator : AbstractValidator<CreateUserDto>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(6);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(512);
    }
}

// Cập nhật user: chỉ FullName và metadata hiển thị (đổi mật khẩu theo luồng khác trong service riêng nếu có).
public sealed class UpdateUserValidator : AbstractValidator<UpdateUserDto>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(512);
    }
}

// Validator vai trò: tên role.
public sealed class CreateRoleValidator : AbstractValidator<CreateRoleDto>
{
    public CreateRoleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
    }
}

// Cập nhật role: chủ yếu Description (Name role thường cố định sau Seed).
public sealed class UpdateRoleValidator : AbstractValidator<UpdateRoleDto>
{
    public UpdateRoleValidator()
    {
        // Mô tả tuỳ chọn; không bắt buộc.
    }
}
