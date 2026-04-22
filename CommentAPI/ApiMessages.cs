namespace CommentAPI;

// Thành công: thường là field "message" trong body 200/201.
// Lỗi: field "code" dùng hằng dưới; field "message" dùng chuỗi tương ứng.
// GlobalExceptionHandler / middleware chuyển ngoại lệ thành JSON tương thích.

// Vùng: mã lỗi ổn định (field "code" trong JSON lỗi) — client có thể map i18n.
#region Mã lỗi (field "code" trong JSON lỗi)

// Tĩnh: chứa tên mã, không lưu trạng thái; chỉ hằng số tham chiếu.
public static class ApiErrorCodes
{
    // Tổng quát: FluentValidation thất bại ở tầng service.
    public const string ValidationFailed = "VALIDATION_FAILED";

    // ModelState/binding: tham số route/body/query không hợp lệ theo [ApiController].
    public const string ModelValidationFailed = "MODEL_VALIDATION_FAILED";

    // DbUpdateException không ánh xạ sang SQL cụ thể.
    public const string DatabaseUpdateFailed = "DATABASE_UPDATE_FAILED";

    // Nghiệp vụ/đầu vào từ chối (ví dụ tìm kiếm thiếu từ khóa).
    public const string InvalidOperation = "INVALID_OPERATION";

    // Lỗi không dự tính, 500.
    public const string InternalError = "INTERNAL_ERROR";

    // Đủ xác thực nhưng thiếu quyền.
    public const string Forbidden = "FORBIDDEN";

    // Thiếu token hoặc token không dùng được.
    public const string Unauthenticated = "UNAUTHENTICATED";

    // Sai user/pass ở login.
    public const string LoginFailed = "LOGIN_FAILED";

    // Refresh token hết hạn/ không hợp lệ.
    public const string RefreshFailed = "REFRESH_FAILED";

    // Logout mà không xác định được user hiện tại.
    public const string LogoutUserUnknown = "LOGOUT_USER_UNKNOWN";

    // Không có bài theo id.
    public const string PostNotFound = "POST_NOT_FOUND";

    // Không có comment theo id.
    public const string CommentNotFound = "COMMENT_NOT_FOUND";

    // Tham chiếu parent sai post hoặc không tồn tại.
    public const string CommentParentInvalid = "COMMENT_PARENT_INVALID";

    // Vi phạm unique index (2627, 2601).
    public const string DuplicateKey = "DUPLICATE_KEY";

    // Ràng buộc FK 547.
    public const string ForeignKeyViolation = "FOREIGN_KEY_VIOLATION";

    // User không tồn tại theo yêu cầu nghiệp vụ.
    public const string UserNotFound = "USER_NOT_FOUND";

    // Trùng UserName khi tạo/sửa.
    public const string UserNameConflict = "USER_NAME_CONFLICT";

    // Trùng Email (normalized) với user khác khi admin sửa.
    public const string UserEmailConflict = "USER_EMAIL_CONFLICT";

    // Role không hợp lệ hoặc danh sách role rỗng khi admin cập nhật.
    public const string UserInvalidRoles = "USER_INVALID_ROLES";

    // Không cho gỡ Admin khỏi admin cuối cùng của hệ thống.
    public const string UserLastAdminProtected = "USER_LAST_ADMIN_PROTECTED";

    // UserManager/Identity từ chối cập nhật (username/email/role/password).
    public const string UserUpdateFailed = "USER_UPDATE_FAILED";

    // Admin phải dùng PUT /api/admin/users/{id} thay vì PUT /api/users/{id}.
    public const string UserUseAdminUpdateEndpoint = "USER_USE_ADMIN_UPDATE_ENDPOINT";

    // Tạo user thất bại ở UserManager/DB.
    public const string UserCreateFailed = "USER_CREATE_FAILED";

    // Xoá user thất bại (có thể còn phụ thuộc).
    public const string UserDeleteFailed = "USER_DELETE_FAILED";

    // Tạo JWT/refresh ổn định thất bại.
    public const string TokenIssueFailed = "TOKEN_ISSUE_FAILED";

    // Hủy/timeout.
    public const string RequestAborted = "REQUEST_ABORTED";

