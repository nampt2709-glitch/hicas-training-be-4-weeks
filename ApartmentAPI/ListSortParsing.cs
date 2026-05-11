
namespace ApartmentAPI;

// Parse sort + sortDir cho GET phân trang (whitelist cột, mặc định CreatedAt hoặc Name tùy entity).
public static class ListSortParsing
{ // Mở khối ListSortParsing — chuyển query thành struct sort an toàn kiểu.
    // Chuẩn hóa sortDir: null/empty → asc; desc/descending → true; asc/ascending → false; còn lại → 400.
    public static bool ParseDescending(string? sortDir)
    { // Mở khối ParseDescending.
        // TRƯỜNG HỢP A — Không gửi hướng sort → mặc định tăng dần (asc).
        if (string.IsNullOrWhiteSpace(sortDir))
            return false;
        var d = sortDir.Trim(); // Bỏ khoảng trắng dư.

        // TRƯỜNG HỢP B — Giảm dần.
        if (d.Equals("desc", StringComparison.OrdinalIgnoreCase)
            || d.Equals("descending", StringComparison.OrdinalIgnoreCase))
            return true;

        // TRƯỜNG HỢP C — Tăng dần tường minh.
        if (d.Equals("asc", StringComparison.OrdinalIgnoreCase)
            || d.Equals("ascending", StringComparison.OrdinalIgnoreCase))
            return false;

        // TRƯỜNG HỢP D — Giá trị không thuộc whitelist → ném ApiException validation.
        throw new ApiException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.InvalidSortDirection,
            ApiMessages.InvalidSortDirection);
    } // Kết thúc ParseDescending.

    // Ném lỗi cột sort không hợp kèm danh sách cột được phép.
    private static ApiException InvalidSort(string columns) =>
        ApiException.BadRequest(
            ApiErrorCodes.InvalidSortColumn,
            $"{ApiMessages.InvalidSortColumnGeneric} Expected: {columns}.");

    // Apartment: map chuỗi sort → ApartmentSortColumn + hướng.
    public static ApartmentListSort ParseApartmentSort(string? sort, string? sortDir)
    { // Mở khối ParseApartmentSort.
        // BƯỚC 1 — Parse hướng sort (asc/desc).
        var desc = ParseDescending(sortDir);
        // BƯỚC 2 — sort rỗng → mặc định CreatedAt.
        if (string.IsNullOrWhiteSpace(sort))
            return new ApartmentListSort(ApartmentSortColumn.CreatedAt, desc);
        // BƯỚC 3 — Trim tên cột rồi map qua whitelist.
        return new ApartmentListSort(ParseApartmentColumn(sort.Trim()), desc);
    } // Kết thúc ParseApartmentSort.

    // Map không phân biệt hoa thường cho từng cột Apartment; sai → InvalidSort.
    private static ApartmentSortColumn ParseApartmentColumn(string c) =>
        c.Equals("id", StringComparison.OrdinalIgnoreCase) ? ApartmentSortColumn.Id
        : c.Equals("floor", StringComparison.OrdinalIgnoreCase) ? ApartmentSortColumn.Floor
        : c.Equals("roomNumber", StringComparison.OrdinalIgnoreCase) || c.Equals("room", StringComparison.OrdinalIgnoreCase) ? ApartmentSortColumn.RoomNumber
        : c.Equals("area", StringComparison.OrdinalIgnoreCase) ? ApartmentSortColumn.Area
        : c.Equals("status", StringComparison.OrdinalIgnoreCase) ? ApartmentSortColumn.Status
        : c.Equals("maxResidents", StringComparison.OrdinalIgnoreCase) ? ApartmentSortColumn.MaxResidents
        : c.Equals("createdAt", StringComparison.OrdinalIgnoreCase) ? ApartmentSortColumn.CreatedAt
        : throw InvalidSort("Id, Floor, RoomNumber, Area, Status, MaxResidents, CreatedAt");

    // Resident: sort mặc định CreatedAt.
    public static ResidentListSort ParseResidentSort(string? sort, string? sortDir)
    { // Mở khối ParseResidentSort.
        var desc = ParseDescending(sortDir);
        if (string.IsNullOrWhiteSpace(sort))
            return new ResidentListSort(ResidentSortColumn.CreatedAt, desc);
        var c = sort.Trim();
        var col = c.Equals("id", StringComparison.OrdinalIgnoreCase) ? ResidentSortColumn.Id
            : c.Equals("fullName", StringComparison.OrdinalIgnoreCase) || c.Equals("name", StringComparison.OrdinalIgnoreCase) ? ResidentSortColumn.FullName
            : c.Equals("identityNumber", StringComparison.OrdinalIgnoreCase) ? ResidentSortColumn.IdentityNumber
            : c.Equals("apartmentId", StringComparison.OrdinalIgnoreCase) ? ResidentSortColumn.ApartmentId
            : c.Equals("createdAt", StringComparison.OrdinalIgnoreCase) ? ResidentSortColumn.CreatedAt
            : throw InvalidSort("Id, FullName, IdentityNumber, ApartmentId, CreatedAt");
        return new ResidentListSort(col, desc);
    } // Kết thúc ParseResidentSort.

    // Utility service catalog.
    public static UtilityListSort ParseUtilitySort(string? sort, string? sortDir)
    { // Mở khối ParseUtilitySort.
        var desc = ParseDescending(sortDir);
        if (string.IsNullOrWhiteSpace(sort))
            return new UtilityListSort(UtilitySortColumn.CreatedAt, desc);
        var c = sort.Trim();
        var col = c.Equals("id", StringComparison.OrdinalIgnoreCase) ? UtilitySortColumn.Id
            : c.Equals("name", StringComparison.OrdinalIgnoreCase) ? UtilitySortColumn.Name
            : c.Equals("price", StringComparison.OrdinalIgnoreCase) ? UtilitySortColumn.Price
            : c.Equals("unit", StringComparison.OrdinalIgnoreCase) ? UtilitySortColumn.Unit
            : c.Equals("isActive", StringComparison.OrdinalIgnoreCase) ? UtilitySortColumn.IsActive
            : c.Equals("createdAt", StringComparison.OrdinalIgnoreCase) ? UtilitySortColumn.CreatedAt
            : throw InvalidSort("Id, Name, Price, Unit, IsActive, CreatedAt");
        return new UtilityListSort(col, desc);
    } // Kết thúc ParseUtilitySort.

    // Hóa đơn.
    public static InvoiceListSort ParseInvoiceSort(string? sort, string? sortDir)
    { // Mở khối ParseInvoiceSort.
        var desc = ParseDescending(sortDir);
        if (string.IsNullOrWhiteSpace(sort))
            return new InvoiceListSort(InvoiceSortColumn.CreatedAt, desc);
        var c = sort.Trim();
        var col = c.Equals("id", StringComparison.OrdinalIgnoreCase) ? InvoiceSortColumn.Id
            : c.Equals("invoiceCode", StringComparison.OrdinalIgnoreCase) ? InvoiceSortColumn.InvoiceCode
            : c.Equals("year", StringComparison.OrdinalIgnoreCase) ? InvoiceSortColumn.Year
            : c.Equals("month", StringComparison.OrdinalIgnoreCase) ? InvoiceSortColumn.Month
            : c.Equals("totalAmount", StringComparison.OrdinalIgnoreCase) ? InvoiceSortColumn.TotalAmount
            : c.Equals("status", StringComparison.OrdinalIgnoreCase) ? InvoiceSortColumn.Status
            : c.Equals("apartmentId", StringComparison.OrdinalIgnoreCase) ? InvoiceSortColumn.ApartmentId
            : c.Equals("createdAt", StringComparison.OrdinalIgnoreCase) ? InvoiceSortColumn.CreatedAt
            : throw InvalidSort("Id, InvoiceCode, Year, Month, TotalAmount, Status, ApartmentId, CreatedAt");
        return new InvoiceListSort(col, desc);
    } // Kết thúc ParseInvoiceSort.

    // Dòng hóa đơn.
    public static InvoiceItemListSort ParseInvoiceItemSort(string? sort, string? sortDir)
    { // Mở khối ParseInvoiceItemSort.
        var desc = ParseDescending(sortDir);
        if (string.IsNullOrWhiteSpace(sort))
            return new InvoiceItemListSort(InvoiceItemSortColumn.CreatedAt, desc);
        var c = sort.Trim();
        var col = c.Equals("id", StringComparison.OrdinalIgnoreCase) ? InvoiceItemSortColumn.Id
            : c.Equals("invoiceId", StringComparison.OrdinalIgnoreCase) ? InvoiceItemSortColumn.InvoiceId
            : c.Equals("serviceId", StringComparison.OrdinalIgnoreCase) ? InvoiceItemSortColumn.ServiceId
            : c.Equals("subTotal", StringComparison.OrdinalIgnoreCase) ? InvoiceItemSortColumn.SubTotal
            : c.Equals("createdAt", StringComparison.OrdinalIgnoreCase) ? InvoiceItemSortColumn.CreatedAt
            : throw InvalidSort("Id, InvoiceId, ServiceId, SubTotal, CreatedAt");
        return new InvoiceItemListSort(col, desc);
    } // Kết thúc ParseInvoiceItemSort.

    // Feedback (cây cha–con).
    public static FeedbackListSort ParseFeedbackSort(string? sort, string? sortDir)
    { // Mở khối ParseFeedbackSort.
        var desc = ParseDescending(sortDir);
        if (string.IsNullOrWhiteSpace(sort))
            return new FeedbackListSort(FeedbackSortColumn.CreatedAt, desc);
        var c = sort.Trim();
        var col = c.Equals("id", StringComparison.OrdinalIgnoreCase) ? FeedbackSortColumn.Id
            : c.Equals("userId", StringComparison.OrdinalIgnoreCase) ? FeedbackSortColumn.UserId
            : c.Equals("isPinned", StringComparison.OrdinalIgnoreCase) ? FeedbackSortColumn.IsPinned
            : c.Equals("isResolved", StringComparison.OrdinalIgnoreCase) ? FeedbackSortColumn.IsResolved
            : c.Equals("parentId", StringComparison.OrdinalIgnoreCase) ? FeedbackSortColumn.ParentId
            : c.Equals("createdAt", StringComparison.OrdinalIgnoreCase) ? FeedbackSortColumn.CreatedAt
            : throw InvalidSort("Id, UserId, IsPinned, IsResolved, ParentId, CreatedAt");
        return new FeedbackListSort(col, desc);
    } // Kết thúc ParseFeedbackSort.

    // Bài đăng / thông báo.
    public static PostListSort ParsePostSort(string? sort, string? sortDir)
    { // Mở khối ParsePostSort.
        var desc = ParseDescending(sortDir);
        if (string.IsNullOrWhiteSpace(sort))
            return new PostListSort(PostSortColumn.CreatedAt, desc);
        var c = sort.Trim();
        var col = c.Equals("id", StringComparison.OrdinalIgnoreCase) ? PostSortColumn.Id
            : c.Equals("userId", StringComparison.OrdinalIgnoreCase) ? PostSortColumn.UserId
            : c.Equals("apartmentId", StringComparison.OrdinalIgnoreCase) ? PostSortColumn.ApartmentId
            : c.Equals("title", StringComparison.OrdinalIgnoreCase) ? PostSortColumn.Title
            : c.Equals("isPublished", StringComparison.OrdinalIgnoreCase) ? PostSortColumn.IsPublished
            : c.Equals("createdAt", StringComparison.OrdinalIgnoreCase) ? PostSortColumn.CreatedAt
            : throw InvalidSort("Id, UserId, ApartmentId, Title, IsPublished, CreatedAt");
        return new PostListSort(col, desc);
    } // Kết thúc ParsePostSort.

    // Đính kèm.
    public static AttachmentListSort ParseAttachmentSort(string? sort, string? sortDir)
    { // Mở khối ParseAttachmentSort.
        var desc = ParseDescending(sortDir);
        if (string.IsNullOrWhiteSpace(sort))
            return new AttachmentListSort(AttachmentSortColumn.CreatedAt, desc);
        var c = sort.Trim();
        var col = c.Equals("id", StringComparison.OrdinalIgnoreCase) ? AttachmentSortColumn.Id
            : c.Equals("scope", StringComparison.OrdinalIgnoreCase) ? AttachmentSortColumn.Scope
            : c.Equals("userId", StringComparison.OrdinalIgnoreCase) ? AttachmentSortColumn.UserId
            : c.Equals("feedbackId", StringComparison.OrdinalIgnoreCase) ? AttachmentSortColumn.FeedbackId
            : c.Equals("postId", StringComparison.OrdinalIgnoreCase) ? AttachmentSortColumn.PostId
            : c.Equals("originalFileName", StringComparison.OrdinalIgnoreCase) ? AttachmentSortColumn.OriginalFileName
            : c.Equals("createdAt", StringComparison.OrdinalIgnoreCase) ? AttachmentSortColumn.CreatedAt
            : throw InvalidSort("Id, Scope, UserId, FeedbackId, PostId, OriginalFileName, CreatedAt");
        return new AttachmentListSort(col, desc);
    } // Kết thúc ParseAttachmentSort.

    // Refresh token (bảo mật / admin).
    public static RefreshTokenListSort ParseRefreshTokenSort(string? sort, string? sortDir)
    { // Mở khối ParseRefreshTokenSort.
        var desc = ParseDescending(sortDir);
        if (string.IsNullOrWhiteSpace(sort))
            return new RefreshTokenListSort(RefreshTokenSortColumn.CreatedAt, desc);
        var c = sort.Trim();
        var col = c.Equals("id", StringComparison.OrdinalIgnoreCase) ? RefreshTokenSortColumn.Id
            : c.Equals("userId", StringComparison.OrdinalIgnoreCase) ? RefreshTokenSortColumn.UserId
            : c.Equals("expiresAt", StringComparison.OrdinalIgnoreCase) ? RefreshTokenSortColumn.ExpiresAt
            : c.Equals("isRevoked", StringComparison.OrdinalIgnoreCase) ? RefreshTokenSortColumn.IsRevoked
            : c.Equals("createdAt", StringComparison.OrdinalIgnoreCase) ? RefreshTokenSortColumn.CreatedAt
            : throw InvalidSort("Id, UserId, ExpiresAt, IsRevoked, CreatedAt");
        return new RefreshTokenListSort(col, desc);
    } // Kết thúc ParseRefreshTokenSort.

    // User Identity.
    public static UserListSort ParseUserSort(string? sort, string? sortDir)
    { // Mở khối ParseUserSort.
        var desc = ParseDescending(sortDir);
        if (string.IsNullOrWhiteSpace(sort))
            return new UserListSort(UserSortColumn.CreatedAt, desc);
        var c = sort.Trim();
        var col = c.Equals("id", StringComparison.OrdinalIgnoreCase) ? UserSortColumn.Id
            : c.Equals("userName", StringComparison.OrdinalIgnoreCase) ? UserSortColumn.UserName
            : c.Equals("email", StringComparison.OrdinalIgnoreCase) ? UserSortColumn.Email
            : c.Equals("fullName", StringComparison.OrdinalIgnoreCase) || c.Equals("name", StringComparison.OrdinalIgnoreCase) ? UserSortColumn.FullName
            : c.Equals("isActive", StringComparison.OrdinalIgnoreCase) ? UserSortColumn.IsActive
            : c.Equals("createdAt", StringComparison.OrdinalIgnoreCase) ? UserSortColumn.CreatedAt
            : throw InvalidSort("Id, UserName, Email, FullName, IsActive, CreatedAt");
        return new UserListSort(col, desc);
    } // Kết thúc ParseUserSort.

    // Role: mặc định sort theo Name.
    public static RoleListSort ParseRoleSort(string? sort, string? sortDir)
    { // Mở khối ParseRoleSort.
        var desc = ParseDescending(sortDir);
        if (string.IsNullOrWhiteSpace(sort))
            return new RoleListSort(RoleSortColumn.Name, desc);
        var c = sort.Trim();
        var col = c.Equals("id", StringComparison.OrdinalIgnoreCase) ? RoleSortColumn.Id
            : c.Equals("name", StringComparison.OrdinalIgnoreCase) ? RoleSortColumn.Name
            : c.Equals("description", StringComparison.OrdinalIgnoreCase) ? RoleSortColumn.Description
            : throw InvalidSort("Id, Name, Description");
        return new RoleListSort(col, desc);
    } // Kết thúc ParseRoleSort.
} // Kết thúc ListSortParsing.

