using System.Security.Cryptography; // SHA256, IncrementalHash.
using ApartmentAPI; // ApiException, ApiErrorCodes, ApiMessages.
using ApartmentAPI.Validators; // AttachmentBinarySignatures: kiểm tra magic bytes.
using Microsoft.AspNetCore.Hosting; // IWebHostEnvironment.ContentRootPath.
using Microsoft.AspNetCore.Http; // IFormFile.
using Microsoft.Extensions.Options; // IOptions<AttachmentStorageOptions>.

namespace ApartmentAPI.Services;

// Kết quả sau khi lưu file mới: tên hiển thị, tên trên đĩa, đường dẫn tương đối (để ghi DB), MIME, kích thước, hash.
public sealed record AttachmentStoredFileResult(
    string OriginalFileName,
    string StoredFileName,
    string RelativePath,
    string ContentType,
    long FileSize,
    string? Sha256Hex);

// Lưu/xóa file vật lý dưới ContentRoot; chặn path traversal và kiểm tra nhị phân đầu file.
public interface IAttachmentFileStorage
{
    Task<AttachmentStoredFileResult> SaveNewAsync(IFormFile file, CancellationToken ct = default);

    void TryDeleteRelativeToContentRoot(string? relativePathFromContentRoot);
}

public sealed class AttachmentFileStorage : IAttachmentFileStorage
{
    private readonly IWebHostEnvironment _env; // Gốc ứng dụng để ghép đường dẫn an toàn.
    private readonly AttachmentStorageOptions _opt; // Thư mục con lưu file (tương đối ContentRoot).

    public AttachmentFileStorage(IWebHostEnvironment env, IOptions<AttachmentStorageOptions> opt)
    { // Mở khối constructor.
        // BƯỚC 1 — Gắn môi trường host + cấu hình lưu trữ từ DI.
        _env = env; // ContentRootPath dùng làm tiền tố tuyệt đối.
        _opt = opt.Value; // RootRelativePath (ví dụ uploads) không chứa "..".
    } // Kết thúc constructor.

