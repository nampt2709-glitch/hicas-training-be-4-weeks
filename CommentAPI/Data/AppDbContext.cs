using CommentAPI.Entities; 
using Microsoft.AspNetCore.Identity; 
using Microsoft.AspNetCore.Identity.EntityFrameworkCore; 
using Microsoft.EntityFrameworkCore; 

namespace CommentAPI.Data;

public class AppDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>
{

    public DbSet<Post> Posts => Set<Post>();

    public DbSet<Comment> Comments => Set<Comment>(); 


    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) 
    { 
    } 

    // Cấu hình model ánh xạ, gọi base để tạo bảng Identity, rồi tùy chỉnh từng thực thể nghiệp vụ.
    protected override void OnModelCreating(ModelBuilder modelBuilder) // ModelBuilder: fluent API, không cần attribute trên nhiều cột; migration phản ánh ánh xạ này.
    {
        base.OnModelCreating(modelBuilder); // Tạo schema Identity (AspNetUsers nếu mặc định) — ở đây bảng Users sau khi cấu hình User. ToTable, v.v.
        // Ghi chú: chuyển bảng user Identity sang tên Users, cột Name/CreatedAt nghiệp vụ, giữ khóa Guid trùng Identity.
        modelBuilder.Entity<User>(entity => // Cấu hình block cho loại thực thể User, biến entity là tác nhận trong khối ánh xạ.
        { // Mở khối cấu hình, mọi câu bên dưới tác dụng lên cùng bảng Users.
            entity.ToTable("Users"); // Tên bảng SQL: Users, thay tên bậc 2c mặc định AspNetUsers, giữ ánh xạ Identity cột chuẩn.
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired(); // Cột Name: tối đa 200 ký tự, NOT NULL, phù hợp tạo user/ hiển thị.
            entity.Property(x => x.CreatedAt).IsRequired(); // Cột mốc tạo, bắt buộc, không bỏ rỗng ở quy ước tầng ánh xạ.
        });

        modelBuilder.Entity<Post>(entity => // Bắt đầu ánh xạ bảng Post, một bài viết, khóa Guid.
        { // Mở khối, ràng buộc kiểu, độ dài, quan hệ tới user — không tự bật cascade theo ủy cầu nghiệp vụ ở dưới.
            entity.HasKey(x => x.Id); // Khai khóa chính chính thức, Id, không composite ở mô hình này.
            entity.Property(x => x.Title).HasMaxLength(300).IsRequired(); // Cột title, 300, NOT NULL, phù bài ngắn trên UI/ DB.
            entity.Property(x => x.Content).HasMaxLength(4000).IsRequired(); // Cột nội dung, 4000, bắt buộc, giới hạn tương ứng migration cũ.
            entity.Property(x => x.CreatedAt).IsRequired(); // Mốc tạo bài, bắt buộc, sort theo thời gian khi cần.

            entity.HasOne(x => x.User) // Một bài hướng tới user chủ, HasOne, navigation User trên thực thể Post, có Id UserId ở bảng.
                .WithMany(x => x.Posts) // Phía nhiều: user có nhiều bài, WithMany, collection Posts trên User.
                .HasForeignKey(x => x.UserId) // Cột ngoại UserId, sinh ở bảng Post, bắt liên kết tới bảng Users/ Identity.
                .OnDelete(DeleteBehavior.NoAction); // Xóa user không tự hủy bài ở DB (NoAction) — cần xử nghiệp vụ/ trigger nếu đổi.
        }); // Hết ánh xạ Post, tạo khóa ngoại với tên tự sinh, không cascade delete.

        modelBuilder.Entity<Comment>(entity => // Cấu hình comment: tự cõi, tới bài, tới user, giới hạn cột, index.
        { // Mở khối, nhiều mối quan hệ 1–n, tự 1 — n.
            entity.HasKey(x => x.Id); // Khóa chính, Guid, từng bình luận.
            entity.Property(x => x.Content).HasMaxLength(4000).IsRequired(); // Cột nội dung, 4000, bắt buộc, giống hạn ở migration gốc.
            entity.Property(x => x.CreatedAt).IsRequired(); // Mốc tạo, cần cho sắp cây/ phẳng thứ tự ổn định theo môi trường.

            entity.HasOne(x => x.User) // Tới tác giả, HasOne, navigation User.
                .WithMany(x => x.Comments) // User có tập bình luận, với tên thuộc tính Comments ở entity User, collection.
                .HasForeignKey(x => x.UserId) // Cột ngoại UserId trên bảng comment.
                .OnDelete(DeleteBehavior.NoAction); // Xóa user không xóa hàng hàng loạt comment: NoAction, tránh cắt dữ liệu im lặng.

            entity.HasOne(x => x.Post) // Tới bài, HasOne, navigation Post, — mỗi comment nằm đúng một bài bắt bởi PostId ở dưới cấu hình.
                .WithMany(x => x.Comments) // Một bài tới tập bình luận, tên tập Comments trên Post.
                .HasForeignKey(x => x.PostId) // Cột ngoại PostId nằm trên bảng comment.
                .OnDelete(DeleteBehavior.NoAction); // Tránh khi xóa bài thì comment chết ủy: NoAction, phải hợp lý nghiệp vụ khi cần cascade khác nơi.

            entity.HasOne(x => x.Parent) // Tự 1 — n, cha là cùng bảng Comment, cột ParentId nullable, — HasOne, navigation Parent, optional.
                .WithMany(x => x.Children) // Tập con tên Children, ở entity, — WithMany, — collection con.
                .HasForeignKey(x => x.ParentId) // Cột ngoại ParentId tự bảng, tree adjacency, — có ràng buộc trigger ngoài migration nếu cần.
                .OnDelete(DeleteBehavior.Restrict); // Xóa cha nếu còn con: bị cấm bởi Restrict, phù hợp chặn cắt gốc cây; xóa cây nghiệp vụ/ soft delete nơi service.

            entity.HasIndex(x => x.PostId); // Index, lọc theo bài, vấn theo từng bài, phân trang ổn định, — SQL tối ưu filter post.
            entity.HasIndex(x => x.ParentId); // Index, tìm con,— liên hệ leo cây/ join cha nhanh hơn quét toàn bảng.
            entity.HasIndex(x => x.UserId); // Index, tìm tất cả comment theo tác giả, — phục vụ sửa/ xóa theo user, admin.
        }); // Hết ánh xạ comment, tạo index có tên, migration phản ánh, — không tự sinh cột Level (tính ở tầng ứng dụng, không bảng).
    } 
    // Kết hàm tạo model, migration sinh/ cập nhật DB khi bạn cần add migration.
} // Kết lớp AppDbContext, dùng AddDbContext, scope, pool tùy cấu hình.
