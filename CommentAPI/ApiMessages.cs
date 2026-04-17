namespace CommentAPI;

/// <summary>
/// Mã lỗi API cố định cho client và tài liệu (không phụ thuộc ngôn ngữ hiển thị).
/// </summary>
public static class ApiErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string ModelValidationFailed = "MODEL_VALIDATION_FAILED";
    public const string DatabaseUpdateFailed = "DATABASE_UPDATE_FAILED";
    public const string InvalidOperation = "INVALID_OPERATION";
    public const string InternalError = "INTERNAL_ERROR";
    public const string Forbidden = "FORBIDDEN";
    public const string Unauthenticated = "UNAUTHENTICATED";
    public const string LoginFailed = "LOGIN_FAILED";
    public const string RefreshFailed = "REFRESH_FAILED";
    public const string LogoutUserUnknown = "LOGOUT_USER_UNKNOWN";
    public const string PostNotFound = "POST_NOT_FOUND";
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string UserNameConflict = "USER_NAME_CONFLICT";
}

/// <summary>
/// Các chuỗi thông báo đưa vào body JSON khi thành công hoặc lỗi (hiển thị cho người gọi API).
/// </summary>
public static class ApiMessages
{
    // --- Users ---
    public const string UserListSuccess = "Users retrieved successfully.";
    public const string UserGetSuccess = "User retrieved successfully.";
    public const string UserCreateSuccess = "User created successfully.";
    public const string UserUpdateSuccess = "User updated successfully.";
    public const string UserDeleteSuccess = "User deleted successfully.";

    // --- Posts ---
    public const string PostListSuccess = "Posts retrieved successfully.";
    public const string PostGetSuccess = "Post retrieved successfully.";
    public const string PostCreateSuccess = "Post created successfully.";
    public const string PostUpdateSuccess = "Post updated successfully.";
    public const string PostDeleteSuccess = "Post deleted successfully.";

    // --- Comments ---
    public const string CommentListSuccess = "Comments retrieved successfully.";
    public const string CommentGetSuccess = "Comment retrieved successfully.";
    public const string CommentCreateSuccess = "Comment created successfully.";
    public const string CommentUpdateSuccess = "Comment updated successfully.";
    public const string CommentDeleteSuccess = "Comment deleted successfully.";
    public const string CommentFlatByPostSuccess = "Flat comments for the post retrieved successfully.";
    public const string CommentCteFlatByPostSuccess = "Flat comments (CTE) for the post retrieved successfully.";
    public const string CommentTreeByPostSuccess = "Comment tree (from flat query) for the post retrieved successfully.";
    public const string CommentCteTreeByPostSuccess = "Comment tree (from CTE) for the post retrieved successfully.";
    public const string CommentAllFlatSuccess = "All comments (flat, EF) retrieved successfully.";
    public const string CommentAllTreeSuccess = "All comments (tree, EF) retrieved successfully.";
    public const string CommentAllCteFlatSuccess = "All comments (flat, CTE) retrieved successfully.";
    public const string CommentAllCteTreeSuccess = "All comments (tree from CTE) retrieved successfully.";
    public const string CommentFlattenEfSuccess = "All comments flattened (EF tree + DFS) retrieved successfully.";
    public const string CommentFlattenCteSuccess = "All comments flattened (CTE) retrieved successfully.";

    public const string Unauthenticated =
        "You are not authenticated for this request. Please sign in.";

    /// <summary>
    /// Thông báo khi đã xác thực nhưng không đủ quyền (403, role hoặc policy).
    /// </summary>
    public const string InsufficientPermission =
        "Your account is not allowed to perform this request.";

    public const string LoginValidationFailed =
        "Login payload is invalid.";

    public const string LoginFailed =
        "Invalid username or password.";

    public const string RefreshValidationFailed =
        "Refresh token payload is invalid.";

    public const string RefreshFailed =
        "Session is invalid or has expired. Please sign in again.";

    public const string LogoutUserUnknown =
        "Could not resolve the current user for this session.";

    public const string LogoutSucceeded =
        "Signed out successfully.";

    public const string CommentCreateInvalidRefs =
        "Invalid post, user, or parent comment reference.";

    public const string PostNotFoundMessage =
        "The specified post was not found.";

    public const string UserNotFoundMessage =
        "The specified user was not found.";

    public const string UserNameTaken =
        "That username is already taken.";

    public const string UserCreateFailed =
        "Could not create the user. Please verify the submitted information.";

    public const string ValidationFailed =
        "The request data is invalid. Please check the submitted fields.";

    public const string InvalidRequest =
        "The request cannot be completed with the current data.";

    public const string ServerError =
        "A system error occurred. Please try again later.";
}
