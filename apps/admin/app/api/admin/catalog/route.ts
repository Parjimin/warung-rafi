import { admin, sameOrigin } from "../../../../lib/auth.ts";
import { body, handled, HttpError, json, object } from "../../../../lib/http.ts";
import { database, rpc } from "../../../../lib/db.ts";
import { products } from "../../../../lib/catalog.ts";
export async function GET() {
  return handled(async () => { await admin(); return json((await database("catalog_state?id=eq.1&select=draft_version,published_version,draft,published") as unknown[])[0]); });
}
export async function POST(request: Request) {
  return handled(async () => {
    sameOrigin(request); const actor = await admin(); const input = object(await body(request));
    if (!Number.isSafeInteger(input.version) || Number(input.version) < 0) throw new HttpError(400, "Versi katalog tidak valid.");
    if (input.action === "save") await rpc("save_catalog_draft", { p_version: input.version, p_products: products(input.products), p_actor: actor });
    else if (input.action === "publish") await rpc("publish_catalog", { p_version: input.version, p_actor: actor });
    else throw new HttpError(400, "Tindakan tidak dikenal.");
    return json({ saved: true });
  });
}
