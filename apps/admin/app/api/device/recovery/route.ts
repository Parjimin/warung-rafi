import { device } from "../../../../lib/auth.ts";
import { handled, HttpError, json, required } from "../../../../lib/http.ts";
import { rpc } from "../../../../lib/db.ts";
export async function GET(request: Request) {
 return handled(async () => {
  device(request); const query = new URL(request.url).searchParams, after = query.get("after") ?? "", state = query.get("state");
  if ((after && !/^[a-f0-9]{32}$/.test(after)) || (state && !/^[a-f0-9]{32}$/.test(state))) throw new HttpError(400, "Identitas pemeriksaan tidak valid.");
  return json(await rpc("device_recovery_page", { p_device: required("DEVICE_ID"), p_after: after, p_state: state }));
 });
}
