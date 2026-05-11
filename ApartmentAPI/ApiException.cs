using Microsoft.AspNetCore.Http; // Mã trạng thái HTTP (404, 400, 409) cho factory ApiException.

namespace ApartmentAPI;

// Ngoại lệ nghiệp vụ — GlobalExceptionHandler trả JSON code / type / message / correlationId.
public sealed class ApiException : Exception
{ // Mở khối ApiException.
    public int StatusCode { get; } // Mã HTTP trả về client.
    public string ErrorCode { get; } // Mã lỗi ổn định (field "code" trong JSON).
    public string ClientMessage { get; } // Thông điệp an toàn cho client (field "message").

    public ApiException(int statusCode, string errorCode, string clientMessage, Exception? inner = null)
        : base(clientMessage, inner)
    { // Mở khối constructor ApiException.
        // BƯỚC 1 — Gán thuộc tính phục vụ middleware thống nhất payload lỗi.
        StatusCode = statusCode; // HTTP status.
        ErrorCode = errorCode; // Business/code field.
        ClientMessage = clientMessage; // User-facing message.
    } // Kết thúc constructor ApiException.

    // Factory 404 — tài nguyên không tồn tại hoặc không khớp id.
    public static ApiException NotFound(string code, string message) =>
        new(StatusCodes.Status404NotFound, code, message);

    // Factory 400 — dữ liệu hoặc tham số không hợp lệ.
    public static ApiException BadRequest(string code, string message) =>
        new(StatusCodes.Status400BadRequest, code, message);

    // Factory 409 — xung đột (trùng khóa, trạng thái không cho phép, v.v.).
    public static ApiException Conflict(string code, string message) =>
        new(StatusCodes.Status409Conflict, code, message);
} // Kết thúc ApiException.

// Mã lỗi ổn định (field "code") — cùng phong cách CommentAPI.
public static class ApiErrorCodes
{ // Mở khối ApiErrorCodes — hằng số mã dùng xuyên API.
    public const string NotFound = "NOT_FOUND"; // Không tìm thấy.
    public const string Validation = "VALIDATION_FAILED"; // Lỗi tổng quát validation.
    public const string ModelValidationFailed = "MODEL_VALIDATION_FAILED"; // Bad request từ rule nghiệp vụ.
    public const string Conflict = "CONFLICT"; // Xung đột tài nguyên.
    public const string Unauthenticated = "UNAUTHENTICATED"; // Chưa đăng nhập / token thiếu.
    public const string Forbidden = "FORBIDDEN"; // Đã xác thực nhưng không đủ quyền.
    public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED"; // Vượt giới hạn tần suất.
    public const string LoginFailed = "LOGIN_FAILED"; // Sai thông tin đăng nhập.
    public const string RefreshFailed = "REFRESH_FAILED"; // Làm mới phiên thất bại.
    public const string LogoutUserUnknown = "LOGOUT_USER_UNKNOWN"; // Không resolve được user khi đăng xuất.
    public const string TokenIssueFailed = "TOKEN_ISSUE_FAILED"; // Phát hành JWT thất bại.
    public const string DatabaseUpdateFailed = "DATABASE_UPDATE_FAILED"; // SaveChanges / DB lỗi cập nhật.
    public const string InvalidOperation = "INVALID_OPERATION"; // Thao tác không hợp lệ theo rule.
    public const string InternalError = "INTERNAL_ERROR"; // Lỗi hệ thống không mong đợi.
    public const string DuplicateKey = "DUPLICATE_KEY"; // Vi phạm unique constraint.
    public const string ForeignKeyViolation = "FOREIGN_KEY_VIOLATION"; // Vi phạm FK / tham chiếu.
    public const string PageSizeTooLarge = "PAGE_SIZE_TOO_LARGE"; // pageSize vượt MaxPageSize.
    public const string InvalidSortDirection = "INVALID_SORT_DIRECTION"; // sortDir không phải asc/desc.
    public const string InvalidSortColumn = "INVALID_SORT_COLUMN"; // sort không nằm trong whitelist.
    public const string RequestAborted = "REQUEST_ABORTED"; // Client hủy / timeout.
    public const string FeedbackReparentCausesCycle = "FEEDBACK_REPARENT_CAUSES_CYCLE"; // Đổi ParentId tạo chu trình cây feedback.
} // Kết thúc ApiErrorCodes.

// Thông báo cho client — thành công (message trong 200) và lỗi (message trong 4xx/5xx).
public static class ApiMessages
{ // Mở khối ApiMessages — chuỗi tiếng Anh thống nhất cho response.
    public const string Ok = "Success."; // Thông điệp mặc định thành công.
    public const string NotFound = "Resource not found."; // Không tìm thấy tài nguyên.
    public const string ValidationFailed = "The request data is invalid. Please check the submitted fields."; // Validation form/model.
    public const string Unauthenticated = "You are not authenticated for this request. Please sign in."; // 401.
    public const string InsufficientPermission = "Your account does not have permission to perform this request."; // 403.
    public const string RateLimitExceeded = "Too many requests."; // 429.
    public const string LoginFailed = "Invalid username or password."; // Đăng nhập sai.
    public const string RefreshFailed = "Session is invalid or has expired. Please sign in again."; // Refresh token hết hạn / thu hồi.
    public const string LogoutUserUnknown = "Could not resolve the current user for this session."; // Logout không biết user.
    public const string LogoutSucceeded = "Signed out successfully."; // Đăng xuất thành công.
    public const string AuthSignUpSuccess = "Account created."; // Đăng ký xong.
    public const string AuthLoginSuccess = "Login successful."; // Đăng nhập thành công.
    public const string AuthRefreshSuccess = "Tokens refreshed."; // Làm mới token thành công.
    public const string TokenIssueFailed = "Could not issue tokens."; // Tạo JWT/refresh thất bại.

