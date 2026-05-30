import http from 'k6/http';

export let options = {
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(99)'],
};

export default function () {
  http.get('http://localhost:5150/api/quotes/summary/dapper?page=1&size=10');
}
