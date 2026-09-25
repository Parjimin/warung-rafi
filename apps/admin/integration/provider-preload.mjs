// Loaded only by the integration test child process, never imported by the app.
// Canonical sandbox requests are intercepted here; production has no alternate-provider setting.
const originalFetch = globalThis.fetch;
const fixtureOrigin = process.env.FIXTURE_PROVIDER_ORIGIN;
if (!/^http:\/\/127\.0\.0\.1:\d+$/.test(fixtureOrigin ?? "")) throw new Error("Invalid local fixture origin");
globalThis.fetch = (input, init) => {
  const url = new URL(input instanceof Request ? input.url : input);
  if (url.hostname === "api.midtrans.com") throw new Error("Production payment request forbidden in fixture");
  if (url.origin === "https://api.sandbox.midtrans.com")
    return originalFetch(fixtureOrigin + "/midtrans" + url.pathname, init);
  return originalFetch(input, init);
};
