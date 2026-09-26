begin;
-- Projections may change; the journal itself can only grow. No PIN or credential is synced.
create table public.cash_sessions (
  device_id text not null, id text not null, expected bigint not null check(expected>=0), closed boolean not null default false,
  payload jsonb not null, primary key(device_id,id)
);
create unique index one_device_open_session on public.cash_sessions(device_id) where not closed;
create table public.refunds (
  device_id text not null, id text not null, order_id text not null, amount bigint not null check(amount>0), state integer not null check(state between 0 and 2),
  payload jsonb not null, primary key(device_id,id), foreign key(device_id,order_id) references public.order_snapshots(device_id,id)
);
create index refunds_order on public.refunds(device_id,order_id);
create table public.finance_events (
  device_id text not null, sequence bigint not null check(sequence>0), id text not null, kind text not null,
  session_id text, order_id text, amount bigint not null check(amount>=0), cash_delta bigint not null,
  occurred_at timestamptz not null, received_at timestamptz not null default now(), payload jsonb not null,
  primary key(device_id,sequence), unique(device_id,id)
);
create index finance_session on public.finance_events(device_id,session_id,sequence);
create function public.keep_finance_history() returns trigger language plpgsql as $$
begin raise exception 'finance history is immutable'; end $$;
create trigger finance_no_change before update or delete on public.finance_events for each row execute function public.keep_finance_history();

create function public.receive_finance_batch(p_device text,p_events jsonb)
returns jsonb language plpgsql security invoker set search_path=public,pg_temp as $$
declare e jsonb; d jsonb; old jsonb; s cash_sessions%rowtype; r refunds%rowtype; payment jsonb;
  seq bigint; last_seq bigint; amount bigint; delta bigint; kind text; sid text; oid text; eid text;
  reserved bigint; accepted jsonb:='[]';
