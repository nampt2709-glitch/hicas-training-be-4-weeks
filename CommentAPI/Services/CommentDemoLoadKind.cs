namespace CommentAPI.Services;

// Enum: phân loại demo trong CommentService / CommentRepository — chọn chiến lược nạp quan hệ EF (lazy / eager / explicit / projection).
public enum CommentDemoLoadKind
{
    // Lazy: proxy LazyLoadingProxies — truy vấn bổ sung khi đọc navigation.
    Lazy,
    // Eager: Include / ThenInclude / AsSplitQuery — gom quan hệ trong (các) câu SQL đầu.
    Eager,
    // Explicit: Entry(...).Reference/Collection.LoadAsync — nạp từng quan hệ khi code yêu cầu.
    Explicit,
    // Projection: Select sang DTO trên server — không tải full entity navigation theo đường mặc định.
    Projection,
} // Kết thúc enum CommentDemoLoadKind.