    public async Task<AttachmentStoredFileResult> SaveNewAsync(IFormFile file, CancellationToken ct = default)
    { // Mở khối SaveNewAsync — một lần đọc stream: kiểm tra header + copy + hash.
        // BƯỚC 1 — Chuẩn hoá tên gốc; tránh chuỗi rỗng gây extension lạ.
        var originalSafe = Path.GetFileName(file.FileName); // Chỉ lấy phần tên file, bỏ đường dẫn client.
        if (string.IsNullOrEmpty(originalSafe))
            originalSafe = "upload"; // Tên mặc định khi client không gửi FileName hợp lệ.

        // BƯỚC 2 — Tên lưu = Guid + extension gốc; tránh trùng và đoán được.
        var ext = Path.GetExtension(originalSafe);
        var storedFileName = $"{Guid.NewGuid():N}{ext}"; // N: 32 ký tự hex không gạch.

        // BƯỚC 3 — Đường dẫn tương đối: root cấu hình + tên file; chuẩn hoá slash.
        var rootSeg = _opt.RootRelativePath.Trim('/').Trim('\\').Replace('\\', '/');
        var relativePath = $"{rootSeg}/{storedFileName}".Replace('\\', '/');

        // BƯỚC 4 — Full path sau khi kiểm tra nằm dưới ContentRoot (GetSafeFullPath).
        var destFull = GetSafeFullPath(relativePath);

        // BƯỚC 5 — Mở stream nguồn một lần: đọc header → validate → ghi file + cập nhật hash incremental.
        await using (var src = file.OpenReadStream())
        { // Mở khối đọc/ghi stream.
            // BƯỚC 5a — Đọc tối đa 16 byte đầu để so khớp magic với extension (lớp phòng thủ bổ sung).
            var header = new byte[16];
            var headerLen = 0;
            while (headerLen < 16)
            {
                var n = await src.ReadAsync(header.AsMemory(headerLen, 16 - headerLen), ct).ConfigureAwait(false);
                if (n == 0)
                    break; // File rất ngắn hoặc rỗng.
                headerLen += n;
            }

            // TRƯỜNG HỢP A: Header/extension không khớp chữ ký nhị phân — từ chối 400.
            if (!AttachmentBinarySignatures.IsValidHeaderAndExtension(header.AsSpan(0, headerLen), file.FileName))
                throw ApiException.BadRequest(ApiErrorCodes.Validation, ApiMessages.AttachmentUploadBinaryInvalid);

            // BƯỚC 5b — Đảm bảo thư mục đích tồn tại trước khi tạo file mới.
            Directory.CreateDirectory(Path.GetDirectoryName(destFull)!);

            long size; // Kích thước sau ghi (bytes).
            string? shaHex; // SHA-256 lowercase hex.

            // BƯỚC 5c — FileMode.CreateNew: không ghi đè file trùng tên (trùng Guid gần như không xảy ra).
            await using (var dest = new FileStream(destFull, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { // Mở khối ghi file + băm.
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                // BƯỚC 5c-i — Ghi lại phần header đã đọc (nếu có) vào hash + file đích.
                if (headerLen > 0)
                {
                    sha.AppendData(header.AsSpan(0, headerLen));
                    await dest.WriteAsync(header.AsMemory(0, headerLen), ct).ConfigureAwait(false);
                }

                // BƯỚC 5c-ii — Copy phần còn lại của stream theo buffer; cập nhật hash song song.
                var buffer = new byte[81920];
                int read;
                while ((read = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                {
                    sha.AppendData(buffer.AsSpan(0, read));
                    await dest.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }

                size = dest.Length;
                var hashBytes = sha.GetHashAndReset();
                shaHex = Convert.ToHexString(hashBytes).ToLowerInvariant();
            } // Kết thúc ghi file + băm.

            // BƯỚC 6 — Content-Type: mặc định octet-stream nếu client không gửi.
            var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;

            // BƯỚC 7 — Trả record kết quả cho tầng service ghi entity Attachment.
            return new AttachmentStoredFileResult(
                originalSafe,
                storedFileName,
                relativePath,
                contentType,
                size,
                shaHex);
        } // Kết thúc khối stream nguồn.
    } // Kết thúc SaveNewAsync.

    public void TryDeleteRelativeToContentRoot(string? relativePathFromContentRoot)
    { // Mở khối TryDelete — không ném ngoại lệ ra ngoài (best-effort).
        // TRƯỜNG HỢP B: Đường dẫn null/empty — không làm gì.
        if (string.IsNullOrWhiteSpace(relativePathFromContentRoot))
            return;

        // BƯỚC 1 — Chuẩn hoá slash; từ chối ".." để tránh thoát khỏi ContentRoot.
        var normalized = relativePathFromContentRoot.Replace('\\', '/');
        if (normalized.Contains("..", StringComparison.Ordinal))
            return;

        try
        { // Mở khối try xóa file.
            var full = GetSafeFullPath(normalized);
            if (File.Exists(full))
                File.Delete(full);
        }
        catch
        {
            // Bỏ qua lỗi xóa (file đã mất hoặc quyền): không chặn soft-delete DB.
        }
    } // Kết thúc TryDeleteRelativeToContentRoot.

    private string GetSafeFullPath(string relativePathFromContentRoot)
    { // Mở khối GetSafeFullPath — đảm bảo full path là con của ContentRoot.
        // BƯỚC 1 — Combine + GetFullPath để loại bỏ "." ".." sau khi đã lọc chuỗi đầu vào.
        var combined = Path.Combine(_env.ContentRootPath, relativePathFromContentRoot.Replace('/', Path.DirectorySeparatorChar));
        var full = Path.GetFullPath(combined);
        var root = Path.GetFullPath(_env.ContentRootPath);
        // TRƯỜNG HỢP C: Đường dẫn resolved không bắt đầu bằng root — coi là path traversal.
        if (!full.StartsWith(root, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException(ApiMessages.AttachmentStoragePathOutsideRoot);
        return full;
    } // Kết thúc GetSafeFullPath.
}
