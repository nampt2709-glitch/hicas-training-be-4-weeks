using AutoMapper; // Profile rỗng vẫn dùng chung entity + DTO V1 trong controller V2 đến khi có nhánh DTO V2 riêng.

namespace ApartmentAPI.V2;

// Profile AutoMapper cho V2: hiện chưa khai báo CreateMap riêng (tránh drift). Thêm CreateMap khi có hậu tốDto V2 trong namespace ApartmentAPI.V2.DTOs.
public class MappingProfile : Profile // Được AddAutoMapper cùng V1.Profile — phải tồn tại ctor public rỗng.
{
    public MappingProfile()
    {
    }
}
