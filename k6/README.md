# k6 (10k request)

## Chạy nhanh

Trong thư mục `k6`:

```bash
k6 run comment-api.js
```

```bash
k6 run apartment-api.js
```

Port và thư mục kết quả **không cần `-e`**: script đọc `COMMENTAPI_PORT` / `APARTMENTAPI_PORT` và (tuỳ chọn) `K6_ARRIVAL_RATE` từ file **`.env` ở root solution** (`open()` từ `k6/lib/resolve-config.js`). **Bắt buộc** đặt working directory là thư mục `k6`. Kết quả ghi vào **`k6/Results/<API>/`** (đường dẫn tương đối `Results/<API>/`).

- Mặc định: arrival **40** request điều phối/giây, **~250s** (~4m10s) để bắt đầu đủ **10k** iteration; giảm tải đỉnh so với nhiều VU shared-iterations.
- Tinh chỉnh: bật hoặc sửa `K6_ARRIVAL_RATE` trong `.env` (ví dụ `30` nếu Docker yếu, `50` nếu máy khỏe). Thời lượng kịch bản = `ceil(10000 / rate)` giây.

## Ghi đè (tuỳ chọn)

| Biến k6 (`-e`) hoặc `.env` | Ý nghĩa |
|----------------------------|---------|
| `BASE_URL` | URL gốc API (bỏ qua port trong `.env`) |
| `RESULTS_ROOT` | Thư mục cha của `CommentAPI/` / `ApartmentAPI/` (mặc định `Results` = `k6/Results`) |
| `K6_ARRIVAL_RATE` | `.env` hoặc `-e` |
| `K6_USERNAME`, `K6_PASSWORD` | Mặc định BULKGEN + mật khẩu theo từng API |

## Docker / `docker-compose`

`RATE_LIMIT_PERMIT_MULTIPLIER` trong compose vẫn giúp tránh 429 khi rate cao.