    public const string ApartmentListSuccess = "Apartments retrieved successfully."; // Danh sách căn hộ.
    public const string ResidentListSuccess = "Residents retrieved successfully."; // Danh sách cư dân.
    public const string UtilityListSuccess = "Utility services retrieved successfully."; // Danh sách tiện ích.
    public const string InvoiceListSuccess = "Invoices retrieved successfully."; // Danh sách hóa đơn.
    public const string InvoiceItemListSuccess = "Invoice items retrieved successfully."; // Dòng hóa đơn.
    public const string FeedbackListSuccess = "Feedbacks retrieved successfully."; // Phản hồi.
    public const string FeedbackCteFlatSuccess = "Feedback CTE flat list retrieved successfully."; // GET .../feedbacks/cte.
    public const string FeedbackCteTreeSuccess = "Feedback tree (CTE) retrieved successfully."; // GET .../feedbacks/tree/cte.
    public const string FeedbackCteFlattenSuccess = "Feedback tree (CTE) flattened retrieved successfully."; // GET .../feedbacks/tree/cte/flatten.
    public const string PostListSuccess = "Posts retrieved successfully."; // Bài đăng / thông báo.
    public const string AttachmentListSuccess = "Attachments retrieved successfully."; // Đính kèm.
    public const string RefreshTokenListSuccess = "Refresh tokens retrieved successfully."; // Token làm mới (admin).
    public const string UserListSuccess = "Users retrieved successfully."; // Người dùng.
    public const string RoleListSuccess = "Roles retrieved successfully."; // Vai trò.

    public const string ApartmentGetSuccess = "Apartment retrieved successfully."; // Chi tiết căn hộ.
    public const string ResidentGetSuccess = "Resident retrieved successfully."; // Chi tiết cư dân.
    public const string UtilityGetSuccess = "Utility service retrieved successfully."; // Chi tiết tiện ích.
    public const string InvoiceGetSuccess = "Invoice retrieved successfully."; // Chi tiết hóa đơn.
    public const string InvoiceItemGetSuccess = "Invoice item retrieved successfully."; // Chi tiết dòng hóa đơn.
    public const string FeedbackGetSuccess = "Feedback retrieved successfully."; // Chi tiết phản hồi.
    public const string PostGetSuccess = "Post retrieved successfully."; // Chi tiết bài đăng.
    public const string AttachmentGetSuccess = "Attachment retrieved successfully."; // Chi tiết đính kèm.
    public const string RefreshTokenGetSuccess = "Refresh token retrieved successfully."; // Chi tiết refresh token.
    public const string UserGetSuccess = "User retrieved successfully."; // Chi tiết user.
    public const string RoleGetSuccess = "Role retrieved successfully."; // Chi tiết role.

    public const string ServerError = "A system error occurred. Please try again later."; // 500 tổng quát.
    public const string InvalidRequest = "The request cannot be completed with the current data."; // 400 nghiệp vụ.
    public const string RequestCancelled = "The request was cancelled or timed out."; // Hủy / timeout.
    public const string PageSizeExceedsMax = "pageSize cannot exceed {0}. Reduce the requested page size and try again."; // Phân trang: pageSize quá lớn.
    public const string CreatedAtRangeInvalid = "createdAtFrom must be less than or equal to createdAtTo."; // Khoảng ngày sai.
    public const string DuplicateKey = "A record with the same unique value already exists."; // Trùng unique.
    public const string ForeignKeyViolation = "The operation conflicts with related data in the database."; // FK.
    public const string InvalidSortDirection = "Invalid sort direction. Use asc or desc (or ascending / descending)."; // sortDir.
    public const string InvalidSortColumnGeneric = "Invalid sort column for this list. Use a column name from the API response."; // sort column.

    public const string AttachmentUploadFileRequired = "A file is required (form field 'file')."; // Multipart thiếu file.

    /// <summary>No multipart file on POST .../uploads/avatar (same tone as other ApiMessages).</summary>
    public const string UploadAvatarNoFile =
        "No file was submitted for upload. Please choose a file using the form field \"file\".";
    public const string AttachmentUploadFileEmpty = "The uploaded file is empty."; // File rỗng.
    public const string AttachmentUploadFileTooLarge = "The file exceeds the maximum allowed size ({0} bytes)."; // Vượt kích thước.
    public const string AttachmentUploadContentTypeNotAllowed = "Only image/* or application/pdf content types are allowed."; // Content-Type.
    public const string AttachmentUploadExtensionNotAllowed =
        "File extension must be one of: .jpg, .jpeg, .png, .gif, .webp, .pdf."; // Phần mở rộng.
    public const string AttachmentUploadBinaryInvalid =
        "File content is not a valid image or PDF, or the extension does not match the actual file type."; // Magic bytes không khớp.
    public const string AttachmentFeedbackIdRequired =
        "Form field 'feedbackId' is required and must be a non-empty GUID when updating a feedback-scoped attachment."; // PUT .../feedback.
    public const string AttachmentPostIdRequired =
        "Form field 'postId' is required and must be a non-empty GUID when updating a post-scoped attachment."; // PUT .../post.
    public const string AttachmentStoragePathOutsideRoot =
        "Resolved storage path is outside the application content root."; // Path traversal / cấu hình sai.

    public const string FeedbackReparentCausesCycle =
        "The chosen parent is invalid: it is this feedback or a descendant, which would form a cycle in the thread."; // Chu trình ParentId.
} // Kết thúc ApiMessages.
