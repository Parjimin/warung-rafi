export class HttpError extends Error {
  status: number;
  constructor(status: number, message: string) { super(message); this.status = status; }
}
export function required(name: string): string {
  const value = process.env[name];
  if (!value) throw new HttpError(503, "Layanan belum dikonfigurasi.");
  return value;
}
export function json(data: unknown, status = 200) {
  return Response.json(data, { status, headers: { "Cache-Control": "no-store" } });
}
export async function handled(action: () => Promise<Response>): Promise<Response> {
  try { return await action(); }
  catch (error) {
    if (error instanceof HttpError) return json({ error: error.message }, error.status);
    // Never return provider payloads, tokens, credentials, or SQL errors to the caller.
    console.error("Request failed", error instanceof Error ? error.name : "UnknownError");
    return json({ error: "Layanan belum berhasil menyimpan data. Silakan coba kembali." }, 503);
  }
}
export async function bytes(request: Request, limit: number): Promise<Buffer> {
  if (!request.body) throw new HttpError(400, "Data kosong.");
  const reader = request.body.getReader(); const chunks: Uint8Array[] = []; let size = 0;
  try {
    while (true) {
      const { done, value } = await reader.read(); if (done) break;
      size += value.byteLength;
      if (size > limit) { await reader.cancel(); throw new HttpError(413, "Data terlalu besar."); }
      chunks.push(value);
    }
    return Buffer.concat(chunks);
  } finally { reader.releaseLock(); }
}
export async function body(request: Request, limit = 256_000): Promise<unknown> {
  const data = await bytes(request, limit);
  try { return JSON.parse(data.toString("utf8")); }
  catch { throw new HttpError(400, "Format JSON tidak valid."); }
}
export function object(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new HttpError(400, "Data tidak valid.");
  return value as Record<string, unknown>;
}
