import { HttpError, object } from "./http.ts";
export type SyncEvent = { id: string; aggregateId: string; version: number; payload: Record<string, unknown> };
const bad = () => new HttpError(400, "Data pesanan tidak valid.");
const integer = (v: unknown, min = 0, max = Number.MAX_SAFE_INTEGER) => Number.isSafeInteger(v) && Number(v) >= min && Number(v) <= max;
export function events(input: unknown): SyncEvent[] {
  if (!Array.isArray(input) || input.length < 1 || input.length > 50) throw bad();
  return input.map(item => {
    const event = object(item); const payload = object(event.payload); const order = object(payload.order);
    if (typeof event.aggregateId !== "string" || !/^[a-f0-9]{32}$/.test(event.aggregateId) || !integer(event.version, 1) || event.id !== `${event.aggregateId}:${event.version}`) throw bad();
    if (order.id !== event.aggregateId || order.version !== event.version || !integer(order.status, 0, 3) || !Array.isArray(order.lines) || order.lines.length > 300) throw bad();
    for (const key of ["createdAt", "updatedAt"]) if (typeof order[key] !== "string" || !Number.isFinite(Date.parse(order[key]))) throw bad();
    if (typeof order.number !== "string" || order.number.length > 80 || typeof order.customerLabel !== "string" || order.customerLabel.length > 60) throw bad();
    let total = 0; const ids = new Set();
    for (const item of order.lines) {
      const line = object(item);
      if (typeof line.productId !== "string" || line.productId.length > 80 || ids.has(line.productId) || typeof line.name !== "string" || line.name.length > 60 || typeof line.category !== "string" || !integer(line.unitPrice, 1, 1_000_000_000) || !integer(line.quantity, 1, 9999)) throw bad();
      ids.add(line.productId); total += Number(line.unitPrice) * Number(line.quantity);
    }
    if (!Number.isSafeInteger(total) || order.total !== total) throw bad();
    if (order.status === 2) {
      const payment = object(payload.payment);
      if (total <= 0 || payment.orderId !== order.id || !integer(payment.method, 0, 1) || payment.amount !== total || !integer(payment.tendered, total) || payment.change !== Number(payment.tendered) - total || typeof payment.paidAt !== "string" || !Number.isFinite(Date.parse(payment.paidAt))) throw bad();
      if (payment.method === 1 && (payment.tendered !== total || payment.change !== 0)) throw bad();
    } else if (payload.payment != null) throw bad();
    if (order.status === 3 && (typeof order.cancellationReason !== "string" || !order.cancellationReason.trim())) throw bad();
    return { id: event.id as string, aggregateId: event.aggregateId, version: event.version as number, payload };
  });
}
