namespace CommentAPI;

// =============================================================================
// ApiMessages.cs — chuỗi JSON: thành công / lỗi khi client gọi API.
// ApiErrorCodes — mã "code" trong JSON lỗi.
// ApiException — ném từ service/controller.
//
// Thông báo thành công (200…) do controller/service trả qua IActionResult.
// GlobalExceptionHandler chỉ gắn chuỗi lỗi khi có exception chưa bắt (vùng lỗi bên dưới + vài mã trong ApiErrorCodes).
// =============================================================================

#region Mã lỗi (field "code" trong JSON lỗi)

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
    public const string CommentNotFound = "COMMENT_NOT_FOUND";
    public const string CommentParentInvalid = "COMMENT_PARENT_INVALID";
    public const string DuplicateKey = "DUPLICATE_KEY";
    public const string ForeignKeyViolation = "FOREIGN_KEY_VIOLATION";
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string UserNameConflict = "USER_NAME_CONFLICT";
    public const string UserCreateFailed = "USER_CREATE_FAILED";
    public const string UserDeleteFailed = "USER_DELETE_FAILED";
    public const string TokenIssueFailed = "TOKEN_ISSUE_FAILED";
    public const string RequestAborted = "REQUEST_ABORTED";
    public const string SearchTermRequired = "SEARCH_TERM_REQUIRED";
}

#endregion

#region Thông báo — thành công (thường là field "message" trong body 200)

public static partial class ApiMessages
{
    public const string UserListSuccess = "Users retrieved successfully.";
    public const string UserGetSuccess = "User retrieved successfully.";
    public const string UserCreateSuccess = "User created successfully.";
    public const string UserUpdateSuccess = "User updated successfully.";
    public const string UserDeleteSuccess = "User deleted successfully.";

    public const string PostListSuccess = "Posts retrieved successfully.";
    public const string PostGetSuccess = "Post retrieved successfully.";
    public const string PostCreateSuccess = "Post created successfully.";
    public const string PostUpdateSuccess = "Post updated successfully.";
    public const string PostDeleteSuccess = "Post deleted successfully.";

    public const string CommentListSuccess = "Comments retrieved successfully.";
    public const string CommentGetSuccess = "Comment retrieved successfully.";
    public const string CommentCreateSuccess = "Comment created successfully.";
    public const string CommentUpdateSuccess = "Comment updated successfully.";
    public const string CommentDeleteSuccess = "Comment deleted successfully.";
    public const string CommentFlatByPostSuccess = "Flat comments for the post retrieved successfully.";
    public const string CommentCteFlatByPostSuccess = "Flat comments (CTE) for the post retrieved successfully.";
    public const string CommentTreeByPostSuccess = "Comment tree (from flat query) for the post retrieved successfully.";
    public const string CommentCteTreeByPostSuccess = "Comment tree (from CTE) for the post retrieved successfully.";
    public const string CommentFlattenCteTreeByPostSuccess =
        "Comment tree (CTE) for the post unrolled to a flat list (preorder DFS).";
    public const string CommentAllFlatSuccess = "All comments (flat, EF) retrieved successfully.";
    public const string CommentAllTreeSuccess = "All comments (tree, EF) retrieved successfully.";
    public const string CommentAllCteFlatSuccess = "All comments (flat, CTE) retrieved successfully.";
    public const string CommentAllCteTreeSuccess = "All comments (tree from CTE) retrieved successfully.";
    public const string CommentFlattenEfSuccess = "All comments flattened (EF tree + DFS) retrieved successfully.";
    public const string CommentFlattenForestSuccess =
        "Comment forest unrolled to a flat list (EF tree + preorder DFS).";
    public const string CommentFlattenTreeByPostSuccess =
        "Comment tree for the post unrolled to a flat list (EF + preorder DFS).";
    public const string CommentFlattenCteSuccess = "All comments flattened (CTE) retrieved successfully.";

