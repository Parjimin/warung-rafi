import { sameSecret } from "../../../../lib/auth.ts";
import { handled, HttpError, json, required } from "../../../../lib/http.ts";
import { runExport } from "../../../../lib/report-worker.ts";
export const maxDuration = 60;
export async function POST(request: Request) {
 return handled(async () => {
  const secret = required("EXPORT_RUNNER_TOKEN");
  if (secret.length < 32) throw new HttpError(503, "Runner belum dikonfigurasi.");
  if (!sameSecret(request.headers.get("authorization") ?? "", `Bearer ${secret}`)) throw new HttpError(401, "Runner belum diizinkan.");
  return json(await runExport());
 });
}
