import { device } from "../../../../lib/auth.ts";
import { handled, json } from "../../../../lib/http.ts";
import { refreshLiveSheets } from "../../../../lib/live-sheets.ts";
export const maxDuration = 60;
export async function POST(request: Request) {
 return handled(async () => { device(request); return json(await refreshLiveSheets()); });
}
