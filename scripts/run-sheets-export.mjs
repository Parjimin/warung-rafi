// Invoke from an external scheduler after deployment; process one durable job.
const origin = process.env.APP_ORIGIN, token = process.env.EXPORT_RUNNER_TOKEN;
if (!origin || !token || token.length < 32 || new URL(origin).protocol !== 'https:') throw new Error('Configure HTTPS APP_ORIGIN and EXPORT_RUNNER_TOKEN.');
const response = await fetch(new URL('/api/jobs/sheets', origin), { method: 'POST', headers: { Authorization: `Bearer ${token}` }, signal: AbortSignal.timeout(65000), redirect: 'error' });
if (!response.ok) throw new Error(`Export runner returned HTTP ${response.status}`);
const result = await response.json(); console.log(JSON.stringify({ processed: result.processed, verified: result.verified, code: result.code }));