begin
  if p_device is null or length(p_device) not between 1 and 80 or p_events is null or jsonb_typeof(p_events)<>'array' or jsonb_array_length(p_events) not between 1 and 50 then raise exception 'invalid finance batch'; end if;
  -- Same lock as order ingestion: a refund cannot race a completed-order snapshot.
  perform pg_advisory_xact_lock(hashtextextended('device:'||p_device,0));
  select coalesce(max(sequence),0) into last_seq from finance_events where device_id=p_device;
  for e in select value from jsonb_array_elements(p_events) loop
    eid:=e->>'id';seq:=(e->>'sequence')::bigint;kind:=e->>'kind';sid:=e->>'sessionId';oid:=e->>'orderId';d:=e->'details';amount:=(e->>'amount')::bigint;delta:=(e->>'cashDelta')::bigint;
    if eid is null or seq is null or amount is null or delta is null or amount<0 or jsonb_typeof(d) is distinct from 'object' then raise exception 'invalid finance event'; end if;
    select payload into old from finance_events where device_id=p_device and id=eid;
    if found then
      if old<>e then raise exception 'finance identity reused' using errcode='40001'; end if;
      accepted:=accepted||jsonb_build_array(eid);continue;
    end if;
    if seq<>last_seq+1 then raise exception 'finance sequence gap' using errcode='40001'; end if;
    if kind='session_opened' then
      if sid is null or oid is not null or eid<>'open-'||sid or amount is distinct from (d->>'openingCash')::bigint or delta<>amount or d->>'id' is distinct from sid or d->>'closedAt' is not null then raise exception 'invalid opening'; end if;
      insert into cash_sessions values(p_device,sid,amount,false,d);
    elsif kind in ('sale','cash_in','cash_out','session_closed','refund_completed') then
      select * into s from cash_sessions where device_id=p_device and id=sid;
      if not found or s.closed then raise exception 'cash session unavailable' using errcode='40001'; end if;
      if kind='sale' then
        select payload->'payment' into payment from order_snapshots where device_id=p_device and id=oid and status=2;
        if payment is null then raise exception 'sale snapshot not received' using errcode='40001'; end if;
        if eid<>'sale-'||oid or payment<>d or amount is distinct from (payment->>'amount')::bigint or delta<>(case when (payment->>'method')::integer=0 then amount else 0 end) then raise exception 'sale differs from payment'; end if;
      elsif kind in ('cash_in','cash_out') then
        if oid is not null or amount<1 or amount>1000000000 or length(trim(e->>'reason')) not between 3 and 200 or delta<>(case when kind='cash_in' then amount else -amount end) then raise exception 'invalid movement'; end if;
      elsif kind='session_closed' then
        if oid is not null or eid<>'close-'||sid or delta<>0 or d->>'closedAt' is null or (d->>'expectedAtClose')::bigint is distinct from s.expected or (d->>'countedCash')::bigint is distinct from amount or d->>'id' is distinct from sid or d->>'openedAt' is distinct from s.payload->>'openedAt' or d->>'openingCash' is distinct from s.payload->>'openingCash' or d->>'openedBy' is distinct from s.payload->>'openedBy' or (amount<>s.expected and length(trim(d->>'closingNote'))<3) then raise exception 'invalid closing'; end if;
        update cash_sessions set closed=true,payload=d where device_id=p_device and id=sid;
      end if;
      -- Refund transition is checked below before either projection is committed.
      if s.expected+delta<0 then raise exception 'negative drawer'; end if;
      update cash_sessions set expected=expected+delta where device_id=p_device and id=sid;
    elsif kind not in ('refund_requested','refund_failed') then raise exception 'unknown finance kind';
    end if;
    if kind='refund_requested' then
      select payload->'payment' into payment from order_snapshots where device_id=p_device and id=oid and status=2;
      if payment is null then raise exception 'refund order not received' using errcode='40001'; end if;
      select coalesce(sum(refunds.amount),0) into reserved from refunds where device_id=p_device and order_id=oid and state in (0,1);
      if amount<1 or amount>(payment->>'amount')::bigint-reserved or delta<>0 or sid is not null or eid<>'request-'||(d->>'id') or (d->>'state')::integer is distinct from 0 or (d->>'amount')::bigint is distinct from amount or d->>'orderId' is distinct from oid then raise exception 'invalid refund request'; end if;
      insert into refunds values(p_device,d->>'id',oid,amount,0,d);
    elsif kind in ('refund_completed','refund_failed') then
      select * into r from refunds where device_id=p_device and id=d->>'id';
      if not found or r.state<>0 then raise exception 'refund is not pending' using errcode='40001'; end if;
      if eid<>'resolve-'||r.id or oid is distinct from r.order_id or amount<>r.amount or (d - array['state','completedAt','sessionId','approvedBy','reference','failureReason'])<>(r.payload - array['state','completedAt','sessionId','approvedBy','reference','failureReason']) or d->>'approvedBy' is distinct from 'Pengelola' then raise exception 'refund identity changed'; end if;
      if kind='refund_completed' then
        if delta<>(case when (r.payload->>'channel')::integer=0 then -amount else 0 end) or (d->>'state')::integer is distinct from 1 or d->>'completedAt' is null or d->>'sessionId' is distinct from sid or length(trim(d->>'reference'))<3 then raise exception 'invalid completed refund'; end if;
      else
        if delta<>0 or sid is not null or (d->>'state')::integer is distinct from 2 or d->>'completedAt' is not null or d->>'sessionId' is not null or length(trim(d->>'failureReason'))<3 then raise exception 'invalid failed refund'; end if;
      end if;
      update refunds set state=(d->>'state')::integer,payload=d where device_id=p_device and id=r.id;
    end if;
    insert into finance_events(device_id,sequence,id,kind,session_id,order_id,amount,cash_delta,occurred_at,payload)
      values(p_device,seq,eid,kind,sid,oid,amount,delta,(e->>'occurredAt')::timestamptz,e);
    last_seq:=seq;accepted:=accepted||jsonb_build_array(eid);
  end loop;
  return accepted;
end $$;
alter table public.cash_sessions enable row level security;
alter table public.refunds enable row level security;
alter table public.finance_events enable row level security;
revoke all on public.cash_sessions,public.refunds,public.finance_events from anon,authenticated;
grant select,insert,update on public.cash_sessions,public.refunds to service_role;
grant select,insert on public.finance_events to service_role;
revoke execute on function public.receive_finance_batch(text,jsonb),public.keep_finance_history() from public,anon,authenticated;
grant execute on function public.receive_finance_batch(text,jsonb) to service_role;
commit;