    // Endpoint tìm kiếm bắt buộc có từ khoá.
    public const string SearchTermRequired = "SEARCH_TERM_REQUIRED";

    // User không phải tác giả tài nguyên.
    public const string NotResourceAuthor = "NOT_RESOURCE_AUTHOR";

    // User thường cập nhật bằng endpoint user nhưng tài khoản là admin — cần endpoint admin.
    public const string UseAdminUpdateEndpoint = "USE_ADMIN_UPDATE_ENDPOINT";

    // Gắn parent tạo chu trình cây comment.
    public const string CommentReparentCausesCycle = "COMMENT_REPARENT_CAUSES_CYCLE";

    // Parent thuộc post khác.
    public const string CommentParentWrongPost = "COMMENT_PARENT_WRONG_POST";
}

#endregion

// Vùng: thông báo thành công bằng tiếng Anh (có thể thay bằng mã nội bộ nếu thêm lớp dịch).
#region Thông báo — thành công (thường là field "message" trong body 200)

// partial: cho phép tách thêm file mở rộng nếu cần (khác file hiện có phần lỗi).
public static partial class ApiMessages
{
    // Nhóm user — CRUD thành công.
    public const string UserListSuccess = "Users retrieved successfully.";
    public const string UserGetSuccess = "User retrieved successfully.";
    public const string UserCreateSuccess = "User created successfully.";
    public const string UserUpdateSuccess = "User updated successfully.";
    public const string UserAdminUpdateSuccess = "User updated successfully (admin, full profile).";
    public const string UserDeleteSuccess = "User deleted successfully.";

    // Nhóm post.
    public const string PostListSuccess = "Posts retrieved successfully.";
    public const string PostGetSuccess = "Post retrieved successfully.";
    public const string PostCreateSuccess = "Post created successfully.";
    public const string PostUpdateSuccess = "Post updated successfully.";
    public const string PostDeleteSuccess = "Post deleted successfully.";

    // Nhóm comment — CRUD cơ bản.
    public const string CommentListSuccess = "Comments retrieved successfully.";
    // Một bài: toàn bộ comment phẳng một lần (không phân trang; khác GET .../post/{id}/flat bị trần MaxPageSize).
    public const string CommentAllByPostSuccess = "All comments for the post retrieved successfully (flat list, no pagination).";
    public const string CommentGetSuccess = "Comment retrieved successfully.";
    public const string CommentCreateSuccess = "Comment created successfully.";
    public const string CommentUpdateSuccess = "Comment updated successfully.";
    public const string CommentDeleteSuccess = "Comment deleted successfully.";
    public const string CommentFlatByPostSuccess = "Flat comments for the post retrieved successfully.";
    public const string CommentCteFlatByPostSuccess = "Flat comments (CTE) for the post retrieved successfully.";
    public const string CommentTreeByPostSuccess = "Comment tree (from flat query) for the post retrieved successfully.";
    public const string CommentCteTreeByPostSuccess = "Comment tree (from CTE) for the post retrieved successfully.";

    // Cây CTE (theo post) rồi duyệt thành list phẳng preorder/DFS tùy service.
    public const string CommentFlattenCteTreeByPostSuccess =
        "Comment tree (CTE) for the post unrolled to a flat list (preorder DFS).";
    public const string CommentAllFlatSuccess = "All comments (flat, EF) retrieved successfully.";
    public const string CommentAllTreeSuccess = "All comments (tree, EF) retrieved successfully.";
    public const string CommentAllCteFlatSuccess = "All comments (flat, CTE) retrieved successfully.";
    public const string CommentAllCteTreeSuccess = "All comments (tree from CTE) retrieved successfully.";
    public const string CommentFlattenEfSuccess = "All comments flattened (EF tree + DFS) retrieved successfully.";

    // Rừng cây toàn hệ, làm phẳng EF + preorder.
    public const string CommentFlattenForestSuccess =
        "Comment forest unrolled to a flat list (EF tree + preorder DFS).";

    // Một post: cây EF làm phẳng.
    public const string CommentFlattenTreeByPostSuccess =
        "Comment tree for the post unrolled to a flat list (EF + preorder DFS).";
    public const string CommentFlattenCteSuccess = "All comments flattened (CTE) retrieved successfully.";

