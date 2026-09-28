import { admin } from "../../../../lib/auth.ts";
import { handled, json } from "../../../../lib/http.ts";
import { readiness } from "../../../../lib/readiness.ts";
export async function GET() {
 return handled(async () => { await admin(); return json(await readiness()); });
}
