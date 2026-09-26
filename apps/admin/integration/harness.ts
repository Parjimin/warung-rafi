// Local test process only. Never imported by app/ or exposed by a production route.
import { readFileSync } from "node:fs";
import { generateKeyPairSync } from "node:crypto";
import type { ReportJob } from "../lib/report-worker.ts";
import { createServer, type Server } from "node:http";
import { spawn } from "node:child_process";
import { setTimeout as delay } from "node:timers/promises";
import type { Product } from "../lib/catalog.ts";
import { financeFixture, applyFixture } from "./reconciliation-fixture.ts";
const listen = (server: Server) => new Promise<number>(resolve => server.listen(0, "127.0.0.1", () => resolve((server.address() as { port: number }).port)));
export async function startHarness(options: { payments?: boolean; reports?: boolean } = {}) {
  const reportSource = JSON.parse(readFileSync(new URL("../../../database/tests/fixtures/report-contract.json", import.meta.url), "utf8"));
  const googleKey = options.reports ? generateKeyPairSync("rsa", { modulusLength: 2048 }).privateKey.export({ type: "pkcs8", format: "pem" }).toString() : "";
  const seed: Product[] = [
    { id: "nasi", name: "Nasi + Bandeng", category: "Nasi", price: 12000, available: true, imageUrl: null },
    { id: "telur", name: "Nasi + Telur", category: "Nasi", price: 9000, available: true, imageUrl: null },
    { id: "tahu", name: "Tahu Goreng", category: "Lauk", price: 1500, available: true, imageUrl: null },
    { id: "teh", name: "Es Teh", category: "Minuman", price: 3000, available: true, imageUrl: null }
  ];
  const state = { draft_version: 1, published_version: 1, draft: seed, published: seed };
  const finance = financeFixture(); const commands = new Map<string, unknown>();
  const control = { reportJobs: [] as (ReportJob & { lease_token?: string })[], reportActors: [] as string[], reportLostCreate: false, googleStatus: 200, googleLostWrite: false, googleCorrupt: false, googleWrites: 0, googleSheets: [] as any[], finance, financeAdminWrites: [] as {p_actor: string; p_command: Record<string, unknown>}[], financeResponseLost: false, financeWrites: [] as unknown[], storageFail: false, dbFail: false, conflict: false, uploaded: Buffer.alloc(0), devices: [] as unknown[], providerCalls: 0, providerFail: false, providerStatus: {} as Record<string, unknown>, providerWrites: [] as Record<string, unknown>[], paymentRows: [] as { sequence: number; transaction_id: string; amount: number; paid_at: string }[] };
  const backend = createServer(async (req, res) => {
    const chunks: Buffer[] = []; for await (const chunk of req) chunks.push(Buffer.from(chunk));
    const bytes = Buffer.concat(chunks); const path = req.url ?? "";
    const reply = (value: unknown, status = 200) => { res.writeHead(status, { "Content-Type": "application/json" }); res.end(JSON.stringify(value)); };
    if (path.startsWith("/google/")) {
      if (path.includes("oauth2.googleapis.com/token")) { reply({ access_token: "fixture-google-token" }); return; }
      if (req.headers.authorization !== "Bearer fixture-google-token") { reply({}, 401); return; }
      if (control.googleStatus !== 200) { reply({ error: "fixture only" }, control.googleStatus); return; }
      if (req.method === "POST") {
        const input = JSON.parse(bytes.toString()); let wrote = false;
        for (const r of input.requests) {
          if (r.addSheet) control.googleSheets.push({ properties: r.addSheet.properties, data: [{ rowData: [] }] });
          if (r.updateCells) { const u = r.updateCells, sheet = control.googleSheets.find(s => s.properties.sheetId === u.range.sheetId); u.rows.forEach((r: unknown, i: number) => sheet.data[0].rowData[u.range.startRowIndex + i] = r); control.googleWrites++; wrote = true; }
        }
        reply({}, control.googleLostWrite && wrote ? 503 : 200); return;
      }
      const sheets = structuredClone(control.googleSheets);
      if (control.googleCorrupt && path.includes("includeGridData") && sheets.length) sheets[0].data[0].rowData[1].values[0] = { userEnteredValue: { stringValue: "tampered" } };
      reply({ sheets }); return;
    }
    if (path.startsWith("/midtrans/v2/")) {
      control.providerCalls++;
      if (req.headers.authorization !== "Basic " + Buffer.from("fixture-midtrans-key:").toString("base64")) { reply({}, 401); return; }
      reply(control.providerStatus, control.providerFail ? 503 : 200); return;
    }
    if (path.startsWith("/auth/v1/token")) { const input = JSON.parse(bytes.toString()); reply({ access_token: input.email === "owner@fixture.test" ? "fixture-admin" : "fixture-other", expires_in: 3600 }); return; }
    if (path === "/auth/v1/user") { reply({ id: req.headers.authorization === "Bearer fixture-admin" ? "fixture-admin-id" : "someone-else" }); return; }
    if (path.startsWith("/storage/v1/object/public/")) { res.writeHead(200, { "Content-Type": "image/jpeg" }); res.end(control.uploaded); return; }
    if (path.startsWith("/storage/v1/object/")) { control.uploaded = bytes; reply({}, control.storageFail ? 503 : 200); return; }
    if (control.dbFail) { reply({ error: "fixture database unavailable" }, 503); return; }
    if (path.startsWith("/rest/v1/payment_notifications")) {
      const query = new URL(path, "http://fixture").searchParams;
      const after = Number(query.get("sequence")?.replace("gt.", ""));
      reply(control.paymentRows.filter(row => row.sequence > after).sort((a,b) => a.sequence-b.sequence).slice(0, Number(query.get("limit")))); return;
    }
    if (path.startsWith("/rest/v1/report_jobs")) {
      const query = new URL(path, "http://fixture").searchParams, id = query.get("id")?.replace("eq.", "");
      const jobs = control.reportJobs.filter(j => !id || j.id === id);
      reply(query.get("select")?.includes("snapshot") ? jobs : jobs.map(({ snapshot, ...j }) => j).reverse()); return;
    }
    if (path.startsWith("/rest/v1/cash_sessions")) { reply(reportSource.sessions); return; }
    if (path.startsWith("/rest/v1/catalog_state")) { reply([state]); return; }
    if (path.startsWith("/rest/v1/device_sync_status")) { reply(control.devices); return; }
    if (path.startsWith("/rest/v1/rpc/")) {
      const input = JSON.parse(bytes.toString());
      if (path.endsWith("create_report")) {
        let job = control.reportJobs.find(j => j.id === input.p_id);
        if (job && (JSON.stringify(job.filter) !== JSON.stringify(input.p_filter) || job.actor !== input.p_actor)) { reply({ code: "40001" }, 409); return; }
        if (!job) { job = { id: input.p_id, filter: input.p_filter, snapshot: { ...structuredClone(reportSource), filter: input.p_filter }, actor: input.p_actor, created_at: new Date().toISOString(), target: null, state: "ready", attempts: 0, failures: 0, next_attempt_at: null, error_code: null, verified_at: null, content_hash: null }; control.reportJobs.push(job); control.reportActors.push(input.p_actor); }
        reply({ id: input.p_id, saved: true }, control.reportLostCreate ? 503 : 200); return;
      }
      if (path.endsWith("queue_report")) { const j = control.reportJobs.find(j => j.id === input.p_id)!; j.state = "queued"; j.target = input.p_target; j.next_attempt_at = new Date().toISOString(); reply(null); return; }
      if (path.endsWith("claim_report")) {
        const j = control.reportJobs.find(j => (!input.p_id || j.id === input.p_id) && ["queued", "failed"].includes(j.state) && j.next_attempt_at && Date.parse(j.next_attempt_at) <= Date.now());
        if (j) { j.state = "running"; j.attempts++; j.lease_token = input.p_token; } reply(j ?? null); return;
      }
      if (path.endsWith("finish_report")) {
        const j = control.reportJobs.find(j => j.id === input.p_id)!;
        if (j.lease_token !== input.p_token) { reply({ code: "40001" }, 409); return; }
        j.state = input.p_success ? "succeeded" : "failed"; j.error_code = input.p_code ?? null; j.next_attempt_at = input.p_retry ? new Date(Date.now() + 30000).toISOString() : null;
        if (input.p_success) { j.verified_at = new Date().toISOString(); j.content_hash = input.p_hash; } reply(null); return;
      }
      if (path.endsWith("finance_dashboard")) { reply({ ...finance, from: input.p_from, to: input.p_to }); return; }
      if (path.endsWith("apply_finance_admin")) {
        const command = input.p_command;
        if (commands.has(command.id)) { reply(commands.get(command.id)); return; }
        if (control.conflict || command.revision !== finance.revision) { reply({ code: "40001" }, 409); return; }
        control.financeAdminWrites.push(input); applyFixture(finance, command, input.p_actor);
        const result = { saved: true, revision: finance.revision, replayed: false }; commands.set(command.id, result);
        reply(result, control.financeResponseLost ? 503 : 200); return;
      }
      if (path.endsWith("record_provider_payment")) {
        control.providerWrites.push(input.p_payment);
        // Transport fixture only; PostgreSQL transition/atomicity rules are tested separately.
        const payment = input.p_payment;
        if (payment.status === "settlement" && !control.paymentRows.some(row => row.transaction_id === payment.transactionId))
          control.paymentRows.push({ sequence: control.paymentRows.length + 1, transaction_id: payment.transactionId, amount: payment.amount, paid_at: payment.paidAt });
        reply(null); return;
      }
      if (path.endsWith("save_catalog_draft")) {
        if (input.p_version !== state.draft_version) { reply({ code: "40001" }, 500); return; }
        state.draft = input.p_products; state.draft_version++; reply(null); return;
      }
      if (path.endsWith("publish_catalog")) {
        if (input.p_version !== state.draft_version) { reply({ code: "40001" }, 500); return; }
        state.published = state.draft; state.published_version++; reply(null); return;
      }
      if (path.endsWith("receive_finance_batch")) {
        if (control.conflict) { reply({ code: "40001" }, 409); return; }
        control.financeWrites.push(input.p_events); reply(input.p_events.map((e: { id: string }) => e.id)); return;
      }
      if (path.endsWith("receive_device_batch")) { reply({ accepted: control.conflict ? [] : input.p_events.map((e: { id: string }) => e.id), conflict: control.conflict }); return; }
      if (path.endsWith("report_device_status")) { control.devices = [{ device_id: input.p_device, pending_count: input.p_pending, catalog_version: input.p_catalog, last_seen_at: new Date().toISOString(), last_report_at: new Date().toISOString(), last_sync_at: null, conflict_at: null, conflict_events: [] }]; reply(null); return; }
    }
    reply({ error: "Unknown fixture path" }, 404);
  });
  const backendPort = await listen(backend);
  const probe = createServer(); const port = await listen(probe); await new Promise<void>(resolve => probe.close(() => resolve()));
  const origin = `http://127.0.0.1:${port}`;
  const processNext = spawn(process.execPath, [...(options.payments ? ["--import", "./integration/provider-preload.mjs"] : []), ...(options.reports ? ["--import", "./integration/sheets-preload.mjs"] : []), "node_modules/next/dist/bin/next", "start", "--hostname", "127.0.0.1", "--port", String(port)], {
    env: { ...process.env, APP_ORIGIN: origin, SUPABASE_URL: `http://127.0.0.1:${backendPort}`, SUPABASE_ANON_KEY: "fixture-anon", SUPABASE_SERVICE_ROLE_KEY: "fixture-service", ADMIN_USER_ID: "fixture-admin-id", DEVICE_API_TOKEN: "fixture-device-token-at-least-32-characters", DEVICE_ID: "kasir-utama", NEXT_TELEMETRY_DISABLED: "1", ...(options.reports ? { FIXTURE_GOOGLE_ORIGIN: `http://127.0.0.1:${backendPort}`, GOOGLE_SHEETS_ID: "fixture_workbook_1234567890", GOOGLE_SERVICE_ACCOUNT_JSON: JSON.stringify({ type: "service_account", client_email: "fixture@fixture.iam.gserviceaccount.com", private_key: googleKey }), EXPORT_RUNNER_TOKEN: "fixture-runner-token-with-at-least-32-characters" } : {}), ...(options.payments ? { FIXTURE_PROVIDER_ORIGIN: `http://127.0.0.1:${backendPort}`, MIDTRANS_SERVER_KEY: "fixture-midtrans-key", MIDTRANS_MERCHANT_ID: "fixture-merchant", MIDTRANS_ENV: "sandbox" } : {}) }, stdio: ["ignore", "pipe", "pipe"]
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