    public const string CommentDemoLazyLoadingSuccess =
        "Comment demo: related data loaded via lazy loading (proxies on navigation access).";

    public const string CommentDemoEagerLoadingSuccess =
        "Comment demo: related data loaded via eager loading (Include / AsSplitQuery).";

    public const string CommentDemoExplicitLoadingSuccess =
        "Comment demo: related data loaded via explicit loading (Entry().Reference / Collection LoadAsync).";

    public const string CommentDemoLazyLoadingListSuccess =
        "Comments demo (paged): related data loaded via lazy loading per row (proxies).";

    public const string CommentDemoEagerLoadingListSuccess =
        "Comments demo (paged): related data loaded via eager loading (Include / AsSplitQuery).";

    public const string CommentDemoExplicitLoadingListSuccess =
        "Comments demo (paged): related data loaded via explicit loading per row (Entry LoadAsync).";

    public const string CommentDemoProjectionSuccess =
        "Comment demo: shape built in SQL via Select (projection), no navigation property load on client.";

    public const string CommentDemoProjectionListSuccess =
        "Comments demo (paged): rows built in SQL via Select (projection / server-side join).";

    public const string UserSearchByNameSuccess = "Users matching the display name filter retrieved successfully.";
    public const string UserSearchByUserNameSuccess = "Users matching the username filter retrieved successfully.";
    public const string PostSearchByTitleSuccess = "Posts matching the title filter retrieved successfully.";
    public const string CommentSearchByContentSuccess = "Comments matching the content filter retrieved successfully.";

    public const string CommentSearchByIdInPostSuccess = "Comment in the post retrieved by id successfully.";

    public const string CommentSearchByContentInPostSuccess =
        "Comments in the post matching the content filter retrieved successfully.";

    public const string LogoutSucceeded = "Signed out successfully.";
}

#endregion

#region Thông báo — lỗi (field "message" trong JSON lỗi)

public static partial class ApiMessages
{
    public const string Unauthenticated =
        "You are not authenticated for this request. Please sign in.";

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

    public const string CommentCreateInvalidRefs =
        "Invalid post, user, or parent comment reference.";

    public const string CommentNotFound =
        "The specified comment was not found.";

    public const string CommentParentInvalid =
        "The parent comment does not exist on this post.";

    public const string PostNotFound =
        "The specified post was not found.";

    public const string UserNotFound =
        "The specified user was not found.";

    public const string UserNameTaken =
        "That username is already taken.";

    public const string UserCreateFailed =
        "Could not create the user. Please verify the submitted information.";

    public const string UserDeleteFailed =
        "The user could not be deleted. Please try again or verify dependencies.";

    public const string DuplicateKey =
        "A record with the same unique value already exists.";

    public const string ForeignKeyViolation =
        "The operation conflicts with related data in the database.";

    public const string TokenIssueFailed =
        "Tokens could not be issued for this account. Please contact support.";

    public const string ValidationFailed =
        "The request data is invalid. Please check the submitted fields.";

    public const string InvalidRequest =
        "The request cannot be completed with the current data.";

    public const string ServerError =
        "A system error occurred. Please try again later.";

    public const string RequestCancelled =
        "The request was cancelled or timed out.";

    public const string SearchTermRequired =
        "Search term is required and cannot be empty.";
}

#endregion

#region Exception nghiệp vụ (throw → GlobalExceptionHandler)

/// <summary>
/// Ném khi vi phạm nghiệp vụ; GlobalExceptionHandler trả JSON với <see cref="ApiErrorCodes"/> và chuỗi lỗi (các hằng trong vùng lỗi của <see cref="ApiMessages"/>).
/// </summary>
public sealed class ApiException : Exception
{
    public int StatusCode { get; }
    public string ErrorCode { get; }
    public string ClientMessage { get; }

    public ApiException(int statusCode, string errorCode, string clientMessage, Exception? innerException = null)
        : base(clientMessage, innerException)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ClientMessage = clientMessage;
    }
}

#endregion
