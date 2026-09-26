import { admin, sameOrigin } from "../../../../lib/auth.ts";
import { body, handled, json } from "../../../../lib/http.ts";
import { rpc } from "../../../../lib/db.ts";
import { financeCommand, reportQuery } from "../../../../lib/reconciliation.ts";
export async function GET(request: Request) {
  return handled(async () => { await admin(); return json(await rpc("finance_dashboard", reportQuery(new URL(request.url).searchParams))); });
}
export async function POST(request: Request) {
  return handled(async () => {
    sameOrigin(request); const actor = await admin();
    return json(await rpc("apply_finance_admin", { p_command: financeCommand(await body(request, 32000)), p_actor: actor }));
  });
}
