/**
 * ApartmentAPI — 10k request, constant-arrival-rate (cùng chiến lược CommentAPI).
 *
 * Chạy (từ thư mục `k6`):  k6 run apartment-api.js
 *
 * Port: APARTMENTAPI_PORT trong `.env` (mặc định 8081). Ghi đè: BASE_URL, RESULTS_ROOT, K6_ARRIVAL_RATE.
 * Kết quả: Results/ApartmentAPI/ trong thư mục `k6` (cwd phải là `k6`).
 */
import http from 'k6/http';
import { check } from 'k6';
import { buildHandleSummary } from './lib/handle-summary.js';
import { accessTokenFromLoginResponse } from './lib/auth-json.js';
import {
  resolveApartmentApiConfig,
  getArrivalRateFromEnv,
  durationForIterations,
} from './lib/resolve-config.js';

const totalIterations = 10000;
const cfg = resolveApartmentApiConfig();
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
  const password = __ENV.K6_PASSWORD || 'ApartmentAPI@123';
  const res = http.post(
    `${base}/api/auth/login`,
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
    `[k6] ApartmentAPI base=${base} arrival=${arrivalRate}/s duration=${scenarioDuration} results=${cfg.resultsRoot}/ApartmentAPI`,
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
    tags: { name: 'apartment-mixed' },
    timeout: '55s',
  };

  const v = apiVersion;
  const p = (page, size) => `page=${page}&pageSize=${size}`;
  const r = Math.random();
  let res;
  if (r < 0.28) {
    res = http.get(`${base}/api/v${v}/apartments?${p(1, 20)}`, params);
  } else if (r < 0.46) {
    res = http.get(`${base}/api/v${v}/residents?${p(1, 20)}`, params);
  } else if (r < 0.62) {
    res = http.get(`${base}/api/v${v}/utility-services?${p(1, 20)}`, params);
  } else if (r < 0.74) {
    res = http.get(`${base}/api/v${v}/invoices?${p(1, 15)}`, params);
  } else if (r < 0.84) {
    res = http.get(`${base}/api/v${v}/feedbacks?${p(1, 15)}`, params);
  } else if (r < 0.92) {
    res = http.get(`${base}/api/v${v}/users?${p(1, 15)}`, params);
  } else if (r < 0.97) {
    res = http.get(`${base}/api/v${v}/roles?${p(1, 20)}`, params);
  } else {
    res = http.get(`${base}/api/v${v}/invoice-items?${p(1, 15)}`, params);
  }

  check(res, { '2xx': (x) => x.status >= 200 && x.status < 300 });
}

export const handleSummary = buildHandleSummary('ApartmentAPI', 'ApartmentAPI', cfg.resultsRoot);
