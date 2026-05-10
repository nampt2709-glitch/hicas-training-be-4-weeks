using System.Globalization; // Parse số epoch invariant (Redis string).
using Microsoft.Extensions.Caching.Distributed; // IDistributedCache Get/Set string.
using Microsoft.Extensions.Logging; // Log debug khi đọc/ghi epoch lỗi mềm.

namespace ApartmentAPI;

// Khóa tĩnh: một chuỗi Redis/memory cho mỗi loại danh sách — bump epoch làm lỗi thời mọi trang cache phân trang.
internal static class CacheEpochKeys
{ // Mở khối CacheEpochKeys — prefix ổn định giống CommentAPI (apt: list epoch).
    internal const string Apartments = "__epoch:list:apt:apartments"; // Epoch danh sách căn hộ.
    internal const string Residents = "__epoch:list:apt:residents"; // Epoch cư dân.
    internal const string Utilities = "__epoch:list:apt:utilities"; // Epoch tiện ích.
    internal const string Invoices = "__epoch:list:apt:invoices"; // Epoch hóa đơn.
    internal const string InvoiceItems = "__epoch:list:apt:invoiceitems"; // Epoch dòng hóa đơn.
    internal const string Feedbacks = "__epoch:list:apt:feedbacks"; // Epoch phản hồi.
    internal const string Attachments = "__epoch:list:apt:attachments"; // Epoch đính kèm.
    internal const string RefreshTokens = "__epoch:list:apt:refreshtokens"; // Epoch refresh token.
    internal const string Users = "__epoch:list:apt:users"; // Epoch user.
    internal const string Roles = "__epoch:list:apt:roles"; // Epoch role.
} // Kết thúc CacheEpochKeys.

// Hợp đồng bump/read epoch theo từng entity — service/repository gọi sau CUD để invalidate cache danh sách.
public interface ICacheListEpochStore
{ // Mở khối ICacheListEpochStore.
    // Đọc epoch hiện tại cho cache key phân trang Apartment — 0 nếu chưa có.
    Task<long> GetApartmentsListEpochAsync(CancellationToken cancellationToken = default);
    // Tăng epoch một bước — mọi khóa trang cũ (embed epoch cũ) trở thành miss logic.
    Task InvalidateApartmentsListsAsync(CancellationToken cancellationToken = default);
    Task<long> GetResidentsListEpochAsync(CancellationToken cancellationToken = default);
    Task InvalidateResidentsListsAsync(CancellationToken cancellationToken = default);
    Task<long> GetUtilitiesListEpochAsync(CancellationToken cancellationToken = default);
    Task InvalidateUtilitiesListsAsync(CancellationToken cancellationToken = default);
    Task<long> GetInvoicesListEpochAsync(CancellationToken cancellationToken = default);
    Task InvalidateInvoicesListsAsync(CancellationToken cancellationToken = default);
    Task<long> GetInvoiceItemsListEpochAsync(CancellationToken cancellationToken = default);
    Task InvalidateInvoiceItemsListsAsync(CancellationToken cancellationToken = default);
    Task<long> GetFeedbacksListEpochAsync(CancellationToken cancellationToken = default);
    Task InvalidateFeedbacksListsAsync(CancellationToken cancellationToken = default);
    Task<long> GetAttachmentsListEpochAsync(CancellationToken cancellationToken = default);
    Task InvalidateAttachmentsListsAsync(CancellationToken cancellationToken = default);
    Task<long> GetRefreshTokensListEpochAsync(CancellationToken cancellationToken = default);
    Task InvalidateRefreshTokensListsAsync(CancellationToken cancellationToken = default);
    Task<long> GetUsersListEpochAsync(CancellationToken cancellationToken = default);
    Task InvalidateUsersListsAsync(CancellationToken cancellationToken = default);
    Task<long> GetRolesListEpochAsync(CancellationToken cancellationToken = default);
    Task InvalidateRolesListsAsync(CancellationToken cancellationToken = default);
} // Kết thúc ICacheListEpochStore.

