using ApartmentAPI.V1.DTOs;
using ApartmentAPI.Entities;
using FluentValidation;

namespace ApartmentAPI.V1.Validators;

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

public sealed class UpdateFeedbackValidator : AbstractValidator<UpdateFeedbackDto>
{
    public UpdateFeedbackValidator()
    {
        RuleFor(x => x.Content).NotEmpty().MaximumLength(8000);
    }
}

// Validator đính kèm: tên file, đường dẫn lưu, MIME.
public sealed class CreateAttachmentValidator : AbstractValidator<CreateAttachmentDto>
{
    public CreateAttachmentValidator()
    {
        RuleFor(x => x.OriginalFileName).NotEmpty().MaximumLength(512);
        RuleFor(x => x.StoredFileName).NotEmpty().MaximumLength(512);
        RuleFor(x => x.FilePath).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(256);
        RuleFor(x => x.FileSize).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateAttachmentValidator : AbstractValidator<UpdateAttachmentDto>
{
    public UpdateAttachmentValidator()
    {
        RuleFor(x => x.OriginalFileName).NotEmpty().MaximumLength(512);
        RuleFor(x => x.StoredFileName).NotEmpty().MaximumLength(512);
        RuleFor(x => x.FilePath).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(256);
        RuleFor(x => x.FileSize).GreaterThanOrEqualTo(0);
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

public sealed class UpdateRoleValidator : AbstractValidator<UpdateRoleDto>
{
    public UpdateRoleValidator()
    {
        // Mô tả tuỳ chọn; không bắt buộc.
    }
}
