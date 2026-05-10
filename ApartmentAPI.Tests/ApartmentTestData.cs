using ApartmentAPI.Data;
using ApartmentAPI.Entities;

namespace ApartmentAPI.Tests;

// Dữ liệu tối thiểu cho InMemory: User (Identity), căn hộ, dịch vụ, hóa đơn, phản hồi — tránh lặp boilerplate trong từng test.
internal static class ApartmentTestData
{
    // Tạo User Identity đủ trường bắt buộc để EF và FK nghiệp vụ chấp nhận.
    public static async Task<User> AddUserAsync(AppDbContext db, int seed = 0)
    {
        var id = Guid.NewGuid();
        // Tên đăng nhập ngắn, duy nhất — tránh vượt giới hạn cột Identity.
        var userName = $"ut_{seed}_{id.ToString("N")[..10]}";
        if (userName.Length > 64)
            userName = userName[..64];

        var email = $"{seed}_{id:N}@unittest.local";
        var u = new User
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            FullName = $"Người thử {seed}",
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        };
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    // Căn hộ với cặp (Floor, RoomNumber) duy nhất theo seed.
    public static Apartment CreateApartment(int seed, ApartmentStatus status = ApartmentStatus.Available)
    {
        return new Apartment
        {
            Floor = 1 + seed % 50,
            RoomNumber = $"T{seed:D5}",
            Area = 50 + seed,
            Status = status,
            MaxResidents = 3,
            CreatedAt = DateTime.UtcNow.AddDays(-seed),
        };
    }

    public static async Task<Apartment> AddApartmentAsync(AppDbContext db, int seed, ApartmentStatus status = ApartmentStatus.Available)
    {
        var a = CreateApartment(seed, status);
        db.Apartments.Add(a);
        await db.SaveChangesAsync();
        return a;
    }

    public static UtilityService CreateUtility(int seed, bool active = true)
    {
        return new UtilityService
        {
            Name = $"DV thử {seed}",
            Price = 10 + seed,
            Unit = "kWh",
            IsActive = active,
            CreatedAt = DateTime.UtcNow.AddHours(-seed),
        };
    }

    public static async Task<UtilityService> AddUtilityAsync(AppDbContext db, int seed, bool active = true)
    {
        var u = CreateUtility(seed, active);
        db.UtilityServices.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    public static async Task<Invoice> AddInvoiceAsync(AppDbContext db, Guid apartmentId, string codeSuffix, InvoiceStatus status = InvoiceStatus.Unpaid)
    {
        var inv = new Invoice
        {
            InvoiceCode = $"INV-UT-{codeSuffix}",
            Month = 6,
            Year = 2024,
            IssueDate = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            DueDate = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            TotalAmount = 100,
            PaidAmount = status == InvoiceStatus.Paid ? 100 : 0,
            Status = status,
            ApartmentId = apartmentId,
            CreatedAt = DateTime.UtcNow,
        };
        db.Invoices.Add(inv);
        await db.SaveChangesAsync();
        return inv;
    }

    public static async Task<Feedback> AddFeedbackAsync(AppDbContext db, Guid userId, Guid? parentId, int seed, bool pinned = false)
    {
        var f = new Feedback
        {
            Content = $"Nội dung thử {seed}",
            UserId = userId,
            ParentId = parentId,
            IsPinned = pinned,
            IsResolved = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(-seed),
        };
        db.Feedbacks.Add(f);
        await db.SaveChangesAsync();
        return f;
    }
}
