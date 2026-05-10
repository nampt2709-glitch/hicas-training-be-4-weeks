/**
 * CommentAPI — 10k request, điều phối tốc độ (constant-arrival-rate) để nhanh hơn nhưng tránh bão concurrent.
 *
 * Chạy (từ thư mục `k6`):  k6 run comment-api.js
 *
 * Port: đọc COMMENTAPI_PORT trong `.env` gốc solution (mặc định 8080). Ghi đè: BASE_URL, RESULTS_ROOT, K6_ARRIVAL_RATE.
 * Kết quả: Results/CommentAPI/ trong thư mục `k6` (cwd phải là `k6`).
 */
import http from 'k6/http';
import { check } from 'k6';
import { buildHandleSummary } from './lib/handle-summary.js';
import { accessTokenFromLoginResponse } from './lib/auth-json.js';
import {
  resolveCommentApiConfig,
  getArrivalRateFromEnv,
  durationForIterations,
} from './lib/resolve-config.js';

const totalIterations = 10000;
const cfg = resolveCommentApiConfig();
const arrivalRate = getArrivalRateFromEnv(40);
const scenarioDuration = durationForIterations(arrivalRate, totalIterations);

const base = cfg.baseUrl;
const apiVersion = __ENV.API_VERSION || '1';

export const options = {
  setupTimeout: '120s',
  scenarios: {
    benchmark_10k: {
      executor: 'constant-arrival-rate',
      rate: arrivalRate,
      timeUnit: '1s',
      duration: scenarioDuration,
      preAllocatedVUs: Math.min(55, Math.max(20, Math.ceil(arrivalRate * 0.85))),
      maxVUs: Math.min(70, Math.max(30, Math.ceil(arrivalRate * 1.4))),
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.18'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)'],
  discardResponseBodies: true,
};

function login() {
  const user = __ENV.K6_USERNAME || 'BULKGEN_A_00001';
  const password = __ENV.K6_PASSWORD || 'CommentAPI@123';
  const res = http.post(
    `${base}/api/v${apiVersion}/auth/login`,
    JSON.stringify({ userName: user, password }),
    {
      headers: {
        'Content-Type': 'application/json',
        Accept: 'application/json',
        'Accept-Encoding': 'identity',
      },
      responseType: 'text',
      timeout: '60s',
    },
  );
  const ok = check(res, { 'login 200': (r) => r.status === 200 });
  if (!ok || res.status !== 200) {
    throw new Error(`Login failed ${res.status}: ${res.body ? String(res.body).substring(0, 240) : ''}`);
  }
  return accessTokenFromLoginResponse(res);
}

export function setup() {
  console.log(
    `[k6] CommentAPI base=${base} arrival=${arrivalRate}/s duration=${scenarioDuration} results=${cfg.resultsRoot}/CommentAPI`,
  );
  const token = login();
  console.log('[k6] Login OK. Starting arrival-rate run (~10k iter).');
  return { token };
}

export default function (data) {
  const params = {
    headers: {
      Authorization: `Bearer ${data.token}`,
    },
    tags: { name: 'comment-mixed' },
    timeout: '55s',
  };

  const v = apiVersion;
  const r = Math.random();
  let res;
  if (r < 0.32) {
    res = http.get(`${base}/api/v${v}/comments?page=1&pageSize=20`, params);
  } else if (r < 0.62) {
    res = http.get(`${base}/api/v${v}/posts?page=1&pageSize=20`, params);
  } else if (r < 0.88) {
    res = http.get(`${base}/api/v${v}/users?page=1&pageSize=20`, params);
  } else if (r < 0.97) {
    res = http.get(`${base}/api/v${v}/comments/flat?page=1&pageSize=15`, params);
  } else {
    res = http.get(`${base}/api/v${v}/posts?page=2&pageSize=10`, params);
  }

  check(res, { '2xx': (x) => x.status >= 200 && x.status < 300 });
}

export const handleSummary = buildHandleSummary('CommentAPI', 'CommentAPI', cfg.resultsRoot);
