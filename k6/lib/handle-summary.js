/**
 * k6 end-of-test summary: prints throughput & latency to stdout (terminal) and writes
 * timestamped Result files under RESULTS_ROOT/<subdir>/ for assignment reporting.
 */
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.0.1/index.js';

function pickMetric(data, name) {
  return data.metrics && data.metrics[name] ? data.metrics[name] : null;
}

function metricRate(m) {
  if (!m || !m.values || m.values.rate == null) return null;
  return m.values.rate;
}

function metricP95Ms(m) {
  if (!m || !m.values || m.values['p(95)'] == null) return null;
  return m.values['p(95)'];
}

/**
 * @param {string} resultsSubdir e.g. "CommentAPI" or "ApartmentAPI"
 * @param {string} apiLabel short name for the JSON meta block
 */
export function buildHandleSummary(resultsSubdir, apiLabel, resultsRootOverride) {
  return function handleSummary(data) {
    const now = new Date();
    const stamp = now
      .toISOString()
      .replace(/:/g, '-')
      .replace(/\./g, '-')
      .replace('T', '_')
      .slice(0, 19);

    const root = (resultsRootOverride || __ENV.RESULTS_ROOT || 'Results')
      .replace(/\\/g, '/')
      .replace(/\/$/, '');
    const dir = `${root}/${resultsSubdir}`;
    const jsonPath = `${dir}/Result_${stamp}.json`;
    const txtPath = `${dir}/Result_${stamp}.txt`;

    const durationMs =
      data.state && data.state.testRunDurationMs != null ? Math.round(data.state.testRunDurationMs) : null;

    const httpReqs = pickMetric(data, 'http_reqs');
    const httpReqDuration = pickMetric(data, 'http_req_duration');
    const httpReqFailed = pickMetric(data, 'http_req_failed');
    const iterations = pickMetric(data, 'iterations');

    const throughput = metricRate(httpReqs);
    const latP95 = metricP95Ms(httpReqDuration);
    const latAvg =
      httpReqDuration && httpReqDuration.values && httpReqDuration.values.avg != null
        ? httpReqDuration.values.avg
        : null;

    const customFooter = [
      '',
      '--- Assignment summary (throughput & latency) ---',
      `API: ${apiLabel}`,
      `Wall clock completed at (ISO): ${now.toISOString()}`,
      `Test run duration (ms): ${durationMs != null ? durationMs : 'n/a'}`,
      `Throughput http_reqs (requests/sec): ${throughput != null ? throughput.toFixed(4) : 'n/a'}`,
      `Latency http_req_duration avg (ms): ${latAvg != null ? latAvg.toFixed(2) : 'n/a'}`,
      `Latency http_req_duration p(95) (ms): ${latP95 != null ? latP95.toFixed(2) : 'n/a'}`,
      '',
    ].join('\n');

    const summaryPlain = textSummary(data, { indent: ' ', enableColors: false }) + customFooter;
    const summaryColor = textSummary(data, { indent: ' ', enableColors: true }) + '\n' + customFooter;

    const payload = {
      meta: {
        api: apiLabel,
        resultsSubdir,
        wallClockCompletedAtIso: now.toISOString(),
        testRunDurationMs: durationMs,
        throughputHttpReqsPerSecond: throughput,
        latencyMs: {
          http_req_duration_avg: latAvg,
          http_req_duration_p95: latP95,
        },
      },
      metrics: {
        http_reqs: httpReqs,
        http_req_duration: httpReqDuration,
        http_req_failed: httpReqFailed,
        iterations,
      },
    };

    return {
      stdout: summaryColor,
      [jsonPath]: JSON.stringify(payload, null, 2),
      [txtPath]: summaryPlain,
    };
  };
}
