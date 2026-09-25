import { device } from "../../../../lib/auth.ts";
import { body, handled, json, required } from "../../../../lib/http.ts";
import { rpc } from "../../../../lib/db.ts";
import { deviceReport } from "../../../../lib/device-status.ts";
export async function POST(request: Request) {
  return handled(async () => {
    device(request); const report = deviceReport(await body(request, 2000));
    await rpc("report_device_status", { p_device: required("DEVICE_ID"), p_pending: report.pendingCount, p_catalog: report.catalogVersion });
    return json({ recorded: true });
  });
}
