-- =============================================================================
-- Tên: Comments_InsertUpdate_IntegrityTrigger.sql
-- Mục đích: Định nghĩa trigger AFTER INSERT, UPDATE lên bảng dbo.Comments để
--   chặn sửa tay trên SQL (hoặc ứng dụng bị lỗi) tạo ra:
--   - Tự tham chiếu: ParentId = Id (một vòng tại chính dòng)
--   - Vòng lặp cha - con: leo từ ParentId lên gốc, nếu gặp lại chính Id dòng
--     đang xét thì chu trình
--   - Cha và con phải cùng bài: PostId của comment phải trùng PostId của
--     bản ghi cha (khi ParentId IS NOT NULL)
--   - Tham chiếu “mồ côi” tới parent không tồn tại: khi ngoại khóa bị tắt hoặc
--     tạm thời NOCHECK (trigger vẫn bắt)
--   - Bài / user: PostId, UserId phải tồn tại ở dbo.Posts và dbo.Users
--   (phòng hờ khi ràng buộc FK bị tắt; nếu FK đang bật thì bước này thừa
--   nhưng vô hại)
--
-- Giới hạn: Trigger chỉ soi từng dòng nằm trong inserted (bản mới/đổi). Nó
--   không tự cập nhật toàn bộ tập cây con khi bạn sửa PostId thủ công tại một
--   bản (ứng dụng API thường cập nhật cả subtree). Trường hợp “con vẫn
--   trỏ cha nhưng PostId cả hai lệch batch” cần quy ước nghiệp vụ hoặc
--   job/SP bổ sung — không bao phủ hết ở đây.
--
-- Cách gắn: Chạy script này thủ công trên SQL Server (SSMS / sqlcmd) đúng
--   database. KHÔNG phải migration EF — giữ tách nếu bạn muốn vận hành/DBA
--   tự quyết khi bật trigger.
-- =============================================================================
SET NOCOUNT ON;
GO

CREATE OR ALTER TRIGGER dbo.trg_Comments_InsertUpdate_Integrity
ON dbo.Comments
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    /* Không có dòng thay đổi — thoát. */
    IF NOT EXISTS (SELECT 1 FROM inserted)
        RETURN;

    /* 1) Tự làm cha của chính mình: ParentId = Id. */
    IF EXISTS (
        SELECT 1
        FROM inserted AS i
        WHERE i.ParentId IS NOT NULL
          AND i.ParentId = i.Id
    )
    BEGIN
        THROW 50001,
            N'Comments: vi phạm — ParentId không được trùng với chính Id (vòng một cạnh).',
            1;
    END;

    /* 2) Post / User tồn tại (phòng hờ FK tắt). */
    IF EXISTS (
        SELECT 1
        FROM inserted AS i
        WHERE NOT EXISTS (SELECT 1 FROM dbo.Posts AS p WHERE p.Id = i.PostId)
    )
    BEGIN
        THROW 50002,
            N'Comments: vi phạm — PostId không tồn tại trong dbo.Posts.',
            1;
    END;

    IF EXISTS (
        SELECT 1
        FROM inserted AS i
        WHERE NOT EXISTS (SELECT 1 FROM dbo.Users AS u WHERE u.Id = i.UserId)
    )
    BEGIN
        THROW 50003,
            N'Comments: vi phạm — UserId không tồn tại trong dbo.Users.',
            1;
    END;

    /* 3) Có ParentId: cha phải tồn tại, cùng PostId (tránh mồ côi / nhảy bài). */
    IF EXISTS (
        SELECT 1
        FROM inserted AS i
        WHERE i.ParentId IS NOT NULL
          AND NOT EXISTS (
                SELECT 1
                FROM dbo.Comments AS p
                WHERE p.Id = i.ParentId
          )
    )
    BEGIN
        THROW 50004,
            N'Comments: vi phạm — ParentId trỏ tới comment không tồn tại (mồ côi/cha ảo).',
            1;
    END;

    IF EXISTS (
        SELECT 1
        FROM inserted AS i
        INNER JOIN dbo.Comments AS p ON p.Id = i.ParentId
        WHERE i.ParentId IS NOT NULL
          AND p.PostId <> i.PostId
    )
    BEGIN
        THROW 50005,
            N'Comments: vi phạm — PostId phải trùng với PostId của parent (cây không ghép ngang bài).',
            1;
    END;

    /* 4) Chu trinh: leo tu ParentId; neu bat gap Id cua dang xet = co vong. */
    IF EXISTS (
        WITH AncestorWalk
        AS ( /* Neo: R = dang can xet, Cur = buoc tren chuoi to tien. */
            SELECT i.Id         AS R,
                   i.ParentId  AS Cur,
                   0           AS D
            FROM inserted AS i
            WHERE i.ParentId IS NOT NULL
            UNION ALL
            SELECT w.R,
                   c.ParentId,
                   w.D + 1
            FROM AncestorWalk AS w
            INNER JOIN dbo.Comments AS c ON c.Id = w.Cur
            WHERE w.Cur IS NOT NULL
              AND w.D < 2000
        )
        SELECT 1
        FROM AncestorWalk AS a
        WHERE a.Cur = a.R
        OPTION (MAXRECURSION 2000)
    )
    BEGIN
        THROW 50006,
            N'Comments: vi phạm — quan hệ ParentId tạo chu trình từ tổ tiên về gốc (cycle).',
            1;
    END
END;
GO

-- Ghi chú vận hành (không tự thực thi khi mở file):
-- - Tắt tạm: DISABLE TRIGGER trg_Comments_InsertUpdate_Integrity ON dbo.Comments;
-- - Bật lại: ENABLE TRIGGER trg_Comments_InsertUpdate_Integrity ON dbo.Comments;
-- - BULK hoặc job đặc biệt: tắt trigger trong phiên, rồi validate lại dữ liệu
--   bằng truy vấn CTE rồi bật lại.
