using ApartmentAPI.Entities; // Thực thể EF làm source/destination của map.
using ApartmentAPI.V1.DTOs; // DTO request/response API phiên bản Url V1.
using AutoMapper; // CreateMap ReverseMap không dùng tại đây — map một chiều như trong constructor.

namespace ApartmentAPI.V1;

// Profile AutoMapper: ép kiểu entity (DB) ↔ DTO cửa REST V1 trong CreateMap — giữ mapper tập trung trong một file.
public class MappingProfile : Profile // Profile được AddAutoMapper trong Program.cs đăng ký.
{
    public MappingProfile()
    {
        // --- Căn hộ ---
        CreateMap<Apartment, ApartmentDto>();
        CreateMap<CreateApartmentDto, Apartment>(); // POST: DTO → entity mới (Id do DB sinh).
        CreateMap<UpdateApartmentDto, Apartment>(); // PUT: ghi đè scalar (service gán Id thủ công).

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
        CreateMap<Attachment, AttachmentDto>(); // PUT multipart có logic map thủ công phần file trong AttachmentService.

        // --- Refresh token ---
        CreateMap<RefreshToken, RefreshTokenDto>();
        CreateMap<CreateRefreshTokenDto, RefreshToken>();
        // Cập nhật refresh token: gán cờ revoked/hạn trong RefreshTokenService (tránh overwrite bool/phụ thuộc default của runtime).

        // --- User (Identity): chỉ map danh sách / không map mật khẩu ---
        CreateMap<User, UserListDto>();

        CreateMap<Role, RoleDto>();
    }
}
