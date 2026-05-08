using System.Reflection;
using ApartmentAPI.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ApartmentAPI.Data;

// File ở thư mục gốc project (cùng kiểu CommentAPI); namespace vẫn ApartmentAPI.Data cho EF/Migrations.
// DbContext: Identity + entity căn hộ; global query filter soft delete cho BaseEntity.
public class AppDbContext : IdentityDbContext<User, Role, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Apartment> Apartments => Set<Apartment>();
    public DbSet<Resident> Residents => Set<Resident>();
    public DbSet<UtilityService> UtilityServices => Set<UtilityService>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<Attachment> Attachments => Set<Attachment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // BƯỚC 1 — Mỗi lớp kế thừa BaseEntity: chỉ truy vấn bản ghi chưa xóa mềm.
        ApplySoftDeleteFilters(builder);

        // BƯỚC 2 — Feedback: cây cha–con, không cascade xóa vòng.
        builder.Entity<Feedback>()
            .HasOne(f => f.Parent)
            .WithMany(p => p.Children)
            .HasForeignKey(f => f.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // BƯỚC 3 — Hóa đơn: mã duy nhất.
        builder.Entity<Invoice>()
            .HasIndex(i => i.InvoiceCode)
            .IsUnique();

        // BƯỚC 4 — Căn hộ: (Tầng + Số phòng) duy nhất.
        builder.Entity<Apartment>()
            .HasIndex(a => new { a.Floor, a.RoomNumber })
            .IsUnique();
    }

    // Gắn HasQueryFilter cho mọi kiểu assignable-to-BaseEntity (không gồm abstract/BaseEntity nếu không có DbSet).
    private static void ApplySoftDeleteFilters(ModelBuilder builder)
    {
        foreach (var et in builder.Model.GetEntityTypes())
        {
            var type = et.ClrType;
            if (type is null || type.IsAbstract || !typeof(BaseEntity).IsAssignableFrom(type))
                continue;
            var method = typeof(AppDbContext).GetMethod(nameof(SetSoftDeleteFilter), BindingFlags.NonPublic | BindingFlags.Static);
            method?.MakeGenericMethod(type).Invoke(null, new object[] { builder });
        }
    }

    private static void SetSoftDeleteFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : BaseEntity
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => !e.IsDeleted);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Tự gán UpdatedAt cho entity BaseEntity đang Modified.
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State != EntityState.Modified)
                continue;
            entry.Entity.UpdatedAt = DateTime.UtcNow;
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
