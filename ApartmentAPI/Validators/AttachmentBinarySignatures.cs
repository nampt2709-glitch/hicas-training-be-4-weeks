using Microsoft.AspNetCore.Http; // IFormFile OpenReadStream.

namespace ApartmentAPI.Validators;

// Kiểm tra upload bằng chữ ký nhị phân (magic bytes), không tin Content-Type hay tên file một mình.
public static class AttachmentBinarySignatures
{ // Mở khối AttachmentBinarySignatures.
    private enum DetectedKind
    { // Loại file suy từ header — map sang extension cho phép.
        Unknown,
        Jpeg,
        Png,
        Gif,
        Webp,
        Pdf,
    }

    // Đọc tối đa 16 byte đầu + so magic bytes — dùng .Must() trong FluentValidation auto-validation (pipeline đồng bộ).
    public static bool IsValidUpload(IFormFile file)
    { // Mở khối IsValidUpload.
        using var stream = file.OpenReadStream();
        var header = new byte[16];
        var total = 0;
        while (total < 16)
        { // Lặp cho đến đủ 16 byte hoặc EOF.
            var n = stream.Read(header, total, 16 - total);
            if (n == 0)
                break;
            total += n;
        }

        var kind = Detect(header.AsSpan(0, total));
        if (kind == DetectedKind.Unknown)
            return false;

        return ExtensionMatchesDetectedKind(Path.GetExtension(file.FileName), kind);
    } // Kết thúc IsValidUpload.

    // Overload async: cùng kết quả IsValidUpload — giữ cho caller await (không dùng trong rule .MustAsync của auto-validation).
    public static Task<bool> IsValidUploadAsync(IFormFile file, CancellationToken ct = default)
    { // Mở khối IsValidUploadAsync.
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(IsValidUpload(file));
    } // Kết thúc IsValidUploadAsync.

    // Lưu đĩa (defense in depth): kiểm tra header đã đọc + extension trước khi ghi bytes.
    public static bool IsValidHeaderAndExtension(ReadOnlySpan<byte> header, string? originalFileName)
    { // Mở khối IsValidHeaderAndExtension.
        var kind = Detect(header);
        if (kind == DetectedKind.Unknown)
            return false;
        return ExtensionMatchesDetectedKind(Path.GetExtension(originalFileName), kind);
    } // Kết thúc IsValidHeaderAndExtension.

    // So khớp magic bytes với từng định dạng ảnh/PDF hỗ trợ.
    private static DetectedKind Detect(ReadOnlySpan<byte> h)
    { // Mở khối Detect.
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF)
            return DetectedKind.Jpeg;

        if (h.Length >= 8
            && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47
            && h[4] == 0x0D && h[5] == 0x0A && h[6] == 0x1A && h[7] == 0x0A)
            return DetectedKind.Png;

        if (h.Length >= 6
            && h[0] == 0x47 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x38
            && (h[4] == 0x37 || h[4] == 0x39)
            && h[5] == 0x61)
            return DetectedKind.Gif;

        if (h.Length >= 12
            && h[0] == (byte)'R' && h[1] == (byte)'I' && h[2] == (byte)'F' && h[3] == (byte)'F'
            && h[8] == (byte)'W' && h[9] == (byte)'E' && h[10] == (byte)'B' && h[11] == (byte)'P')
            return DetectedKind.Webp;

        if (h.Length >= 4 && h[0] == 0x25 && h[1] == 0x50 && h[2] == 0x44 && h[3] == 0x46)
            return DetectedKind.Pdf;

        return DetectedKind.Unknown;
    } // Kết thúc Detect.

    // Whitelist extension theo DetectedKind — tránh .pdf giả là ảnh hoặc ngược lại.
    private static bool ExtensionMatchesDetectedKind(string? extension, DetectedKind kind)
    { // Mở khối ExtensionMatchesDetectedKind.
        var ext = extension ?? "";
        if (string.IsNullOrEmpty(ext))
            return false;

        return kind switch
        {
            DetectedKind.Jpeg => ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                                 || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase),
            DetectedKind.Png => ext.Equals(".png", StringComparison.OrdinalIgnoreCase),
            DetectedKind.Gif => ext.Equals(".gif", StringComparison.OrdinalIgnoreCase),
            DetectedKind.Webp => ext.Equals(".webp", StringComparison.OrdinalIgnoreCase),
            DetectedKind.Pdf => ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    } // Kết thúc ExtensionMatchesDetectedKind.
} // Kết thúc AttachmentBinarySignatures.
