import { HttpError, object } from "./http.ts";
export const categories = ["Nasi", "Lauk", "Sundukan", "Minuman"] as const;
export type Product = { id: string; name: string; category: string; price: number; available: boolean; imageUrl: string | null };
export type CatalogState = { draft_version: number; published_version: number; draft: Product[]; published: Product[] };
export function products(input: unknown): Product[] {
  if (!Array.isArray(input) || input.length > 300) throw new HttpError(400, "Maksimal 300 menu.");
  const ids = new Set<string>();
  return input.map(item => {
    const value = object(item);
    const { id, name, category, price, available, imageUrl } = value;
    if (typeof id !== "string" || !/^[a-zA-Z0-9_-]{1,80}$/.test(id) || ids.has(id)) throw new HttpError(400, "Identitas menu tidak valid atau berulang.");
    ids.add(id);
    if (typeof name !== "string" || !name.trim() || name.length > 60 || !categories.includes(category as typeof categories[number])) throw new HttpError(400, "Nama atau kategori tidak valid.");
    if (!Number.isSafeInteger(price) || Number(price) < 1 || Number(price) > 1_000_000_000 || typeof available !== "boolean") throw new HttpError(400, "Harga atau ketersediaan tidak valid.");
    let image: string | null = null;
    if (imageUrl) {
      try { const url = new URL(String(imageUrl)); if (url.protocol !== "https:" || url.username || url.password || url.href.length > 2000) throw new Error(); image = url.href; }
      catch { throw new HttpError(400, "Foto harus berupa URL HTTPS."); }
    }
    return { id, name: name.trim(), category: category as string, price: price as number, available, imageUrl: image };
  });
}
