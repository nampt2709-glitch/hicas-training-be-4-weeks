-- CTE đệ quy: cây comment cho một post (anchor = gốc; bước đệ quy khớp ParentId và cùng PostId).
WITH CommentTree AS (
    SELECT
        c.Id,
        c.Content,
        c.CreatedAt,
        c.ParentId,
        c.PostId,
        c.UserId,
        0 AS Level
    FROM Comments c
    WHERE c.PostId = @postId
      AND c.ParentId IS NULL

    UNION ALL

    SELECT
        c.Id,
        c.Content,
        c.CreatedAt,
        c.ParentId,
        c.PostId,
        c.UserId,
        ct.Level + 1
    FROM Comments c
    INNER JOIN CommentTree ct
        ON c.ParentId = ct.Id
       AND c.PostId = ct.PostId
    WHERE c.PostId = @postId
)
SELECT
    Id,
    Content,
    CreatedAt,
    ParentId,
    PostId,
    UserId,
    Level
FROM CommentTree
WHERE (@createdAtFrom IS NULL OR CreatedAt >= @createdAtFrom)
  AND (@createdAtTo IS NULL OR CreatedAt <= @createdAtTo)
ORDER BY Level, CreatedAt, Id
OPTION (MAXRECURSION 256);
