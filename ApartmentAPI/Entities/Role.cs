// File: entity vai trò — kế thừa IdentityRole<Guid> + mô tả hiển thị nội bộ.
using Microsoft.AspNetCore.Identity; // IdentityRole<Guid>.

namespace ApartmentAPI.Entities;

// Vai trò ASP.NET Identity: Name/NormalizedName từ lớp cơ sở; thêm Description tuỳ chọn.
public class Role : IdentityRole<Guid>
{ // Mở khối Role.
    public string? Description { get; set; } // Mô tả vai trò cho admin/UI.
} // Kết thúc Role.
