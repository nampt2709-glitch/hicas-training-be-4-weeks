using ApartmentAPI.Data;
using ApartmentAPI.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ApartmentAPI.RecordGenerator;

/// <summary>
/// Sinh và xóa lô dữ liệu (~100k bản ghi) cho domain căn hộ; cleanup tôn trọng FK và chỉ sờ các hàng có marker.
/// </summary>
internal sealed class BulkRecordGenerator
{
    private readonly AppDbContext _db;
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<Role> _roleManager;

    public BulkRecordGenerator(
        AppDbContext db,
        UserManager<User> userManager,
        RoleManager<Role> roleManager)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    /// <summary>
    /// Kiểm tra user bulk (hậu tố email chuẩn hóa) — nếu có thì không chèn trùng.
    /// </summary>
    private Task<bool> AnyBulkUsersExistAsync(CancellationToken ct)
    {
        var pattern = "%" + BulkGenerationConstants.NormalizedEmailSuffix;
        return _db.Users.AsNoTracking().AnyAsync(
            u => u.NormalizedEmail != null && EF.Functions.Like(u.NormalizedEmail, pattern),
            ct);
    }

    /// <summary>
    /// Chèn marker, user Identity, rồi các bảng nghiệp vụ theo batch; bỏ qua nếu lô đã tồn tại.
    /// </summary>
    public async Task InsertIfNeededAsync(CancellationToken ct)
    {
        if (await AnyBulkUsersExistAsync(ct))
        {
            Console.WriteLine(
                "Đã phát hiện dữ liệu bulk (@bulkgen.recordgenerator.local). Bỏ qua insert. Chạy cleanup trước nếu muốn tạo lại.");
            return;
        }

        var rnd = new Random(918273654);
        var roleUser = await _roleManager.FindByNameAsync("User")
                       ?? throw new InvalidOperationException("Vai trò User chưa tồn tại. Chạy migration/seed trước.");
        var roleAdmin = await _roleManager.FindByNameAsync("Admin")
                        ?? throw new InvalidOperationException("Vai trò Admin chưa tồn tại. Chạy migration/seed trước.");

        // BƯỚC A — User marker (có email bulk) để nhất quán cleanup Identity; không dùng làm tác giả chính.
        var marker = new User
        {
            UserName = BulkGenerationConstants.MarkerUserName,
            Email = "bulkgen.marker@bulkgen.recordgenerator.local",
            EmailConfirmed = true,
            FullName = "Hệ thống đánh dấu bulk",
            CreatedAt = DateTime.UtcNow,
        };

        var markerResult = await _userManager.CreateAsync(marker, BulkGenerationConstants.BulkUserPassword);
        if (!markerResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Không tạo được marker: " + string.Join("; ", markerResult.Errors.Select(e => e.Description)));
        }

        await _userManager.AddToRoleAsync(marker, roleUser.Name!);

        var authorIds = new List<Guid>(BulkGenerationConstants.UserCount);

        // BƯỚC B — Tạo tài khoản nội dung (bulkgen_a_ / bulkgen_u_), gán role; Admin cho N tài khoản đầu.
        for (var i = 1; i < BulkGenerationConstants.UserCount; i++)
        {
            string userName;
            string email;
            if (i <= BulkGenerationConstants.BulkAdminCount)
            {
                userName = $"bulkgen_a_{i:D5}";
                email = $"bulkgen_a_{i:D5}@bulkgen.recordgenerator.local";
            }
            else
            {
                var plainUserIndex = i - BulkGenerationConstants.BulkAdminCount;
                userName = $"bulkgen_u_{plainUserIndex:D5}";
                email = $"bulkgen_u_{plainUserIndex:D5}@bulkgen.recordgenerator.local";
            }

            var u = new User
            {
                UserName = userName,
                Email = email,
                EmailConfirmed = true,
                FullName = BuildPersonName(rnd, i),
                CreatedAt = DateTime.UtcNow.AddDays(-rnd.Next(0, 520)),
            };

            var r = await _userManager.CreateAsync(u, BulkGenerationConstants.BulkUserPassword);
            if (!r.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Không tạo user {u.UserName}: " + string.Join("; ", r.Errors.Select(e => e.Description)));
            }

            await _userManager.AddToRoleAsync(u, roleUser.Name!);
            if (i <= BulkGenerationConstants.BulkAdminCount)
            {
                await _userManager.AddToRoleAsync(u, roleAdmin.Name!);
            }

            authorIds.Add(u.Id);
        }

        var markerUserName = BulkGenerationConstants.BulkCreatedByMarker;

        Console.WriteLine(
            $"Đã tạo {BulkGenerationConstants.UserCount} user Identity (gồm marker), trong đó {Math.Min(BulkGenerationConstants.BulkAdminCount, authorIds.Count)} tài khoản có vai trò Admin. Đang tạo dịch vụ tiện ích…");

        // BƯỚC C — UtilityService (Batch EF) — nguồn cho InvoiceItem.
        var utilityIds = new List<Guid>(BulkGenerationConstants.UtilityServiceCount);
        var utilities = new List<UtilityService>(BulkGenerationConstants.UtilityServiceCount);
        for (var s = 0; s < BulkGenerationConstants.UtilityServiceCount; s++)
        {
            var id = Guid.NewGuid();
            utilityIds.Add(id);
            var unit = UtilityUnits[rnd.Next(UtilityUnits.Length)];
            var price = Math.Round((decimal)(rnd.NextDouble() * 500 + 10), 2);
            utilities.Add(new UtilityService
            {
                Id = id,
                Name = $"[BULK] Dịch vụ #{s + 1} ({unit})",
                Description = $"Sinh tự động — mã nội bộ {s + 1:N0}",
                Price = price,
                Unit = unit,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-rnd.Next(0, 400)),
                CreatedBy = markerUserName,
            });
        }

