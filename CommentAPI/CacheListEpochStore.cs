using System.Globalization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace CommentAPI;

// =============================================================================
// File CacheListEpochStore.cs: lưu và tăng bộ đếm “epoch” (thế hệ) trong cache phân tán.
//
// — Thuật ngữ “epoch” dùng ở đây có nghĩa gì?
// Trong TIẾNG ANH kỹ thuật, “epoch” = một mốc / một giai đoạn (thường đánh số 0,1,2…) đánh dấu
// “đời” của Snapshot dữ liệu. Ta không có nghĩa “Unix Epoch” (01/01/1970 UTC) — chỉ mượn cùng một từ
// để nói: “Đây là thế hệ thứ K của không gian cache danh sách; mỗi lần dữ liệu nghiệp vụ làm sai lệch
// danh sách thì ta lên K+1 để không còn tái sử dụng JSON list cũ.”
//
// — Tại sao gọi là epoch mà không gọi “version” hay “generation”?
// “Version” cũng đúng; “epoch” nhấn mạnh có thời tuyến: thế trước / thế sau, snapshot cũ vô hiệu hóa một cách logic.
// Generation (thế hệ) gần nghĩa tương tự; epoch là từ quen trong hệ thống cache / storage (vd. fencing token).
//
// — Cơ chế: mỗi khóa cache danh sách (prefix cmt:/pst:/usr:) nhúng số epoch hiện tại. Khi Bump (Invalidate*),
// ta chỉ tăng MỘT số trong Redis/memory; không cần xóa từng key trang/sort (không gian khóa vô hạn).
// Request đọc list sau bump dùng epoch mới ⇒ khóa JSON khác trước ⇒ miss ⇒ DB đọp lại ⇒ ghi snapshot mới.
// Các khóa JSON cũ (epoch thấp hơn) có thể còn cho đến khi TTL hết — không được đọc nữa vì không còn trùng tên khóa.
// =============================================================================

// -----------------------------------------------------------------------------
// CacheEpochKeys: chuỗi khóa hệ thống rất ngắn trong IDistributedCache.
// Redis sẽ thêm InstanceName (prefix) ở tầng dưới; giá trị lưu tại các key sau là một chữ số long dạng "0","1","2",… .
// -----------------------------------------------------------------------------
internal static class CacheEpochKeys
{
    // Bộ nhớ đếm thế hệ danh sách comment (ánh xạ tới tiền tố khóa phản hồi EntityCacheKeys: cmt:{epoch}:…).
    internal const string CommentsLists = "__epoch:list:comments";

    // Bộ nhớ đếm thế hệ GET /api/posts phân trang không filter (tiền tố khóa: pst:{epoch}:…).
    internal const string PostsLists = "__epoch:list:posts";

    // Bộ nhớ đếm thế hệ GET /api/users phân trang không filter (tiền tố khóa: usr:{epoch}:…).
    internal const string UsersLists = "__epoch:list:users";
}

// -----------------------------------------------------------------------------
// ICacheListEpochStore: hợp đồng đọc epoch hiện tại và “đẩy thế kỷ sang trang” (Invalidate = Bump +1).
// Không chứa nghiệp vụ REST — chỉ số và chuỗi trong distributed cache.
// -----------------------------------------------------------------------------
public interface ICacheListEpochStore
{
    // Đọc số epoch comment-list hiện tại để nhúng vào EntityCacheKeys (cmt:{n}:…) khi đọc/ghi cache JSON list.
    Task<long> GetCommentsListEpochAsync(CancellationToken cancellationToken = default);

    // Bump epoch comment-list: CRUD Comment (hoặc cascade thay đổi aggregate comment) ⇒ thế tiếp theo ⇒ mọi list cache cmt cũ bị orphan.
    Task InvalidateCommentsListsAsync(CancellationToken cancellationToken = default);

    // Đọc epoch danh sách Post (pst).
    Task<long> GetPostsListEpochAsync(CancellationToken cancellationToken = default);

