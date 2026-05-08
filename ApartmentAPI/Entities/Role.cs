using Microsoft.AspNetCore.Identity;

namespace ApartmentAPI.Entities;

// Vai trò — IdentityRole<Guid> + mô tả tùy chọn.
public class Role : IdentityRole<Guid>
{
    public string? Description { get; set; }
}
