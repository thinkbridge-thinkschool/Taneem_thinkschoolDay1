// Before: no cache — every request hits the DB
// Target: /api/quotes/summary (CQRS handler, no HybridCache)
import http from 'k6/http';
import { check } from 'k6';

export const options = {
  vus: 20,
  duration: '15s',
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(99)'],
};

export default function () {
  const res = http.get('http://localhost:5150/api/quotes/summary?page=1&size=10');
  check(res, { 'status 200': r => r.status === 200 });
}
