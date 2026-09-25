import { HttpError, object } from "./http.ts";

const invalid = () => new HttpError(400, "Catatan keuangan tidak valid.");
const integer = (value: unknown, min = 0, max = Number.MAX_SAFE_INTEGER) => Number.isSafeInteger(value) && Number(value) >= min && Number(value) <= max;
const guid = (value: unknown) => typeof value === "string" && /^[a-f0-9]{32}$/.test(value);
const date = (value: unknown) => typeof value === "string" && value.length <= 40 && Number.isFinite(Date.parse(value));
const note = (value: unknown, min = 0) => typeof value === "string" && value.trim().length >= min && value.length <= 200;
const kinds = new Set(["session_opened", "session_closed", "sale", "cash_in", "cash_out", "refund_requested", "refund_completed", "refund_failed"]);
export type FinanceEvent = { id: string; sequence: number; kind: string; occurredAt: string; sessionId: string | null; orderId: string | null; amount: number; cashDelta: number; actor: string; reason: string; details: Record<string, unknown> };

// Shape validation at the HTTP boundary; PostgreSQL validates ordered state transitions.
export function financeEvents(input: unknown): FinanceEvent[] {
  if (!Array.isArray(input) || input.length < 1 || input.length > 50) throw invalid();
  return input.map(value => {
    const e = object(value); const d = object(e.details);
    if (typeof e.kind !== "string" || !kinds.has(e.kind) || !integer(e.sequence, 1) || !date(e.occurredAt) || !integer(e.amount) || !integer(e.cashDelta, -Number.MAX_SAFE_INTEGER) || !note(e.reason) || !["Kasir", "Pengelola"].includes(String(e.actor))) throw invalid();
    if (e.sessionId !== null && !guid(e.sessionId) || e.orderId !== null && !guid(e.orderId)) throw invalid();
    let id: string; let delta: number; const amount = Number(e.amount);
    if (e.kind === "session_opened" || e.kind === "session_closed") {
      if (!guid(e.sessionId) || e.orderId !== null || d.id !== e.sessionId || !date(d.openedAt) || d.openedBy !== "Kasir" || !integer(d.openingCash, 0, 1_000_000_000) || !note(d.closingNote)) throw invalid();
      const open = e.kind === "session_opened"; id = `${open ? "open" : "close"}-${e.sessionId}`; delta = open ? amount : 0;
      if (open ? amount !== d.openingCash || d.closedAt !== null || d.countedCash !== null || d.expectedAtClose !== null : !date(d.closedAt) || !integer(d.countedCash, 0, 1_000_000_000) || !integer(d.expectedAtClose) || amount !== d.countedCash || e.reason !== d.closingNote) throw invalid();
    } else if (e.kind === "sale") {
      if (!guid(e.sessionId) || !guid(e.orderId) || amount < 1 || d.orderId !== e.orderId || !integer(d.method, 0, 1) || d.amount !== amount || !integer(d.tendered, amount) || d.change !== Number(d.tendered) - amount || !date(d.paidAt) || d.method === 1 && (d.tendered !== amount || d.change !== 0)) throw invalid();
      id = `sale-${e.orderId}`; delta = d.method === 0 ? amount : 0;
    } else if (e.kind === "cash_in" || e.kind === "cash_out") {
      if (!guid(e.id) || !guid(e.sessionId) || e.orderId !== null || !integer(amount, 1, 1_000_000_000) || !note(e.reason, 3) || Object.keys(d).length !== 0) throw invalid();
      id = e.id as string; delta = e.kind === "cash_in" ? amount : -amount;
    } else {
      if (!guid(d.id) || !guid(e.orderId) || d.orderId !== e.orderId || d.amount !== amount || !integer(amount, 1, 1_000_000_000) || !integer(d.channel, 0, 1) || !note(d.reason, 3) || !date(d.requestedAt)) throw invalid();
      const requested = e.kind === "refund_requested"; const completed = e.kind === "refund_completed";
      id = `${requested ? "request" : "resolve"}-${d.id}`; delta = completed && d.channel === 0 ? -amount : 0;
      if (d.state !== (requested ? 0 : completed ? 1 : 2) || d.sessionId !== e.sessionId || (completed ? !guid(e.sessionId) || !date(d.completedAt) || !note(d.reference, 3) || d.failureReason !== "" : e.sessionId !== null || d.completedAt !== null)) throw invalid();
      if (requested ? e.actor !== "Kasir" || d.approvedBy !== "" || d.reference !== "" || d.failureReason !== "" || e.reason !== d.reason : e.actor !== "Pengelola" || d.approvedBy !== "Pengelola" || (completed ? e.reason !== d.reference : !note(d.failureReason, 3) || e.reason !== d.failureReason || d.reference !== "")) throw invalid();
    }
    if (e.id !== id || e.cashDelta !== delta) throw invalid();
    if (!e.kind.startsWith("refund_") && e.actor !== "Kasir") throw invalid();
    return { id, sequence: e.sequence as number, kind: e.kind, occurredAt: e.occurredAt as string, sessionId: e.sessionId as string | null, orderId: e.orderId as string | null, amount, cashDelta: delta, actor: e.actor as string, reason: e.reason as string, details: d };
  });
}