        foreach (var chunk in utilities.Chunk(400))
        {
            _db.UtilityServices.AddRange(chunk);
            await _db.SaveChangesAsync(ct);
        }

        Console.WriteLine($"Đã tạo {BulkGenerationConstants.UtilityServiceCount} dịch vụ. Đang tạo căn hộ…");

        // BƯỚC D — Apartment — chỉ số (Floor, RoomNumber) duy nhất.
        var apartmentIds = new List<Guid>(BulkGenerationConstants.ApartmentCount);
        var apartments = new List<Apartment>(BulkGenerationConstants.ApartmentCount);
        for (var a = 0; a < BulkGenerationConstants.ApartmentCount; a++)
        {
            var id = Guid.NewGuid();
            apartmentIds.Add(id);
            var floor = 1 + a % 40;
            var roomNumber = $"{a / 40 + 1:D4}";
            apartments.Add(new Apartment
            {
                Id = id,
                Floor = floor,
                RoomNumber = roomNumber,
                Area = Math.Round((decimal)(rnd.NextDouble() * 120 + 35), 2),
                Status = (ApartmentStatus)(a % 3),
                MaxResidents = 2 + rnd.Next(0, 5),
                Note = a % 7 == 0 ? "Ghi chú bulk — kiểm tra phân trang." : null,
                CreatedAt = DateTime.UtcNow.AddDays(-rnd.Next(0, 600)),
                CreatedBy = markerUserName,
            });
        }

        foreach (var chunk in apartments.Chunk(500))
        {
            _db.Apartments.AddRange(chunk);
            await _db.SaveChangesAsync(ct);
        }

        Console.WriteLine($"Đã tạo {BulkGenerationConstants.ApartmentCount} căn hộ. Đang tạo cư dân…");

