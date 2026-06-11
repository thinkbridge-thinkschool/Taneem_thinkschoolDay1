// After: HybridCache on — cache miss only on first request, rest served from L1/L2
// Target: /api/quotes (HybridCache, 30s TTL)
import http from 'k6/http';
import { check } from 'k6';

export const options = {
  vus: 20,
  duration: '15s',
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(99)'],
};

export default function () {
  const res = http.get('http://localhost:5150/api/quotes?page=1&size=10');
  check(res, { 'status 200': r => r.status === 200 });
}
