using System.Reflection; // GetMethod + MakeGenericMethod cho filter generic.
using ApartmentAPI.Entities; // DbSet entity nghiệp vụ + BaseEntity.
using Microsoft.AspNetCore.Identity.EntityFrameworkCore; // IdentityDbContext<User, Role, Guid>.
using Microsoft.EntityFrameworkCore; // ModelBuilder, HasQueryFilter, SaveChanges.

namespace ApartmentAPI.Data;

// File ở thư mục gốc project (cùng kiểu CommentAPI); namespace vẫn ApartmentAPI.Data cho EF/Migrations.
// DbContext: Identity + entity căn hộ; global query filter soft delete cho BaseEntity.
public class AppDbContext : IdentityDbContext<User, Role, Guid>
{ // Mở khối AppDbContext.
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    { // Constructor — options (connection, provider) do DI inject.
    }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>(); // Phiên làm mới JWT.
    public DbSet<Apartment> Apartments => Set<Apartment>(); // Căn hộ.
    public DbSet<Resident> Residents => Set<Resident>(); // Cư dân.
    public DbSet<UtilityService> UtilityServices => Set<UtilityService>(); // Dịch vụ tiện ích.
    public DbSet<Invoice> Invoices => Set<Invoice>(); // Hóa đơn.
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>(); // Dòng hóa đơn.
    public DbSet<Feedback> Feedbacks => Set<Feedback>(); // Phản hồi (cây).
    public DbSet<Attachment> Attachments => Set<Attachment>(); // Tệp đính kèm.

    protected override void OnModelCreating(ModelBuilder builder)
    { // Mở khối OnModelCreating — cấu hình Fluent API.
        // BƯỚC 0 — Đăng ký bảng Identity (AspNetUsers, Roles, v.v.).
        base.OnModelCreating(builder);

        // BƯỚC 1 — Mỗi lớp kế thừa BaseEntity: chỉ truy vấn bản ghi chưa xóa mềm.
        ApplySoftDeleteFilters(builder);

        // BƯỚC 2 — Feedback: cây cha–con, không cascade xóa vòng.
        builder.Entity<Feedback>()
            .HasOne(f => f.Parent) // Nút cha.
            .WithMany(p => p.Children) // Con.
            .HasForeignKey(f => f.ParentId) // FK nullable.
            .OnDelete(DeleteBehavior.Restrict); // Không tự xóa lan trong SQL Server.

        // BƯỚC 3 — Hóa đơn: mã duy nhất.
        builder.Entity<Invoice>()
            .HasIndex(i => i.InvoiceCode)
            .IsUnique();

        // BƯỚC 4 — Căn hộ: (Tầng + Số phòng) duy nhất.
        builder.Entity<Apartment>()
            .HasIndex(a => new { a.Floor, a.RoomNumber })
            .IsUnique();
    } // Kết thúc OnModelCreating.

    // Gắn HasQueryFilter cho mọi kiểu assignable-to-BaseEntity (không gồm abstract/BaseEntity nếu không có DbSet).
    private static void ApplySoftDeleteFilters(ModelBuilder builder)
    { // Mở khối ApplySoftDeleteFilters.
        // BƯỚC 1 — Duyệt mọi entity type trong model.
        foreach (var et in builder.Model.GetEntityTypes())
        {
            var type = et.ClrType;
            // TRƯỜNG HỢP A — Bỏ qua abstract hoặc không phải BaseEntity.
            if (type is null || type.IsAbstract || !typeof(BaseEntity).IsAssignableFrom(type))
                continue;
            // BƯỚC 2 — Reflection gọi SetSoftDeleteFilter<TEntity> cho đúng kiểu runtime.
            var method = typeof(AppDbContext).GetMethod(nameof(SetSoftDeleteFilter), BindingFlags.NonPublic | BindingFlags.Static);
            method?.MakeGenericMethod(type).Invoke(null, new object[] { builder });
        }
    } // Kết thúc ApplySoftDeleteFilters.

    // Áp filter toàn cục: mọi query mặc định loại IsDeleted.
    private static void SetSoftDeleteFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : BaseEntity
    { // Mở khối SetSoftDeleteFilter.
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => !e.IsDeleted); // Chỉ bản ghi “sống”.
    } // Kết thúc SetSoftDeleteFilter.

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    { // Mở khối SaveChangesAsync — hook cập nhật audit trước khi flush DB.
        // BƯỚC 1 — Với mọi BaseEntity đang Modified: gán UpdatedAt = UtcNow.
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State != EntityState.Modified)
                continue;
            entry.Entity.UpdatedAt = DateTime.UtcNow; // Timestamp sửa cuối.
        }

        // BƯỚC 2 — Gọi EF SaveChangesAsync thực sự.
        return base.SaveChangesAsync(cancellationToken);
    } // Kết thúc SaveChangesAsync.
} // Kết thúc AppDbContext.
