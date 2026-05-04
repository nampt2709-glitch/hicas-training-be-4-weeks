namespace CommentAPI;

// Thứ tự sắp cho route comment (query `sort` — dropdown client): dùng chung list phẳng EF, gốc phân trang, CTE, demo.
public enum CommentRouteListSort
{
    // Theo CreatedAt tăng dần, tie-break Id (dùng BFS / unpaged nội bộ).
    ByCreatedAt = 0,

    // PostId → CreatedAt → Id (mặc định list phẳng toàn hệ / theo bài; đồng bộ với hành vi cũ).
    ByPostCreatedAtId = 1,

    // CreatedAt giảm dần (mới nhất trước).
    ByCreatedAtDesc = 2,

    // UserId → CreatedAt → Id (nhóm theo tác giả).
    ByUserIdCreatedAtId = 3,

    // Id tăng dần (ổn định tuyệt đối).
    ByIdAsc = 4,
}
