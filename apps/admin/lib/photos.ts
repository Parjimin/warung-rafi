import sharp from "sharp";
import { randomUUID } from "node:crypto";
import { bytes, HttpError, required } from "./http.ts";

export const PHOTO_MAX_BYTES = 3_000_000;
export const PHOTO_BUCKET = "menu-photos";
const types = new Map([["image/jpeg", "jpeg"], ["image/png", "png"], ["image/webp", "webp"]]);

export async function preparePhoto(request: Request): Promise<Buffer> {
  const expected = types.get(request.headers.get("content-type")?.split(";")[0].trim() ?? "");
  if (!expected) throw new HttpError(415, "Pilih foto JPG, PNG, atau WebP.");
  const data = await bytes(request, PHOTO_MAX_BYTES);
  try {
    const image = sharp(data, { failOn: "warning", limitInputPixels: 25_000_000 });
    const metadata = await image.metadata();
    if (metadata.format !== expected || (metadata.pages ?? 1) !== 1) throw new Error("Invalid image");
    // Decode and re-encode: reject damaged files, remove metadata, produce WPF-compatible JPEG.
    return await image.rotate().resize(1200, 1200, { fit: "inside", withoutEnlargement: true })
      .flatten({ background: "#ffffff" }).jpeg({ quality: 85 }).toBuffer();
  } catch { throw new HttpError(400, "Foto rusak, terlalu besar dimensinya, atau formatnya tidak sesuai. Gunakan foto diam maksimal 25 megapiksel."); }
}

export async function uploadPhoto(data: Buffer): Promise<string> {
  const base = required("SUPABASE_URL").replace(/\/$/, "");
  const key = required("SUPABASE_SERVICE_ROLE_KEY");
  const path = `menu/${randomUUID()}.jpg`;
  const response = await fetch(`${base}/storage/v1/object/${PHOTO_BUCKET}/${path}`, {
    method: "POST", body: new Uint8Array(data), signal: AbortSignal.timeout(15_000),
    headers: { apikey: key, Authorization: `Bearer ${key}`, "Content-Type": "image/jpeg", "x-upsert": "false", "cache-control": "max-age=31536000" }
  });
  if (!response.ok) throw new HttpError(503, "Foto belum berhasil diunggah. Periksa layanan penyimpanan lalu coba kembali.");
  return `${base}/storage/v1/object/public/${PHOTO_BUCKET}/${path}`;
}