// Cột ORDER BY cho danh sách căn hộ (giá trị enum = segment khóa cache).
public enum ApartmentSortColumn
{
    CreatedAt = 0,
    Id = 1,
    Floor = 2,
    RoomNumber = 3,
    Area = 4,
    Status = 5,
    MaxResidents = 6,
}

// Struct nhỏ gọn: cột + hướng — dùng trong repository và EntityCacheKeys.*
public readonly record struct ApartmentListSort(ApartmentSortColumn Column, bool Descending)
{
    public int CacheSegment => (int)Column; // Phân đoạn sort trong khóa cache phân trang.
}

public enum ResidentSortColumn
{
    CreatedAt = 0,
    Id = 1,
    FullName = 2,
    IdentityNumber = 3,
    ApartmentId = 4,
}

public readonly record struct ResidentListSort(ResidentSortColumn Column, bool Descending)
{
    public int CacheSegment => (int)Column;
}

public enum UtilitySortColumn
{
    CreatedAt = 0,
    Id = 1,
    Name = 2,
    Price = 3,
    Unit = 4,
    IsActive = 5,
}

public readonly record struct UtilityListSort(UtilitySortColumn Column, bool Descending)
{
    public int CacheSegment => (int)Column;
}

public enum InvoiceSortColumn
{
    CreatedAt = 0,
    Id = 1,
    InvoiceCode = 2,
    Year = 3,
    Month = 4,
    TotalAmount = 5,
    Status = 6,
    ApartmentId = 7,
}

