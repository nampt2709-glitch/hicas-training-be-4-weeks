/**
 * Tự resolve BASE_URL (port) và RESULTS_ROOT khi không truyền -e từ CLI.
 * Đọc file .env ở root solution (đường dẫn open() tính từ file này: k6/lib → lên 2 cấp).
 *
 * Mặc định RESULTS_ROOT = `Results` → thư mục `k6/Results` khi bạn chạy k6 với cwd là thư mục `k6`.
 * Chạy: đứng trong thư mục `k6`, lệnh `k6 run comment-api.js` (hoặc apartment-api.js).
 */

/** Đọc nội dung repo .env (null nếu không có). */
export function loadRepoDotEnvText() {
  try {
    return open('../../.env');
  } catch (e1) {
    try {
      return open('../.env');
    } catch (e2) {
      return null;
    }
  }
}

/** Một dòng KEY=value (bỏ qua CR/BOM nhẹ). */
export function parseEnvKey(text, key) {
  if (!text) return null;
  const re = new RegExp(`^${key}=(.*)$`, 'm');
  const m = text.match(re);
  if (!m) return null;
  let v = m[1].trim();
  if (v.startsWith('\ufeff')) v = v.slice(1);
  return v.replace(/\r$/, '');
}

export function resolveCommentApiConfig() {
  const text = loadRepoDotEnvText();
  const port = parseInt(parseEnvKey(text, 'COMMENTAPI_PORT') || '8080', 10);
  const base = (__ENV.BASE_URL || `http://localhost:${port}`).replace(/\/$/, '');
  const resultsRoot = (__ENV.RESULTS_ROOT || 'Results').replace(/\\/g, '/').replace(/\/$/, '');
  return { baseUrl: base, resultsRoot };
}

export function resolveApartmentApiConfig() {
  const text = loadRepoDotEnvText();
  const port = parseInt(parseEnvKey(text, 'APARTMENTAPI_PORT') || '8081', 10);
  const base = (__ENV.BASE_URL || `http://localhost:${port}`).replace(/\/$/, '');
  const resultsRoot = (__ENV.RESULTS_ROOT || 'Results').replace(/\\/g, '/').replace(/\/$/, '');
  return { baseUrl: base, resultsRoot };
}

/**
 * Tốc độ arrival (request điều phối / giây). Ưu tiên .env K6_ARRIVAL_RATE, rồi env k6, rồi default.
 * 35–40 thường an toàn trên Docker; tăng để nhanh hơn nếu máy khỏe.
 */
export function getArrivalRateFromEnv(defaultRate) {
  const text = loadRepoDotEnvText();
  const fromFile = parseEnvKey(text, 'K6_ARRIVAL_RATE');
  if (fromFile) {
    const n = parseInt(fromFile, 10);
    if (n > 0) return n;
  }
  const fromK6 = __ENV.K6_ARRIVAL_RATE;
  if (fromK6) {
    const n = parseInt(fromK6, 10);
    if (n > 0) return n;
  }
  return defaultRate;
}

/** Thời lượng kịch bản arrival-rate để đạt đúng ~totalIterations bắt đầu (làm tròn lên). */
export function durationForIterations(rate, totalIterations) {
  const sec = Math.max(1, Math.ceil(totalIterations / rate));
  return `${sec}s`;
}