    // Demo — một bản ghi: lazy (proxy bật khi truy cập nav).
    public const string CommentDemoLazyLoadingSuccess =
        "Comment demo: related data loaded via lazy loading (proxies on navigation access).";

    // Demo — eager Include/AsSplitQuery.
    public const string CommentDemoEagerLoadingSuccess =
        "Comment demo: related data loaded via eager loading (Include / AsSplitQuery).";

    // Demo — explicit LoadAsync từng quan hệ.
    public const string CommentDemoExplicitLoadingSuccess =
        "Comment demo: related data loaded via explicit loading (Entry().Reference / Collection LoadAsync).";

    // Danh sách + lazy.
    public const string CommentDemoLazyLoadingListSuccess =
        "Comments demo (paged): related data loaded via lazy loading per row (proxies).";
    public const string CommentDemoEagerLoadingListSuccess =
        "Comments demo (paged): related data loaded via eager loading (Include / AsSplitQuery).";
    public const string CommentDemoExplicitLoadingListSuccess =
        "Comments demo (paged): related data loaded via explicit loading per row (Entry LoadAsync).";

    // Projection: Select tạo DTO trên server.
    public const string CommentDemoProjectionSuccess =
        "Comment demo: shape built in SQL via Select (projection), no navigation property load on client.";
    public const string CommentDemoProjectionListSuccess =
        "Comments demo (paged): rows built in SQL via Select (projection / server-side join).";

    // Tìm kiếm: user, post, comment; có kèm theo bộ lọc.
    public const string UserSearchByNameSuccess = "Users matching the display name filter retrieved successfully.";
    public const string UserSearchByUserNameSuccess = "Users matching the username filter retrieved successfully.";
    public const string PostSearchByTitleSuccess = "Posts matching the title filter retrieved successfully.";
    public const string CommentSearchByContentSuccess = "Comments matching the content filter retrieved successfully.";

    public const string CommentSearchByIdInPostSuccess = "Comment in the post retrieved by id successfully.";

    public const string CommentSearchByContentInPostSuccess =
        "Comments in the post matching the content filter retrieved successfully.";

    // Đăng xuất sạch session phía client (kèm server invalidate refresh tùy implement).
    public const string LogoutSucceeded = "Signed out successfully.";
}

#endregion

// Vùng: chuỗi lỗi hiển thị (field "message" trong body lỗi) — tương ứng mã ở ApiErrorCodes.
#region Thông báo — lỗi (field "message" trong JSON lỗi)

// Phần thứ hai của partial: chỉ hằng thông điệp lỗi thân thiện.
public static partial class ApiMessages
{
    // 401: chưa đăng nhập hoặc token không hợp lệ.
    public const string Unauthenticated =
        "You are not authenticated for this request. Please sign in.";

    // 403: policy cấm (role, quy tắc tùy chỉnh).
    public const string InsufficientPermission =
        "Your account is not allowed to perform this request.";

    // Body login không đạt validator.
    public const string LoginValidationFailed =
        "Login payload is invalid.";

    // Sai tài khoản/mật khẩu.
    public const string LoginFailed =
        "Invalid username or password.";

    // Body refresh invalid.
    public const string RefreshValidationFailed =
        "Refresh token payload is invalid.";

    // Refresh hết hạn hoặc bị thu hồi.
    public const string RefreshFailed =
        "Session is invalid or has expired. Please sign in again.";

    // Logout mà sub không đọc được.
    public const string LogoutUserUnknown =
        "Could not resolve the current user for this session.";

    // Tạo comment: id post/parent/user không khớp DB.
    public const string CommentCreateInvalidRefs =
        "Invalid post, user, or parent comment reference.";

    // Không tìm thấy comment theo thao tác.
    public const string CommentNotFound =
        "The specified comment was not found.";

    // Parent null hoặc thuộc post khác.
    public const string CommentParentInvalid =
        "The parent comment does not exist on this post.";

    // Post không tồn tại.
    public const string PostNotFound =
        "The specified post was not found.";

    // User không tồn tại theo id.
    public const string UserNotFound =
        "The specified user was not found.";

    // Trùng UserName ở Identity.
    public const string UserNameTaken =
        "That username is already taken.";

