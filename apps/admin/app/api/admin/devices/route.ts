import { admin } from "../../../../lib/auth.ts";
import { database } from "../../../../lib/db.ts";
import { handled, json } from "../../../../lib/http.ts";
export async function GET() {
  return handled(async () => {
    await admin();
    return json(await database("device_sync_status?select=device_id,last_seen_at,last_sync_at,pending_count,catalog_version,last_report_at,conflict_at,conflict_events&order=device_id&limit=100"));
  });
}
