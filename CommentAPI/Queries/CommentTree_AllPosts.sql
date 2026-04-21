-- CTE đệ quy: toàn bộ comment mọi post (anchor = mọi gốc; đệ quy khớp ParentId và cùng PostId).
WITH CommentTree AS (
    SELECT
        c.Id,
        c.Content,
        c.CreatedAt,
        c.ParentId,
        c.PostId,
        0 AS Level
    FROM Comments c
    WHERE c.ParentId IS NULL

    UNION ALL

    SELECT
        c.Id,
        c.Content,
        c.CreatedAt,
        c.ParentId,
        c.PostId,
        ct.Level + 1
    FROM Comments c
    INNER JOIN CommentTree ct
        ON c.ParentId = ct.Id
       AND c.PostId = ct.PostId
)
SELECT
    Id,
    Content,
    CreatedAt,
    ParentId,
    PostId,
    Level
FROM CommentTree
ORDER BY PostId, Level, CreatedAt, Id
OPTION (MAXRECURSION 256);
