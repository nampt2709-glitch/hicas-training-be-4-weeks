using AutoMapper; // Profile, CreateMap, ForMember — đăng ký ánh xạ DTO ↔ entity.
using CommentAPI.DTOs; // UserDto, PostDto, CommentDto, body tạo/sửa, v.v.
using CommentAPI.Entities; // User, Post, Comment và navigation ảo (không map sâu mặc định).

namespace CommentAPI;

// =============================================================================
// File MappingProfile.cs: hồ sơ AutoMapper duy nhất — mọi CreateMap gọi trong constructor;
// một số ForMember Ignore/MapFrom phục vụ projection hoặc giữ UserId khi admin không gửi.
// =============================================================================

public class MappingProfile : Profile // Kế thừa Profile: định nghĩa tập ánh xạ khi ứng dụng chạy.
{
    // Hàm tạo: mỗi CreateMap tương ứng một cặp kiểu; thứ tự không phụ thuộc, chỉ thêm biểu mẫu tới bảng ánh xạ toàn cục.
    public MappingProfile() // Gọi một lần lúc AddAutoMapper, không inject phụ thuộc: chỉ cấu hình ánh xạ.
    {
        // BƯỚC 1 — Khai báo toàn bộ CreateMap User / Post / Comment (entity ↔ DTO) và chỉnh ForMember nơi cần.
        // --- User: entity Identity + bảng mở rộng; Roles trên UserDto do UserManager/ghép batch sau khi map.

        // Từ entity User (DB) ra DTO trả về client: trường tương ứng cùng tên.
        CreateMap<User, UserDto>();

        // Tạo user: Name/UserName/Email; Password băm ở UserManager, không qua map.
        CreateMap<CreateUserDto, User>();

        // Từ DTO cập nhật vào thực thể: chỉ trường ghi sẵn trong cấu hình (nếu có) hoặc gán thủ công ở service.
        CreateMap<UpdateUserDto, User>();

        // Dòng projection SQL/EF phẳng → DTO; Roles bổ sung sau (batch roles).
        CreateMap<UserPageRow, UserDto>()
            .ForMember(d => d.Roles, o => o.Ignore()); // Danh sách role không có trên hàng projection, gán ngoài AutoMapper.

        // --- Post: phản hồi CRUD + admin cập nhật tùy chọn chủ bài (UserId null = giữ chủ cũ khi map lên entity có sẵn).

        // Post domain sang DTO phản hồi, không tự ánh xạ navigation sâu trừ khi cấu hình thêm.
        CreateMap<Post, PostDto>();

        // Tạo bài: DTO tạo mới ánh xạ sang thực thể, Id/ CreatedAt thường gán ở service.
        CreateMap<CreatePostDto, Post>();

        // Cập nhật bài: cập nhật giới hạn từ DTO, có thì custom map sau.
        CreateMap<UpdatePostDto, Post>();

        // Admin: tiêu đề/nội dung + UserId tùy chọn.
        CreateMap<AdminUpdatePostDto, Post>()
            .ForMember(d => d.UserId, o => o.MapFrom((src, dest) => src.UserId ?? dest.UserId)); // Nullable: không gửi thì giữ UserId hiện tại của dest.

        // --- Comment: phẳng, cây, cập nhật admin; Level trên CommentFlatDto tính ở tầng service khi cần.

        // Comment entity sang DTO, giữ cấu trúc phẳng thường dùng ở API.
        CreateMap<Comment, CommentDto>();

        // GET /api/comments/flat: entity → CommentFlatDto; không có cột Level trong DB — route EF cố định Level = 0.
        CreateMap<Comment, CommentFlatDto>()
            .ForMember(d => d.Level, o => o.MapFrom(_ => 0)); // Route phẳng EF, không CTE.

        // Cây: Children map đệ quy khi navigation Children đã Include.
        CreateMap<Comment, CommentTreeDto>();

        // Tạo comment: chuyển từ body request sang entity, Id/CreatedAt do service bổ sung.
        CreateMap<CreateCommentDto, Comment>();

        // User sửa nội dung: DTO tối giản, map vào bản thể cần sửa.
        CreateMap<UpdateCommentDto, Comment>();

        // Admin: đủ PostId/ParentId/UserId/Content; nghiệp vụ cây vẫn xử lý ở service.
        CreateMap<AdminUpdateCommentDto, Comment>();
    } // Kết thúc constructor MappingProfile.
} // Kết thúc lớp MappingProfile.
