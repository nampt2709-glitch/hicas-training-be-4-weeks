using ApartmentAPI.V1.DTOs;
using ApartmentAPI.Entities;
using AutoMapper;

namespace ApartmentAPI.V1;

// Cấu hình AutoMapper: entity (DB) ↔ DTO trả về / nhận vào API.
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        // --- Căn hộ ---
        CreateMap<Apartment, ApartmentDto>();
        CreateMap<CreateApartmentDto, Apartment>();
        CreateMap<UpdateApartmentDto, Apartment>();

        // --- Cư dân ---
        CreateMap<Resident, ResidentDto>();
        CreateMap<CreateResidentDto, Resident>();
        CreateMap<UpdateResidentDto, Resident>();

        // --- Dịch vụ tiện ích ---
        CreateMap<UtilityService, UtilityServiceDto>();
        CreateMap<CreateUtilityServiceDto, UtilityService>();
        CreateMap<UpdateUtilityServiceDto, UtilityService>();

        // --- Hóa đơn ---
        CreateMap<Invoice, InvoiceDto>();
        CreateMap<CreateInvoiceDto, Invoice>();
        CreateMap<UpdateInvoiceDto, Invoice>();

        // --- Dòng hóa đơn ---
        CreateMap<InvoiceItem, InvoiceItemDto>();
        CreateMap<CreateInvoiceItemDto, InvoiceItem>();
        CreateMap<UpdateInvoiceItemDto, InvoiceItem>();

        // --- Phản hồi ---
        CreateMap<Feedback, FeedbackDto>();
        CreateMap<CreateFeedbackDto, Feedback>();
        CreateMap<UpdateFeedbackDto, Feedback>();

        // --- Đính kèm ---
        CreateMap<Attachment, AttachmentDto>();
        CreateMap<CreateAttachmentDto, Attachment>();
        CreateMap<UpdateAttachmentDto, Attachment>();

        // --- Refresh token ---
        CreateMap<RefreshToken, RefreshTokenDto>();
        CreateMap<CreateRefreshTokenDto, RefreshToken>();
        // Cập nhật refresh token: gán thủ công trong RefreshTokenService (tránh ghi đè bool mặc định).

        // --- User (Identity): chỉ map danh sách / không map mật khẩu ---
        CreateMap<User, UserListDto>();

        CreateMap<Role, RoleDto>();
    }
}
