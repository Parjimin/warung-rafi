import { cookies } from "next/headers";
import { sameOrigin, SESSION_COOKIE } from "../../../../lib/auth.ts";
import { handled, json } from "../../../../lib/http.ts";
export async function POST(request: Request) {
  return handled(async () => { sameOrigin(request); (await cookies()).delete(SESSION_COOKIE); return json({ signedOut: true }); });
}