        // BƯỚC E — Resident — liên kết Apartment + tuỳ chọn User bulk.
        var residents = new List<Resident>(BulkGenerationConstants.ResidentCount);
        for (var r = 0; r < BulkGenerationConstants.ResidentCount; r++)
        {
            var aptId = apartmentIds[r % apartmentIds.Count];
            Guid? userId = r % 4 != 0 ? authorIds[r % authorIds.Count] : null;
            residents.Add(new Resident
            {
                Id = Guid.NewGuid(),
                FullName = BuildPersonName(rnd, 10_000 + r),
                IdentityNumber = $"BG-RES-{r + 1:D8}",
                PhoneNumber = $"09{rnd.Next(1_000_000, 9_999_999):D7}",
                Email = $"res_{r + 1:D6}@bulkgen.recordgenerator.local",
                BirthDate = DateTime.UtcNow.AddYears(-20 - rnd.Next(0, 50)).Date,
                IsPrimaryResident = r % 3 == 0,
                IsActive = r % 20 != 0,
                ApartmentId = aptId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow.AddDays(-rnd.Next(0, 300)),
                CreatedBy = markerUserName,
            });
        }

        foreach (var chunk in residents.Chunk(600))
        {
            _db.Residents.AddRange(chunk);
            await _db.SaveChangesAsync(ct);
        }

        Console.WriteLine($"Đã tạo {BulkGenerationConstants.ResidentCount} cư dân. Đang tạo hóa đơn + chi tiết…");

        // BƯỚC F — Invoice kèm InvoiceItem trong từng batch SaveChanges (tránh N+1 cập nhật TotalAmount).
        var priceByServiceId = utilities.ToDictionary(x => x.Id, x => x.Price);
        var itemSeq = 0;
        const int invoiceBatchSize = 250;
        for (var batchStart = 0; batchStart < BulkGenerationConstants.InvoiceCount; batchStart += invoiceBatchSize)
        {
            var batchEnd = Math.Min(batchStart + invoiceBatchSize, BulkGenerationConstants.InvoiceCount);
            var invoiceBatch = new List<Invoice>(batchEnd - batchStart);
            for (var inv = batchStart; inv < batchEnd; inv++)
            {
                var id = Guid.NewGuid();
                var aptId = apartmentIds[inv % apartmentIds.Count];
                var month = 1 + inv % 12;
                var year = 2023 + inv % 4;
                var status = inv % 5 == 0 ? InvoiceStatus.Paid : InvoiceStatus.Unpaid;
                var invoice = new Invoice
                {
                    Id = id,
                    InvoiceCode = $"BULK-INV-{inv + 1:D10}",
                    Month = month,
                    Year = year,
                    IssueDate = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc),
                    DueDate = new DateTime(year, month, 15, 0, 0, 0, DateTimeKind.Utc),
                    PaidAt = status == InvoiceStatus.Paid
                        ? new DateTime(year, month, 10, 0, 0, 0, DateTimeKind.Utc)
                        : null,
                    TotalAmount = 0,
                    PaidAmount = 0,
                    Status = status,
                    Note = inv % 11 == 0 ? "Hóa đơn bulk." : null,
                    ApartmentId = aptId,
                    CreatedAt = DateTime.UtcNow.AddDays(-rnd.Next(0, 200)),
                    CreatedBy = markerUserName,
                };

                var lineCount = inv < 600 ? 4 : 3;
                decimal invoiceTotal = 0;
                for (var line = 0; line < lineCount; line++)
                {
                    var svcId = utilityIds[(itemSeq + line) % utilityIds.Count];
                    var qty = (decimal)Math.Round(rnd.NextDouble() * 5 + 1, 2);
                    var unitPrice = priceByServiceId[svcId];
                    var sub = Math.Round(qty * unitPrice, 2);
                    invoiceTotal += sub;
                    invoice.Items.Add(new InvoiceItem
                    {
                        Id = Guid.NewGuid(),
                        ServiceId = svcId,
                        Quantity = qty,
                        UnitPrice = unitPrice,
                        SubTotal = sub,
                        Description = line % 2 == 0 ? "Dòng bulk — tiêu thụ kỳ." : null,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = markerUserName,
                    });
                }

                itemSeq += lineCount;
                invoice.TotalAmount = Math.Round(invoiceTotal, 2);
                invoice.PaidAmount = invoice.Status == InvoiceStatus.Paid ? invoice.TotalAmount : 0;
                invoiceBatch.Add(invoice);
            }