// Triển khai: lưu epoch dạng string long trong distributed cache; TTL ~ “vĩnh viễn” thực tế (~100 năm).
public sealed class CacheListEpochStore : ICacheListEpochStore
{ // Mở khối CacheListEpochStore.
    private static readonly DistributedCacheEntryOptions EpochRetention = new()
    { // Tuỳ chọn giữ khóa epoch — tránh bị xóa sớm làm epoch reset về 0 ngoài ý muốn.
        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(36524), // ~ 100 năm.
    };

    private readonly IDistributedCache _cache; // Backend Redis hoặc memory.
    private readonly ILogger<CacheListEpochStore> _log; // Ghi debug khi cache lỗi mềm.

    public CacheListEpochStore(IDistributedCache cache, ILogger<CacheListEpochStore> log)
    { // Mở khối constructor CacheListEpochStore.
        // BƯỚC 1 — Tiêm IDistributedCache + logger cho ReadEpoch/BumpEpoch.
        _cache = cache; // Cache phân tán đã đăng ký trong DI.
        _log = log; // Logger category CacheListEpochStore.
    } // Kết thúc constructor CacheListEpochStore.

    public Task<long> GetApartmentsListEpochAsync(CancellationToken cancellationToken = default) =>
        ReadEpochAsync(CacheEpochKeys.Apartments, cancellationToken);

    public Task InvalidateApartmentsListsAsync(CancellationToken cancellationToken = default) =>
        BumpEpochAsync(CacheEpochKeys.Apartments, cancellationToken);

    public Task<long> GetResidentsListEpochAsync(CancellationToken cancellationToken = default) =>
        ReadEpochAsync(CacheEpochKeys.Residents, cancellationToken);

    public Task InvalidateResidentsListsAsync(CancellationToken cancellationToken = default) =>
        BumpEpochAsync(CacheEpochKeys.Residents, cancellationToken);

    public Task<long> GetUtilitiesListEpochAsync(CancellationToken cancellationToken = default) =>
        ReadEpochAsync(CacheEpochKeys.Utilities, cancellationToken);

    public Task InvalidateUtilitiesListsAsync(CancellationToken cancellationToken = default) =>
        BumpEpochAsync(CacheEpochKeys.Utilities, cancellationToken);

    public Task<long> GetInvoicesListEpochAsync(CancellationToken cancellationToken = default) =>
        ReadEpochAsync(CacheEpochKeys.Invoices, cancellationToken);

    public Task InvalidateInvoicesListsAsync(CancellationToken cancellationToken = default) =>
        BumpEpochAsync(CacheEpochKeys.Invoices, cancellationToken);

    public Task<long> GetInvoiceItemsListEpochAsync(CancellationToken cancellationToken = default) =>
        ReadEpochAsync(CacheEpochKeys.InvoiceItems, cancellationToken);

    public Task InvalidateInvoiceItemsListsAsync(CancellationToken cancellationToken = default) =>
        BumpEpochAsync(CacheEpochKeys.InvoiceItems, cancellationToken);

    public Task<long> GetFeedbacksListEpochAsync(CancellationToken cancellationToken = default) =>
        ReadEpochAsync(CacheEpochKeys.Feedbacks, cancellationToken);

    public Task InvalidateFeedbacksListsAsync(CancellationToken cancellationToken = default) =>
        BumpEpochAsync(CacheEpochKeys.Feedbacks, cancellationToken);

    public Task<long> GetAttachmentsListEpochAsync(CancellationToken cancellationToken = default) =>
        ReadEpochAsync(CacheEpochKeys.Attachments, cancellationToken);

    public Task InvalidateAttachmentsListsAsync(CancellationToken cancellationToken = default) =>
        BumpEpochAsync(CacheEpochKeys.Attachments, cancellationToken);

    public Task<long> GetRefreshTokensListEpochAsync(CancellationToken cancellationToken = default) =>
        ReadEpochAsync(CacheEpochKeys.RefreshTokens, cancellationToken);

    public Task InvalidateRefreshTokensListsAsync(CancellationToken cancellationToken = default) =>
        BumpEpochAsync(CacheEpochKeys.RefreshTokens, cancellationToken);

