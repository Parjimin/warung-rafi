// Local test process only. Never imported by app/ or exposed by a production route.
import { createServer, type Server } from "node:http";
import { spawn } from "node:child_process";
import { setTimeout as delay } from "node:timers/promises";
import type { Product } from "../lib/catalog.ts";
const listen = (server: Server) => new Promise<number>(resolve => server.listen(0, "127.0.0.1", () => resolve((server.address() as { port: number }).port)));
export async function startHarness() {
  const seed: Product[] = [
    { id: "nasi", name: "Nasi + Bandeng", category: "Nasi", price: 12000, available: true, imageUrl: null },
    { id: "telur", name: "Nasi + Telur", category: "Nasi", price: 9000, available: true, imageUrl: null },
    { id: "tahu", name: "Tahu Goreng", category: "Lauk", price: 1500, available: true, imageUrl: null },
    { id: "teh", name: "Es Teh", category: "Minuman", price: 3000, available: true, imageUrl: null }
  ];
  const state = { draft_version: 1, published_version: 1, draft: seed, published: seed };
  const control = { storageFail: false, dbFail: false, conflict: false, uploaded: Buffer.alloc(0), devices: [] as unknown[] };
  const backend = createServer(async (req, res) => {
    const chunks: Buffer[] = []; for await (const chunk of req) chunks.push(Buffer.from(chunk));
    const bytes = Buffer.concat(chunks); const path = req.url ?? "";
    const reply = (value: unknown, status = 200) => { res.writeHead(status, { "Content-Type": "application/json" }); res.end(JSON.stringify(value)); };
    if (path.startsWith("/auth/v1/token")) { const input = JSON.parse(bytes.toString()); reply({ access_token: input.email === "owner@fixture.test" ? "fixture-admin" : "fixture-other", expires_in: 3600 }); return; }
    if (path === "/auth/v1/user") { reply({ id: req.headers.authorization === "Bearer fixture-admin" ? "fixture-admin-id" : "someone-else" }); return; }
    if (path.startsWith("/storage/v1/object/public/")) { res.writeHead(200, { "Content-Type": "image/jpeg" }); res.end(control.uploaded); return; }
    if (path.startsWith("/storage/v1/object/")) { control.uploaded = bytes; reply({}, control.storageFail ? 503 : 200); return; }
    if (control.dbFail) { reply({ error: "fixture database unavailable" }, 503); return; }
    if (path.startsWith("/rest/v1/catalog_state")) { reply([state]); return; }
    if (path.startsWith("/rest/v1/device_sync_status")) { reply(control.devices); return; }
    if (path.startsWith("/rest/v1/rpc/")) {
      const input = JSON.parse(bytes.toString());
      if (path.endsWith("save_catalog_draft")) {
        if (input.p_version !== state.draft_version) { reply({ code: "40001" }, 500); return; }
        state.draft = input.p_products; state.draft_version++; reply(null); return;
      }
      if (path.endsWith("publish_catalog")) {
        if (input.p_version !== state.draft_version) { reply({ code: "40001" }, 500); return; }
        state.published = state.draft; state.published_version++; reply(null); return;
      }
      if (path.endsWith("receive_device_batch")) { reply({ accepted: control.conflict ? [] : input.p_events.map((e: { id: string }) => e.id), conflict: control.conflict }); return; }
      if (path.endsWith("report_device_status")) { control.devices = [{ device_id: input.p_device, pending_count: input.p_pending, catalog_version: input.p_catalog, last_seen_at: new Date().toISOString(), last_report_at: new Date().toISOString(), last_sync_at: null, conflict_at: null, conflict_events: [] }]; reply(null); return; }
    }
    reply({ error: "Unknown fixture path" }, 404);
  });
  const backendPort = await listen(backend);
  const probe = createServer(); const port = await listen(probe); await new Promise<void>(resolve => probe.close(() => resolve()));
  const origin = `http://127.0.0.1:${port}`;
  const processNext = spawn(process.execPath, ["node_modules/next/dist/bin/next", "start", "--hostname", "127.0.0.1", "--port", String(port)], {
    env: { ...process.env, APP_ORIGIN: origin, SUPABASE_URL: `http://127.0.0.1:${backendPort}`, SUPABASE_ANON_KEY: "fixture-anon", SUPABASE_SERVICE_ROLE_KEY: "fixture-service", ADMIN_USER_ID: "fixture-admin-id", DEVICE_API_TOKEN: "fixture-device-token-at-least-32-characters", DEVICE_ID: "kasir-utama", NEXT_TELEMETRY_DISABLED: "1" }, stdio: ["ignore", "pipe", "pipe"]
  });
  let logs = ""; processNext.stdout.on("data", chunk => logs += chunk); processNext.stderr.on("data", chunk => logs += chunk);
  const stop = async () => { processNext.kill(); backend.closeAllConnections(); await new Promise<void>(resolve => backend.close(() => resolve())); };
  for (let i = 0; i < 100; i++) {
    try { if ((await fetch(origin + "/login")).ok) return { origin, state, control, stop }; } catch { /* startup only */ }
    if (processNext.exitCode !== null) { await stop(); throw new Error(logs); }
    await delay(200);
  }
  await stop(); throw new Error("Next test server did not start: " + logs);
}
