namespace CommentAPI.DTOs;

// Tạo comment: nội dung, bài, tác giả, cha; Admin route gắn User từ DTO nghiệp vụ.
public class CreateCommentDto
{
    public string Content { get; set; } = string.Empty; // Nội dung bình luận, mặc định rỗng; validator bắt rỗng/độ dài.
    public Guid PostId { get; set; } // Bài thuộc về, phải tồn tại, cùng với parent khi có cha.
    public Guid UserId { get; set; } // Tác giả, phải tồn tại, kiểm tra ở service.
    public Guid? ParentId { get; set; } // null = gốc cây; có giá trị thì phải tồn tại cùng PostId.
}

// User thường: chỉ sửa nội dung, không sửa cây hay bài ở DTO này.
public class UpdateCommentDto
{
    public string Content { get; set; } = string.Empty; // Nội dung mới, mặc định rỗng, validator theo cấu hình.
}

// Admin: đủ cột; service kiểm tra chu trình, cùng post, đồng bộ subtree khi chuyển bài nếu cần.
public class AdminUpdateCommentDto
{
    public string Content { get; set; } = string.Empty; // Nội dung sau chỉnh, mặc định rỗng.
    public Guid PostId { get; set; } // Bài mục tiêu; toàn tập cây con cập nhật Post khi lệch bài ở service.
    public Guid? ParentId { get; set; } // null = gắn gốc; không null = cha phải cùng PostId và không tạo vòng.
    public Guid UserId { get; set; } // Gán tác giả, user phải tồn tại ở bảng Users.
}

// Bản ghi phẳng response: mọi trường cần cho list/search chi tiết một comment.
public class CommentDto
{
    public Guid Id { get; set; } // Id comment, dùng update/delete/ cache key.
    public string Content { get; set; } = string.Empty; // Nội dung, mặc định rỗng từ map.
    public DateTime CreatedAt { get; set; } // Mốc tạo, sắp xếp theo thời gian.
    public Guid PostId { get; set; } // Bài, lọc theo post.
    public Guid UserId { get; set; } // Tác giả, hiển thị/ kiểm tra quyền.
    public Guid? ParentId { get; set; } // null = gốc, có giá trị = id cha trong cây cùng bài.
}

// Một dòng phẳng thuộc cây: có cấp Level sau khi tính độ sâu, có UserId để phân tích tác giả từng dòng.
public class CommentFlatDto
{
    public Guid Id { get; set; } // Id dòng, duy nhất toàn bảng.
    public string Content { get; set; } = string.Empty; // Nội dung, mặc định rỗng.
    public DateTime CreatedAt { get; set; } // Thời tạo, sắp theo từng route.
    public Guid PostId { get; set; } // Bài, nhóm cây; filter theo bài ở query.
    public Guid UserId { get; set; } // Tác giả bản ghi, biết ai viết khi so khớp với tài khoản hiện tại.
    public Guid? ParentId { get; set; } // Liên kết tới id cha, null = gốc cây trong post này.
    public int Level { get; set; } // Độ sâu: 0 gốc, tăng theo từng tầng cha; dùng hiển thị thụt lề/ phân tầng.
}

// Cấu trúc cây lồng: mỗi nút có danh sách con kiểu đệ quy cùng DTO, có UserId mỗi nút.
public class CommentTreeDto
{
    public Guid Id { get; set; } // Id nút, duy nhất toàn bảng.
    public string Content { get; set; } = string.Empty; // Nội dung, mặc định rỗng.
    public DateTime CreatedAt { get; set; } // Thời tạo, sắp thứ tự an toàn ở service.
    public Guid PostId { get; set; } // Bài, dù cây; giữ tính toàn vẹn cùng bài ở nghiệp vụ.
    public Guid UserId { get; set; } // Tác giả tại nút, hiển thị ai gửi ở mọi tầng cây.
    public Guid? ParentId { get; set; } // Gắn cha khi cần phẳng hóa; cây lồng còn dùng Children.
    public List<CommentTreeDto> Children { get; set; } = new(); // Con trực tiếp, cấu trúc đệ quy; mặc đã khởi tạo rỗng.
}
