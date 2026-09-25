import { HttpError, object } from "./http.ts";
export type DeviceStatus = {
  device_id: string; last_seen_at: string; last_sync_at: string | null;
  pending_count: number | null; catalog_version: number | null; last_report_at: string | null;
  conflict_at: string | null; conflict_events: string[];
};
export function deviceReport(input: unknown) {
  const value = object(input);
  if (!Number.isSafeInteger(value.pendingCount) || Number(value.pendingCount) < 0 || Number(value.pendingCount) > 2_147_483_647 ||
      !Number.isSafeInteger(value.catalogVersion) || Number(value.catalogVersion) < 0)
    throw new HttpError(400, "Status perangkat tidak valid.");
  return { pendingCount: Number(value.pendingCount), catalogVersion: Number(value.catalogVersion) };
}
