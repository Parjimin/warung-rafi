import { admin, sameOrigin } from "../../../../lib/auth.ts";
import { handled, json } from "../../../../lib/http.ts";
import { preparePhoto, uploadPhoto } from "../../../../lib/photos.ts";
export const runtime = "nodejs";
export async function POST(request: Request) {
  return handled(async () => {
    sameOrigin(request); await admin();
    return json({ imageUrl: await uploadPhoto(await preparePhoto(request)) }, 201);
  });
}
