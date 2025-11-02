import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';

// Custom metrics
const errorRate = new Rate('errors');
const connectionTime = new Trend('connection_time');

// Test configuration
export const options = {
  stages: [
    { duration: '2m', target: 100 }, // Ramp up to 100 users over 2 minutes
    { duration: '5m', target: 100 }, // Stay at 100 users for 5 minutes
    { duration: '2m', target: 200 }, // Ramp up to 200 users over 2 minutes
    { duration: '5m', target: 200 }, // Stay at 200 users for 5 minutes
    { duration: '2m', target: 500 }, // Ramp up to 500 users over 2 minutes
    { duration: '5m', target: 500 }, // Stay at 500 users for 5 minutes
    { duration: '2m', target: 0 },   // Ramp down to 0 users over 2 minutes
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'], // 95% of requests should be below 500ms
    http_req_failed: ['rate<0.1'],    // Error rate should be below 10%
    errors: ['rate<0.1'],             // Custom error rate
  },
};

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';

// Sample Wi-Fi network data for testing
const testNetworks = [
  { ssid: 'TestNetwork1', password: 'password123' },
  { ssid: 'TestNetwork2', password: 'password456' },
  { ssid: 'TestNetwork3', password: 'password789' },
  { ssid: 'CorporateWiFi', password: 'corp2024!' },
  { ssid: 'GuestNetwork', password: 'guest123' },
];

export default function () {
  // Scenario 1: Network scanning (most common operation)
  const scanStart = new Date().getTime();
  const scanResponse = http.get(`${BASE_URL}/api/networks/scan`, {
    timeout: '30s',
  });

  const scanDuration = new Date().getTime() - scanStart;

  check(scanResponse, {
    'scan status is 200': (r) => r.status === 200,
    'scan response time < 10s': (r) => r.timings.duration < 10000,
    'scan returns networks array': (r) => {
      try {
        const data = JSON.parse(r.body);
        return Array.isArray(data.networks);
      } catch (e) {
        return false;
      }
    },
  });

  errorRate.add(scanResponse.status !== 200);

  // Scenario 2: Connection attempt (less frequent but important)
  if (Math.random() < 0.1) { // 10% of users try to connect
    const network = testNetworks[Math.floor(Math.random() * testNetworks.length)];
    const connectStart = new Date().getTime();

    const connectResponse = http.post(`${BASE_URL}/api/networks/connect`, JSON.stringify({
      ssid: network.ssid,
      password: network.password
    }), {
      headers: {
        'Content-Type': 'application/json',
      },
      timeout: '30s',
    });

    const connectDuration = new Date().getTime() - connectStart;
    connectionTime.add(connectDuration);

    check(connectResponse, {
      'connect status is valid': (r) => r.status === 200 || r.status === 400 || r.status === 401,
      'connect response time < 15s': (r) => r.timings.duration < 15000,
    });

    errorRate.add(connectResponse.status >= 500);
  }

  // Scenario 3: Status check
  const statusResponse = http.get(`${BASE_URL}/api/status`, {
    timeout: '10s',
  });

  check(statusResponse, {
    'status is 200': (r) => r.status === 200,
    'status response time < 2s': (r) => r.timings.duration < 2000,
    'status contains connection info': (r) => {
      try {
        const data = JSON.parse(r.body);
        return data.hasOwnProperty('connectionStatus');
      } catch (e) {
        return false;
      }
    },
  });

  errorRate.add(statusResponse.status !== 200);

  // Scenario 4: Health check
  const healthResponse = http.get(`${BASE_URL}/health`, {
    timeout: '5s',
  });

  check(healthResponse, {
    'health status is 200': (r) => r.status === 200,
    'health response time < 1s': (r) => r.timings.duration < 1000,
  });

  errorRate.add(healthResponse.status !== 200);

  // Random sleep between 1-5 seconds to simulate user behavior
  sleep(Math.random() * 4 + 1);
}

export function teardown(data) {
  console.log('Test completed. Final metrics:');
  console.log(`Total requests: ${data.metrics.http_reqs.values.count}`);
  console.log(`Average response time: ${data.metrics.http_req_duration.values.avg}ms`);
  console.log(`95th percentile: ${data.metrics.http_req_duration.values['p(95)']}ms`);
  console.log(`Error rate: ${data.metrics.http_req_failed.values.rate * 100}%`);
}
