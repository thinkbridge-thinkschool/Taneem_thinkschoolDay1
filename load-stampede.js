// Stampede test: 50 VUs all fire at time 0 on a cold cache.
// Stampede protection means only 1 DB query fires — the rest wait and reuse that result.
import http from 'k6/http';
import { check } from 'k6';

export const options = {
  vus: 50,
  iterations: 50, // exactly 50 requests, all at once
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(99)'],
};

export default function () {
  const res = http.get('http://localhost:5150/api/quotes?page=1&size=10');
  check(res, { 'status 200': r => r.status === 200 });
}
