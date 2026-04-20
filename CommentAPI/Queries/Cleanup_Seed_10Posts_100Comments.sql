/*
    Mục đích:
        Xóa dữ liệu do Seed_10Posts_100Comments.sql tạo ra.
    Cách nhận diện:
        Posts: Content b��t đầu b��ng SEED_10P100C_POST
        Comments: Content b��t đầu b��ng SEED_10P100C_C (hoặc thuộc post seed).
    Th�� tự:
        Xóa Comments trư��c, sau đó Posts (khóa ngoại).
    Cách dùng:
        Dán vào SSMS hoặc Azure Data Studio, chọn database, F5.
*/

SET NOCOUNT ON;

BEGIN TRANSACTION;

DELETE c
FROM Comments AS c
INNER JOIN Posts AS p ON p.Id = c.PostId
WHERE p.Content LIKE N'SEED_10P100C_POST%';

DELETE FROM Comments
WHERE Content LIKE N'SEED_10P100C_C%';

DELETE FROM Posts
WHERE Content LIKE N'SEED_10P100C_POST%';

COMMIT TRANSACTION;

PRINT N'Đã xóa dữ liệu seed SEED_10P100C (posts và comments).';
