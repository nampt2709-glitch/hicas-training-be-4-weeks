namespace ApartmentAPI.RecordGenerator;

/// <summary>
/// Hằng số nhận diện lô dữ liệu sinh bởi RecordGenerator (email/mật khẩu/ CreatedBy để cleanup an toàn).
/// </summary>
internal static class BulkGenerationConstants
{
    /// <summary>Hậu tố NormalizedEmail cho mọi user bulk (SQL so khớp LIKE).</summary>
    internal const string NormalizedEmailSuffix = "@BULKGEN.RECORDGENERATOR.LOCAL";

    /// <summary>Tài khoản đánh dấu lô (không gán làm tác giả chính cho feedback nghiệp vụ).</summary>
    internal const string MarkerUserName = "apt_bulkgen_marker";

    /// <summary>Mật khẩu thống nhất cho user bulk (đủ mạnh cho UserManager mặc định).</summary>
    internal const string BulkUserPassword = "ApartmentAPI@123";

    /// <summary>Ghi vào CreatedBy trên entity BaseEntity để xóa lô bằng SQL có thứ tự.</summary>
    internal const string BulkCreatedByMarker = "ApartmentAPI.RecordGenerator";

    /// <summary>Tổng hàng nghiệp vụ ~100k (Identity + căn hộ + dịch vụ + cư dân + hóa đơn + …).</summary>
    internal const int TotalBusinessRowsApprox = 100_000;

    internal const int UserCount = 2500;

    /// <summary>Số tài khoản nội dung đầu tiên được gán thêm vai trò Admin.</summary>
    internal const int BulkAdminCount = 10;

    internal const int ApartmentCount = 1700;
    internal const int UtilityServiceCount = 40;
    internal const int ResidentCount = 3200;
    internal const int InvoiceCount = 8000;
    internal const int InvoiceItemTotal = 24_600;
    /// <summary>Phản hồi — giảm một phần so với trước để giữ tổng ~100k khi thêm Post.</summary>
    internal const int FeedbackCount = 48_960;

    /// <summary>Bài đăng / thông báo bulk — bù vào tổng hàng nghiệp vụ.</summary>
    internal const int PostCount = 2000;

    internal const int AttachmentCount = 5000;
    internal const int RefreshTokenCount = 4000;
}
