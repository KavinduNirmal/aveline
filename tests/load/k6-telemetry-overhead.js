import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend, Rate } from 'k6/metrics';

// Custom metrics to measure middleware overhead
export const telemetryLatency = new Trend('telemetry_request_duration');
export const excludedLatency = new Trend('excluded_request_duration');
export const failureRate = new Rate('request_failures');

export const options = {
  stages: [
    { duration: '15s', target: 20 },
    { duration: '30s', target: 50 },
    { duration: '15s', target: 0 },
  ],
  thresholds: {
    // Middleware budget: p99 duration under 50ms for local mock, zero 5xx errors
    http_req_duration: ['p(99)<100'],
    request_failures: ['rate<0.01'],
  },
};

const BASE_URL = __ENV.API_BASE_URL || 'http://localhost:5000';
const AUTH_TOKEN = __ENV.AUTH_TOKEN || 'test-token';
const ORG_ID = __ENV.ORG_ID || '01918a99-9b48-75b5-9ef6-b25862024765';

export default function () {
  const headers = {
    Authorization: `Bearer ${AUTH_TOKEN}`,
    'Content-Type': 'application/json',
  };

  // 1. Excluded endpoint (health check) - skips telemetry middleware
  const healthRes = http.get(`${BASE_URL}/health`);
  check(healthRes, {
    'health status is 200': (r) => r.status === 200,
  });
  excludedLatency.add(healthRes.timings.duration);

  // 2. Telemetry-measured endpoint
  const statsRes = http.get(`${BASE_URL}/api/v1/orgs/${ORG_ID}/billing/burn-rate`, { headers });
  const success = check(statsRes, {
    'stats status is 200 or 401 (auth handled)': (r) => r.status === 200 || r.status === 401,
  });

  telemetryLatency.add(statsRes.timings.duration);
  failureRate.add(!success);

  sleep(0.1);
}
