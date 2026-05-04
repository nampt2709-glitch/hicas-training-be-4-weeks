namespace CommentAPI.Services;

// Phân loại demo kiểu nạp quan hệ (lazy / eager / explicit / projection) — dùng chung helper trong CommentService.
public enum CommentDemoLoadKind
{
    Lazy,
    Eager,
    Explicit,
    Projection,
}
