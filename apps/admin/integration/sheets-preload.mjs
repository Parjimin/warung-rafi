// Test-process-only transport redirect. Production never imports this module.
const original = globalThis.fetch;
globalThis.fetch = (input, init) => {
 const url = String(input), target = process.env.FIXTURE_GOOGLE_ORIGIN;
 if (target && (url.startsWith('https://sheets.googleapis.com/') || url === 'https://oauth2.googleapis.com/token')) {
  const parsed = new URL(url); return original(target + '/google/' + parsed.hostname + parsed.pathname + parsed.search, init);
 }
 return original(input, init);
};
