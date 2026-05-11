using Microsoft.AspNetCore.Http; // IFormFile multipart.

namespace ApartmentAPI.V1.DTOs;

// Chỉ chứa file — route POST .../users/{userId}/avatar xác định scope Avatar; Swagger không có field Scope/Guid mặc định gây hiểu nhầm.
public sealed class AvatarAttachmentUploadModel
{
    public IFormFile File { get; set; } = null!; // Một file ảnh/PDF — validator kiểm magic bytes.
}

// Multipart cho POST .../uploads/avatar — cùng quy tắc Avatar; thông báo lỗi thống nhất ApiMessages (tiếng Anh).
public sealed class UploadsAvatarUploadModel
{
    public IFormFile File { get; set; } = null!; // Tệp avatar mới; thay mọi avatar Avatar-scope cũ của user hiện tại.
}

// Chỉ chứa file — route POST .../posts/{postId}/files xác định bài đăng; UserId gán theo tác giả bài.
public sealed class PostAttachmentUploadModel
{
    public IFormFile File { get; set; } = null!; // File đính kèm nội dung bài đăng.
}

// Chỉ chứa file — route POST .../feedbacks/{feedbackId}/files xác định feedback; UserId gán server theo tác giả feedback.
public sealed class FeedbackAttachmentUploadModel
{
    public IFormFile File { get; set; } = null!; // File đính kèm nội dung phản hồi.
}

// PUT .../attachments/{id}/avatar — đổi nội dung file (tuỳ chọn); scope/ user cố định theo bản ghi hiện có (chỉ đổi file).
public sealed class UpdateAvatarAttachmentFormModel
{
    public IFormFile? File { get; set; } // Có giá trị = thay file + kiểm tra MIME/giới hạn như tạo.
}

// PUT .../attachments/{id}/feedback — file tuỳ chọn + FeedbackId bắt buộc (multipart: client gửi field feedbackId, null không hợp lệ).
public sealed class UpdateFeedbackAttachmentFormModel
{
    public IFormFile? File { get; set; } // Null = giữ bytes cũ.
    public Guid? FeedbackId { get; set; } // Bắt buộc khi gửi — FluentValidation chặn thiếu/Empty (tránh 00000000... làm giá trị mặc định hiển thị).
}

// PUT .../attachments/{id}/post — file tuỳ chọn + PostId bắt buộc trong form multipart.
public sealed class UpdatePostAttachmentFormModel
{
    public IFormFile? File { get; set; } // Null = giữ bytes cũ.
    public Guid? PostId { get; set; } // Bắt buộc — validator chặn Empty.
}
