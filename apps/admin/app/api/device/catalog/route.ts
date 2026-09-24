import { device } from "../../../../lib/auth.ts";
import { handled, json } from "../../../../lib/http.ts";
import { database } from "../../../../lib/db.ts";
import type { CatalogState } from "../../../../lib/catalog.ts";
export async function GET(request: Request) {
  return handled(async () => {
    device(request);
    const [state] = await database("catalog_state?id=eq.1&select=published_version,published") as CatalogState[];
    return json({ version: state.published_version, products: state.published });
  });
}
