namespace CommentAPI.DTOs;

// Bản ghi bất biến: một dòng từ SELECT chiếu cột, không gồm cột bảo mật; init-only qua hàm tạo record.
public sealed record UserPageRow( // Từ khóa record gắn với primary constructor, sinh Equals/GetHashCode theo từng cột thành phần.

    Guid Id, // Khóa user, cột Id trên bảng Users, Guid duy nhất toàn bảng.

    string Name, // Tên hiển thị, không rỗng theo ràng buộc cấu hình entity khi tạo; đọc từ cột Name.

    string UserName, // Tên đăng nhập, map Identity UserName, dùng hiển thị danh sách/ chi tiết.

    string? Email, // Email tùy chọn, null nếu cột rỗng theo tài khoản.

    DateTime CreatedAt // Mốc tạo tài khoản, kiểu DateTime, thường lưu UTC ở lớp nghiệp vụ/ DB.
);
// Mỗi tham số tương ứng cột/ biểu thức trả về từ truy vấn phía repository, theo thứ tự khai báo ở trên.