    // Bump epoch danh sách Post: tạo/sửa/xóa post ảnh hưởng /api/posts paged không filter (và có thể kết hợp bump comment nếu xóa post).
    Task InvalidatePostsListAsync(CancellationToken cancellationToken = default);

    // Đọc epoch danh sách User (usr).
    Task<long> GetUsersListEpochAsync(CancellationToken cancellationToken = default);

    // Bump epoch danh sách User: user mới hoặc sửa thông tin hiển thị trong list hoặc xóa user.
    Task InvalidateUsersListAsync(CancellationToken cancellationToken = default);
}

// -----------------------------------------------------------------------------
// CacheListEpochStore: triển khai ICacheListEpochStore trên IDistributedCache (Redis ưu tiên hoặc memory fallback).
// TRƯỜNG HỢP Redis tạm hỏng — ReadEpoch trả 0 và log Debug; Bump nuốt lỗi (API CRUD không fail vì cache).
// TRƯỜNG HỢP Hai request Bump đồng thời — có thể mất một bước +1 trong race hiếm; epoch vẫn không giảm, an toàn hướng “luôn mới”.
// -----------------------------------------------------------------------------
public sealed class CacheListEpochStore : ICacheListEpochStore
{
    // Cấu hình TTL của key epoch: Relative ~100 năm — không muốn khóa hệ thống biến mất sớm (nếu mất, ReadEpoch trả 0 ⇒ có nguy cơ trùng tên khóa cũ với payload cũ trong cửa sổ TTL ngắn).
    private static readonly DistributedCacheEntryOptions EpochRetention = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(36524),
    };

    private readonly IDistributedCache _cache; // Backend thực tế đã được Program đăng ký (composite Redis-first).
    private readonly ILogger<CacheListEpochStore> _log; // Logger cấp Debug khi degrade.

    public CacheListEpochStore(IDistributedCache cache, ILogger<CacheListEpochStore> log)
    { // BƯỚC 1 — Gán hai dependency cho trường readonly.
        _cache = cache; // Lưu tham chiếu cache phân tán dùng chung với EntityResponseCache.
        _log = log; // Lưu logger category CacheListEpochStore.
    }

    // BƯỚC public — Delegate ReadEpoch vào khóa comment list.
    public Task<long> GetCommentsListEpochAsync(CancellationToken cancellationToken = default) =>
        ReadEpochAsync(CacheEpochKeys.CommentsLists, cancellationToken);

    // BƯỚC public — Delegate Bump vào khóa comment list.
    public Task InvalidateCommentsListsAsync(CancellationToken cancellationToken = default) =>
        BumpEpochAsync(CacheEpochKeys.CommentsLists, cancellationToken);

    public Task<long> GetPostsListEpochAsync(CancellationToken cancellationToken = default) =>
        ReadEpochAsync(CacheEpochKeys.PostsLists, cancellationToken);

    public Task InvalidatePostsListAsync(CancellationToken cancellationToken = default) =>
        BumpEpochAsync(CacheEpochKeys.PostsLists, cancellationToken);

    public Task<long> GetUsersListEpochAsync(CancellationToken cancellationToken = default) =>
        ReadEpochAsync(CacheEpochKeys.UsersLists, cancellationToken);

    public Task InvalidateUsersListAsync(CancellationToken cancellationToken = default) =>
        BumpEpochAsync(CacheEpochKeys.UsersLists, cancellationToken);

    // -------------------------------------------------------------------------
    // ReadEpochAsync: đọc chuỗi số tại key; không có hoặc lỗi parse ⇒ 0 (thế hệ đầu = an toàn với không gian key mặc định).
    // -------------------------------------------------------------------------
    private async Task<long> ReadEpochAsync(string key, CancellationToken cancellationToken)
    { // BƯỚC 1 — Bọc try/catch để không làm đổ request GET khi Redis timeout.
        try // Nhóm lỗi không phải hủy.
        {
            // BƯỚC 1a — Lấy chuỗi UTF-8 từ IDistributedCache (StackExchange.Memory hoặc Redis).
            var raw = await _cache.GetStringAsync(key, cancellationToken);

            // TRƯỜNG HỢP Mới khởi động hệ hoặc chưa Bump lần nào: key trống → epoch 0.
            if (string.IsNullOrEmpty(raw))
                return 0;

            // TRƯỜNG HỢP Chuỗi hỏng (hand-edit Redis): không parse được → coi như 0 để không nổ deserialization.
            return long.TryParse(raw, CultureInfo.InvariantCulture, out var v) ? v : 0;
        }
        catch (OperationCanceledException) // Hủy có chủ đích từ client/host.
        {
            throw; // Không nuốt: stack phải thấy hủy.
        }
        catch (Exception ex) // Timeout / Redis down / lỗi mạng.
        {
            // BƯỚC degrade — Log ở mức Debug (không spam production); trả 0 ⇒ cache key không đổi epoch ⇒ vẫn có thể hit cache cũ trong TTL nếu mạng Redis hỏng một phía — trade-off có chủ đích.
            _log.LogDebug(ex, "ReadEpochAsync miss cho {Key}; dùng 0.", key);
            return 0;
        }
    }

    // -------------------------------------------------------------------------
    // BumpEpochAsync: read-modify-write tăng 1 — pattern đơn giản; không transaction multi-instance nhưng chấp nhận được cho invalidation logic.
    // -------------------------------------------------------------------------
    private async Task BumpEpochAsync(string key, CancellationToken cancellationToken)
    { // Khởi tạo next — dù là 0 hay giá trị đọc được đều sẽ +1.
        long next = 0;

        try // Bump best-effort: lỗi không bubble lên service CRUD.
        {
            // BƯỚC 2a — Đọc giá trị hiện tại (chuỗi số epoch trước bump).
            var raw = await _cache.GetStringAsync(key, cancellationToken);

            // BƯỚC 2b — Parse nếu được; không parse hoặc rỗng ⇒ next ban đầu 0 và bước sau +1 ⇒ bắt đầu từ epoch 1.
            if (!string.IsNullOrEmpty(raw)
                && long.TryParse(raw, CultureInfo.InvariantCulture, out var v))
                next = v;

            // BƯỚC 2c — Tăng epoch trong checked để OverflowException có lối thoát có kiểm soát khi đạt long.MaxValue.
            checked
            {
                next++;
            }

            // BƯỚC 2d — Ghi lại chuỗi ép kiểu InvariantCulture (ổn định parsing giữa OS).
            await _cache.SetStringAsync(
                key, // Khóa hệ epoch (__epoch:list:…).
                next.ToString(CultureInfo.InvariantCulture), // Chuỗi "123" không group separator.
                EpochRetention, // TTL rất dài để key đếm không tự expire sớm.
                cancellationToken);
        }
        catch (OperationCanceledException) // Hủy.
        {
            throw;
        }
        catch (OverflowException) // next++ sau long.MaxValue.
        {
            // TRƯỜNG HỢP Overflow — Viết reset về 1 (vẫn đổi epoch so với MaxValue ⇒ invalidation có hiệu lực; chấp nhận wrap log).
            await TryWriteEpochAsync(key, 1L, cancellationToken);
        }
        catch (Exception ex) // Mọi lỗi cache khác.
        {
            // BƯỚC degrade — chỉ log; CRUD không throw vì không thể Bump.
            _log.LogDebug(ex, "BumpEpochAsync lỗi cho {Key}; bỏ qua.", key);
        }
    }

    // -------------------------------------------------------------------------
    // TryWriteEpochAsync: helper nhỏ ghi epoch cố định (recovery sau overflow hoặc có thể mở rộng tái sync).
    // -------------------------------------------------------------------------
    private async Task TryWriteEpochAsync(string key, long value, CancellationToken cancellationToken)
    { // BƯỚC 3 — Retry write đơn lẻ không throw ra ngoài.
        try
        {
            await _cache.SetStringAsync(
                key,
                value.ToString(CultureInfo.InvariantCulture),
                EpochRetention,
                cancellationToken);
        }
        catch (Exception ex) // Lại fail — chỉ log.
        {
            _log.LogDebug(ex, "TryWriteEpochAsync lỗi cho {Key}.", key);
        }
    }
}