    // Email đã gắn tài khoản khác (admin đổi email).
    public const string UserEmailTaken =
        "This email is already assigned to another account.";

    // Role không thuộc tập cho phép hoặc danh sách role không hợp lệ.
    public const string UserInvalidRoles =
        "Roles must be a non-empty list of allowed values: Admin, User (no duplicates).";

    // Gỡ quyền Admin khỏi admin duy nhất.
    public const string UserLastAdminProtected =
        "Cannot remove the Admin role from the only administrator account.";

    // Cập nhật user qua Identity thất bại.
    public const string UserUpdateFailed =
        "The user could not be updated. Check username, email, password rules, and roles.";

    // Admin không được dùng PUT /api/users/{id} (chỉ Name).
    public const string UserUseAdminUpdateEndpoint =
        "Administrator accounts must use PUT /api/admin/users/{id} with the full admin update body.";

    // CreateUser: Identity trả thất bại tổng quát.
    public const string UserCreateFailed =
        "Could not create the user. Please verify the submitted information.";

    // Xoá user: lỗi bất thường (FK, hệ thống).
    public const string UserDeleteFailed =
        "The user could not be deleted. Please try again or verify dependencies.";

    // Unique constraint SQL.
    public const string DuplicateKey =
        "A record with the same unique value already exists.";

    // Khóa ngoài / bản ghi liên quan cản bước.
    public const string ForeignKeyViolation =
        "The operation conflicts with related data in the database.";

    // Phát hành token lỗi ở lớp auth.
    public const string TokenIssueFailed =
        "Tokens could not be issued for this account. Please contact support.";

    // Dữ liệu không hợp lệ tổng quát.
    public const string ValidationFailed =
        "The request data is invalid. Please check the submitted fields.";

    // 400: không thể thực hiện với bộ dữ liệu hiện tại.
    public const string InvalidRequest =
        "The request cannot be completed with the current data.";

    // 500: lỗi hệ thống không ánh xạ chi tiết.
    public const string ServerError =
        "A system error occurred. Please try again later.";

    // 408/499: hủy/timeout.
    public const string RequestCancelled =
        "The request was cancelled or timed out.";

    // Tìm kiếm thiếu chuỗi sau trim.
    public const string SearchTermRequired =
        "Search term is required and cannot be empty.";

    // Tác giả resource không khớp current user.
    public const string NotResourceAuthor =
        "You are not the author of this resource. Only the author may perform this action.";

    // Admin cần endpoint admin cập nhật resource.
    public const string UseAdminUpdateEndpoint =
        "This account is an administrator. Use the PUT /api/admin/... update endpoint for this resource.";

    // Gắn parent tạo vòng cây.
    public const string CommentReparentCausesCycle =
        "The chosen parent is invalid: it is this comment or a descendant, which would form a cycle in the thread.";

    // Parent không cùng post đích.
    public const string CommentParentWrongPost =
        "The parent comment is missing or not in the same target post as this comment.";
}

#endregion

// Vùng: ngoại lệ nghiệp vụ — GlobalExceptionHandler đọc StatusCode, ErrorCode, ClientMessage.
#region Exception nghiệp vụ (throw → GlobalExceptionHandler)

// Sealed: không cần kế thừa; giữ bộ StatusCode/ErrorCode/ClientMessage ổn định.
public sealed class ApiException : Exception
{
    // Mã HTTP gợi ý (400, 401, 404, 409, v.v.) — client nhận qua response status.
    public int StatusCode { get; }
    // Một trong ApiErrorCodes.
    public string ErrorCode { get; }
    // Nội dung hiển thị/serial JSON, có thể trùng ApiMessages hằng tương ứng.
    public string ClientMessage { get; }

    // Có thể bọc ngoại lệ gốc từ DB/inner để ghi log (detail dev).
    public ApiException(int statusCode, string errorCode, string clientMessage, Exception? innerException = null)
        : base(clientMessage, innerException)
    {
        StatusCode = statusCode; // Gán số bước ghi phản hồi
        ErrorCode = errorCode; // Gán mã ổn định
        ClientMessage = clientMessage; // Thông điệp tới client
    }
}

#endregion
