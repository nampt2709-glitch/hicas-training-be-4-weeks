/*
    Mục đích:
        Chèn 10 bài viết (Posts) và 100 comment (Comments); m��i bài 10 comment.
        Cấu trúc: xâu chu��i cha-con (comment sau là con�c) để test flatten / cây.
    ��iều kiện:
        Bảng Users có ít nhất một user (lấy UserId đầu tiên theo UserName).
    Cách dùng:
        Dán script vào SSMS hoặc Azure Data Studio, chọn database, F5. Không gọi API backend.
*/

SET NOCOUNT ON;

DECLARE @UserId UNIQUEIDENTIFIER =
(
    SELECT TOP (1) u.Id FROM Users AS u ORDER BY u.UserName
);

IF @UserId IS NULL
BEGIN
    RAISERROR(N'No user in Users table. Create a user before running this seed script.', 16, 1);
    RETURN;
END;

DECLARE @PostMarker NVARCHAR(64) = N'SEED_10P100C_POST';
DECLARE @CommentMarker NVARCHAR(64) = N'SEED_10P100C_C';

DECLARE @Posts TABLE
(
    PostNum INT NOT NULL PRIMARY KEY,
    PostId UNIQUEIDENTIFIER NOT NULL
);

DECLARE @p INT = 1;
DECLARE @postId UNIQUEIDENTIFIER;
DECLARE @postNum INT;
DECLARE @parentId UNIQUEIDENTIFIER;
DECLARE @c INT;
DECLARE @commentId UNIQUEIDENTIFIER;

WHILE @p <= 10
BEGIN
    SET @postId = NEWID();

    INSERT INTO Posts (Id, Title, Content, CreatedAt, UserId)
    VALUES
    (@postId,
     N'Seed API Test Post ' + CAST(@p AS NVARCHAR(2)),
     @PostMarker + N' | Post #' + CAST(@p AS NVARCHAR(2)),
     DATEADD(MINUTE, @p, SYSUTCDATETIME()),
     @UserId);

    INSERT INTO @Posts (PostNum, PostId) VALUES (@p, @postId);

    SET @p += 1;
END;

SET @postNum = 1;

WHILE @postNum <= 10
BEGIN
    SELECT @postId = p.PostId FROM @Posts AS p WHERE p.PostNum = @postNum;

    SET @parentId = NULL;
    SET @c = 1;

    WHILE @c <= 10
    BEGIN
        SET @commentId = NEWID();

        INSERT INTO Comments (Id, Content, CreatedAt, PostId, UserId, ParentId)
        VALUES
        (@commentId,
         @CommentMarker + N' | p' + CAST(@postNum AS NVARCHAR(2)) + N' i' + CAST(@c AS NVARCHAR(2)),
         DATEADD(SECOND, @c + @postNum * 100, SYSUTCDATETIME()),
         @postId,
         @UserId,
         @parentId);

        SET @parentId = @commentId;
        SET @c += 1;
    END;

    SET @postNum += 1;
END;

PRINT N'Đã chèn 10 Posts và 100 Comments (seed test).';