            _db.Invoices.AddRange(invoiceBatch);
            await _db.SaveChangesAsync(ct);
        }

        if (itemSeq != BulkGenerationConstants.InvoiceItemTotal)
        {
            throw new InvalidOperationException(
                $"Số InvoiceItem không khớp cấu hình: {itemSeq} != {BulkGenerationConstants.InvoiceItemTotal}.");
        }

        Console.WriteLine(
            $"Đã tạo {BulkGenerationConstants.InvoiceCount} hóa đơn + {BulkGenerationConstants.InvoiceItemTotal} dòng. Đang tạo phản hồi (cây)…");

        // BƯỚC G — Feedback: chèn gốc trước; chèn trả lời (Restrict trên ParentId); một phần trả lời lồng vào feedback đã có để tạo cây sâu > 1 tầng (RecordGenerator trước đây hầu hết chỉ có con trực tiếp của gốc).
        var rootCount = BulkGenerationConstants.FeedbackCount * 40 / 100;
        var childCount = BulkGenerationConstants.FeedbackCount - rootCount;
        var rootIds = new List<Guid>(rootCount);
        var feedbackBuffer = new List<Feedback>(2500);
        // Hồi Id mọi nút đã chèn — dùng chọn ParentId ngẫu nhiên để có nhánh con-of-reply (không chỉ root → con).
        var poolForParentPick = new List<Guid>();

        for (var f = 0; f < rootCount; f++)
        {
            var id = Guid.NewGuid();
            rootIds.Add(id);
            poolForParentPick.Add(id);
            feedbackBuffer.Add(new Feedback
            {
                Id = id,
                Content = BuildFeedbackBody(rnd, f, isReply: false),
                IsResolved = f % 9 == 0,
                IsPinned = f % 80 == 0,
                UserId = authorIds[f % authorIds.Count],
                ParentId = null,
                CreatedAt = DateTime.UtcNow.AddMinutes(-rnd.Next(0, 500_000)),
                CreatedBy = markerUserName,
            });

            if (feedbackBuffer.Count >= 2000)
            {
                _db.Feedbacks.AddRange(feedbackBuffer);
                await _db.SaveChangesAsync(ct);
                feedbackBuffer.Clear();
            }
        }

        if (feedbackBuffer.Count > 0)
        {
            _db.Feedbacks.AddRange(feedbackBuffer);
            await _db.SaveChangesAsync(ct);
            feedbackBuffer.Clear();
        }

        for (var c = 0; c < childCount; c++)
        {
            // ~28% trả lời gắn vào bất kỳ nút đã có (gốc hoặc reply) → nhiều cây có depth ≥ 2; phần còn lại vẫn trả lời trực tiếp gốc luân phiên.
            var useDeepParent = poolForParentPick.Count > 0 && rnd.NextDouble() < 0.28;
            var parentId = useDeepParent
                ? poolForParentPick[rnd.Next(poolForParentPick.Count)]
                : rootIds[c % rootIds.Count];
            var newId = Guid.NewGuid();
            poolForParentPick.Add(newId);
            feedbackBuffer.Add(new Feedback
            {
                Id = newId,
                Content = BuildFeedbackBody(rnd, c, isReply: true),
                IsResolved = c % 11 == 0,
                IsPinned = false,
                UserId = authorIds[rnd.Next(authorIds.Count)],
                ParentId = parentId,
                CreatedAt = DateTime.UtcNow.AddMinutes(-rnd.Next(0, 400_000)),
                CreatedBy = markerUserName,
            });

            if (feedbackBuffer.Count >= 2500)
            {
                _db.Feedbacks.AddRange(feedbackBuffer);
                await _db.SaveChangesAsync(ct);
                feedbackBuffer.Clear();
            }
        }

        if (feedbackBuffer.Count > 0)
        {
            _db.Feedbacks.AddRange(feedbackBuffer);
            await _db.SaveChangesAsync(ct);
        }

        Console.WriteLine($"Đã tạo {BulkGenerationConstants.FeedbackCount} phản hồi. Đang tạo file đính kèm + refresh token…");

        // BƯỚC H — Attachment (một phần feedback) + RefreshToken.
        var feedbackIdsForAttach = await _db.Feedbacks.AsNoTracking()
            .Where(f => f.CreatedBy == markerUserName)
            .OrderBy(f => f.CreatedAt)
            .Take(BulkGenerationConstants.AttachmentCount)
            .Select(f => f.Id)
            .ToListAsync(ct);

        var attachBuffer = new List<Attachment>();
        for (var z = 0; z < feedbackIdsForAttach.Count; z++)
        {
            var fid = feedbackIdsForAttach[z];
            var uid = authorIds[z % authorIds.Count];
            attachBuffer.Add(new Attachment
            {
                Id = Guid.NewGuid(),
                Scope = AttachmentScope.Feedback,
                OriginalFileName = $"bulk_att_{z + 1:D5}.png",
                StoredFileName = $"{Guid.NewGuid():N}.png",
                FilePath = $"/uploads/bulk/{z + 1:D5}.png",
                ContentType = "image/png",
                FileSize = rnd.Next(1024, 900_000),
                UserId = uid,
                FeedbackId = fid,
                CreatedAt = DateTime.UtcNow.AddHours(-rnd.Next(0, 2000)),
                CreatedBy = markerUserName,
            });
            if (attachBuffer.Count >= 500)
            {
                _db.Attachments.AddRange(attachBuffer);
                await _db.SaveChangesAsync(ct);
                attachBuffer.Clear();
            }
        }

        if (attachBuffer.Count > 0)
        {
            _db.Attachments.AddRange(attachBuffer);
            await _db.SaveChangesAsync(ct);
        }

        var tokenBuffer = new List<RefreshToken>();
        for (var t = 0; t < BulkGenerationConstants.RefreshTokenCount; t++)
        {
            tokenBuffer.Add(new RefreshToken
            {
                Id = Guid.NewGuid(),
                TokenHash = $"bulk-rt-{t + 1:D6}-{Guid.NewGuid():N}",
                ExpiresAt = DateTime.UtcNow.AddDays(rnd.Next(1, 60)),
                IsRevoked = t % 50 == 0,
                RevokedAt = t % 50 == 0 ? DateTime.UtcNow : null,
                DeviceInfo = "RecordGenerator",
                IpAddress = $"10.0.{t % 200}.{t % 255}",
                UserId = authorIds[t % authorIds.Count],
                CreatedAt = DateTime.UtcNow,
                CreatedBy = markerUserName,
            });
            if (tokenBuffer.Count >= 500)
            {
                _db.RefreshTokens.AddRange(tokenBuffer);
                await _db.SaveChangesAsync(ct);
                tokenBuffer.Clear();
            }
        }

        if (tokenBuffer.Count > 0)
        {
            _db.RefreshTokens.AddRange(tokenBuffer);
            await _db.SaveChangesAsync(ct);
        }

        Console.WriteLine(
            $"Hoàn tất lô bulk ~{BulkGenerationConstants.TotalBusinessRowsApprox:N0} bản ghi nghiệp vụ (kèm Identity).");
    }

    /// <summary>
    /// Xóa theo thứ tự FK: chi tiết hóa đơn → hóa đơn → cư dân → đính kèm → feedback → refresh → căn hộ → dịch vụ → user bulk.
    /// </summary>
    public async Task CleanupBulkAsync(CancellationToken ct)
    {
        var m = BulkGenerationConstants.BulkCreatedByMarker;

        // BƯỚC 1 — Gỡ cây feedback (Restrict) trước khi xóa hàng loạt.
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE Feedbacks
            SET ParentId = NULL
            WHERE CreatedBy = {m}
            """,
            cancellationToken: ct);

        var delItems = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM InvoiceItems
            WHERE InvoiceId IN (SELECT Id FROM Invoices WHERE CreatedBy = {m})
            """,
            cancellationToken: ct);

        var delInv = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM Invoices WHERE CreatedBy = {m}
            """,
            cancellationToken: ct);

        var delRes = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM Residents WHERE CreatedBy = {m}
            """,
            cancellationToken: ct);

        var delAtt = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM Attachments WHERE CreatedBy = {m}
            """,
            cancellationToken: ct);

        var delFb = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM Feedbacks WHERE CreatedBy = {m}
            """,
            cancellationToken: ct);

        var delRt = await _db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM RefreshTokens
            WHERE UserId IN (
                SELECT Id FROM AspNetUsers
                WHERE NormalizedEmail IS NOT NULL
                  AND NormalizedEmail LIKE '%@BULKGEN.RECORDGENERATOR.LOCAL')
            """,
            cancellationToken: ct);

        var delApt = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM Apartments WHERE CreatedBy = {m}
            """,
            cancellationToken: ct);

        var delUtil = await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            DELETE FROM UtilityServices WHERE CreatedBy = {m}
            """,
            cancellationToken: ct);

        var delUsers = await _db.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM AspNetUsers
            WHERE NormalizedEmail IS NOT NULL
              AND NormalizedEmail LIKE '%@BULKGEN.RECORDGENERATOR.LOCAL'
            """,
            cancellationToken: ct);

        Console.WriteLine(
            $"Cleanup: InvoiceItems={delItems}, Invoices={delInv}, Residents={delRes}, Attachments={delAtt}, Feedbacks={delFb}, RefreshTokens={delRt}, Apartments={delApt}, UtilityServices={delUtil}, AspNetUsers={delUsers}.");
    }

    private static string BuildFeedbackBody(Random rnd, int index, bool isReply)
    {
        var phrase = FeedbackBodies[rnd.Next(FeedbackBodies.Length)];
        return isReply
            ? $"{phrase} (trả lời #{index + 1})"
            : $"{phrase} — phiếu gốc #{index + 1} — rnd {rnd.Next(1000, 9999)}";
    }

    private static string BuildPersonName(Random rnd, int seed)
    {
        var first = FirstNames[rnd.Next(FirstNames.Length)];
        var last = LastNames[rnd.Next(LastNames.Length)];
        return $"{first} {last} #{seed:D4}";
    }

    private static readonly string[] UtilityUnits =
    [
        "kWh", "m3", "tháng", "lượt", "GB",
    ];

    private static readonly string[] FirstNames =
    [
        "Anh", "Bình", "Chi", "Dũng", "Giang", "Hà", "Hải", "Hùng", "Khánh", "Lan",
        "Linh", "Mai", "Minh", "Nam", "Ngọc", "Oanh", "Phúc", "Quang", "Quỳnh", "Tâm",
        "Thảo", "Trang", "Trung", "Tuấn", "Vân", "Việt", "Vy", "Yến",
    ];

    private static readonly string[] LastNames =
    [
        "Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Huỳnh", "Phan", "Vũ", "Võ", "Đặng",
        "Bùi", "Đỗ", "Hồ", "Ngô", "Dương", "Lý",
    ];

    private static readonly string[] FeedbackBodies =
    [
        "Phản ánh điều hoà kêu lớn vào ban đêm.",
        "Đề nghị kiểm tra cầu thang thoát hiểm tầng hầm.",
        "Thanh toán QR qua app có lỗi hiển thị hai lần.",
        "Hành lang bị vướng xe — mong ban quản trị nhắc nhở.",
        "Cảm ơn đội bảo vệ hỗ trợ gửi kiện hộ.",
    ];
}
