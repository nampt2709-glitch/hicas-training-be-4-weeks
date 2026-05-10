// File: cấu hình lưu file đính kèm (Day 17 — upload ảnh/file): thư mục con dưới ContentRoot + giới hạn kích thước.
namespace ApartmentAPI;

// Binding section "AttachmentStorage": đường dẫn gốc tương đối + max bytes mỗi file.
public sealed class AttachmentStorageOptions
{ // Mở khối AttachmentStorageOptions.
    public const string SectionName = "AttachmentStorage"; // Tên section trong IConfiguration.

    // Đường dẫn tương đối (ví dụ uploads/attachments), không bắt đầu bằng /.
    public string RootRelativePath { get; set; } = "uploads/attachments"; // Thư mục con lưu file đã normalize.

    // Kích thước tối đa mỗi file (byte) — kiểm tra trước khi ghi đĩa.
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024; // Mặc định 20 MB.
} // Kết thúc AttachmentStorageOptions.