public readonly record struct InvoiceListSort(InvoiceSortColumn Column, bool Descending)
{
    public int CacheSegment => (int)Column;
}

public enum InvoiceItemSortColumn
{
    CreatedAt = 0,
    Id = 1,
    InvoiceId = 2,
    ServiceId = 3,
    SubTotal = 4,
}

public readonly record struct InvoiceItemListSort(InvoiceItemSortColumn Column, bool Descending)
{
    public int CacheSegment => (int)Column;
}

public enum FeedbackSortColumn
{
    CreatedAt = 0,
    Id = 1,
    UserId = 2,
    IsPinned = 3,
    IsResolved = 4,
    ParentId = 5,
}

public readonly record struct FeedbackListSort(FeedbackSortColumn Column, bool Descending)
{
    public int CacheSegment => (int)Column;
}

public enum PostSortColumn
{
    CreatedAt = 0,
    Id = 1,
    UserId = 2,
    ApartmentId = 3,
    Title = 4,
    IsPublished = 5,
}

public readonly record struct PostListSort(PostSortColumn Column, bool Descending)
{
    public int CacheSegment => (int)Column;
}

public enum AttachmentSortColumn
{
    CreatedAt = 0,
    Id = 1,
    Scope = 2,
    UserId = 3,
    FeedbackId = 4,
    PostId = 5,
    OriginalFileName = 6,
}

public readonly record struct AttachmentListSort(AttachmentSortColumn Column, bool Descending)
{
    public int CacheSegment => (int)Column;
}

public enum RefreshTokenSortColumn
{
    CreatedAt = 0,
    Id = 1,
    UserId = 2,
    ExpiresAt = 3,
    IsRevoked = 4,
}

public readonly record struct RefreshTokenListSort(RefreshTokenSortColumn Column, bool Descending)
{
    public int CacheSegment => (int)Column;
}

public enum UserSortColumn
{
    CreatedAt = 0,
    Id = 1,
    UserName = 2,
    Email = 3,
    FullName = 4,
    IsActive = 5,
}

public readonly record struct UserListSort(UserSortColumn Column, bool Descending)
{
    public int CacheSegment => (int)Column;
}

public enum RoleSortColumn
{
    Name = 0,
    Id = 1,
    Description = 2,
}

public readonly record struct RoleListSort(RoleSortColumn Column, bool Descending)
{
    public int CacheSegment => (int)Column;
}