    public Task<long> GetUsersListEpochAsync(CancellationToken cancellationToken = default) =>
        ReadEpochAsync(CacheEpochKeys.Users, cancellationToken);

    public Task InvalidateUsersListsAsync(CancellationToken cancellationToken = default) =>
        BumpEpochAsync(CacheEpochKeys.Users, cancellationToken);

    public Task<long> GetRolesListEpochAsync(CancellationToken cancellationToken = default) =>
        ReadEpochAsync(CacheEpochKeys.Roles, cancellationToken);

    public Task InvalidateRolesListsAsync(CancellationToken cancellationToken = default) =>
        BumpEpochAsync(CacheEpochKeys.Roles, cancellationToken);

    // Đọc chuỗi long từ cache; miss/parse fail/lỗi mềm → 0 (coi như epoch đầu tiên).
    private async Task<long> ReadEpochAsync(string key, CancellationToken cancellationToken)
    { // Mở khối ReadEpochAsync.
        try
        { // BƯỚC 1 — GetStringAsync theo khóa epoch.
            var raw = await _cache.GetStringAsync(key, cancellationToken); // null nếu chưa bump lần nào.
            // TRƯỜNG HỢP A — Khóa trống → epoch 0.
            if (string.IsNullOrEmpty(raw))
                return 0;
            // BƯỚC 2 — Parse invariant; fail → 0.
            return long.TryParse(raw, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }
        catch (OperationCanceledException)
        { // Hủy request — ném lại cho ASP.NET Core.
            throw;
        }
        catch (Exception ex)
        { // BƯỚC 3 — Lỗi Redis/network: không fail request; log debug và coi miss.
            _log.LogDebug(ex, "ReadEpochAsync miss for {Key}; using 0.", key);
            return 0;
        }
    } // Kết thúc ReadEpochAsync.

    // Đọc epoch hiện có (nếu có), ++checked, SetString với TTL dài; overflow → reset 1 qua TryWriteEpochAsync.
    private async Task BumpEpochAsync(string key, CancellationToken cancellationToken)
    { // Mở khối BumpEpochAsync.
        long next = 0;
        try
        { // BƯỚC 1 — Đọc giá trị cũ (nếu có).
            var raw = await _cache.GetStringAsync(key, cancellationToken);
            if (!string.IsNullOrEmpty(raw) && long.TryParse(raw, CultureInfo.InvariantCulture, out var v))
                next = v; // Tiếp tục từ epoch trước.
            // BƯỚC 2 — Tăng có kiểm tra overflow.
            checked
            {
                next++;
            }

            // BƯỚC 3 — Ghi lại string invariant + retention dài.
            await _cache.SetStringAsync(
                key,
                next.ToString(CultureInfo.InvariantCulture),
                EpochRetention,
                cancellationToken);
        }
        catch (OperationCanceledException)
        { // Hủy — bubble up.
            throw;
        }
        catch (OverflowException)
        { // TRƯỜNG HỢP B — long.Max → reset an toàn về 1.
            await TryWriteEpochAsync(key, 1L, cancellationToken);
        }
        catch (Exception ex)
        { // BƯỚC 4 — Lỗi ghi cache: không làm fail CUD nghiệp vụ; chỉ log.
            _log.LogDebug(ex, "BumpEpochAsync failed for {Key}; skipping.", key);
        }
    } // Kết thúc BumpEpochAsync.

    // Ghi epoch cố định (dùng khi overflow) — lỗi chỉ log debug.
    private async Task TryWriteEpochAsync(string key, long value, CancellationToken cancellationToken)
    { // Mở khối TryWriteEpochAsync.
        try
        { // BƯỚC 1 — SetString với cùng retention như BumpEpoch.
            await _cache.SetStringAsync(
                key,
                value.ToString(CultureInfo.InvariantCulture),
                EpochRetention,
                cancellationToken);
        }
        catch (Exception ex)
        { // BƯỚC 2 — Bỏ qua lỗi ghi — invalidate mềm thất bại không chặn luồng chính.
            _log.LogDebug(ex, "TryWriteEpochAsync failed for {Key}.", key);
        }
    } // Kết thúc TryWriteEpochAsync.
} // Kết thúc CacheListEpochStore.
