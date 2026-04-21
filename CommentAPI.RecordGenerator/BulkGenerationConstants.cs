namespace CommentAPI.RecordGenerator;

/// <summary>
/// Hằng số nhận diện lô dữ liệu sinh bởi RecordGenerator (email/mật khẩu cố định để cleanup an toàn).
/// </summary>
internal static class BulkGenerationConstants
{
    /// <summary>Hậu tố NormalizedEmail cho mọi user bulk (SQL so khớp LIKE/EndsWith).</summary>
    internal const string NormalizedEmailSuffix = "@BULKGEN.RECORDGENERATOR.LOCAL";

    /// <summary>Tài khoản đánh dấu hoàn tất seed (không dùng để đăng bài/comment).</summary>
    internal const string MarkerUserName = "bulkgen_marker";

    /// <summary>Mật khẩu thống nhất cho user bulk (đủ mạnh cho UserManager mặc định).</summary>
    internal const string BulkUserPassword = "CommentAPI@123";

    /// <summary>Tổng số bản ghi nghiệp vụ (Users + Posts + Comments) = 100k.</summary>
    internal const int TotalBusinessRows = 100_000;

    internal const int UserCount = 2500;

    /// <summary>Số tài khoản nội dung đầu tiên được gán thêm vai trò Admin (cùng User).</summary>
    internal const int BulkAdminCount = 10;

    internal const int PostCount = 7500;
    internal const int CommentCount = 90_000;
}
