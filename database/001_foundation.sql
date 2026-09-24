-- Run once in a new Supabase project using SQL Editor. No production data is seeded.
begin;
create table public.catalog_state (
  id integer primary key check (id=1), draft_version bigint not null default 0,
  published_version bigint not null default 0, draft jsonb not null default '[]',
  published jsonb not null default '[]', updated_at timestamptz not null default now()
);
insert into public.catalog_state(id) values(1);
create table public.audit_log (
  id bigint generated always as identity primary key, actor text not null,
  action text not null, details jsonb not null, created_at timestamptz not null default now()
);
create table public.device_events (
  device_id text not null, event_id text not null, aggregate_id text not null,
  version bigint not null check(version>0), payload jsonb not null,
  received_at timestamptz not null default now(), primary key(device_id,event_id),
  unique(device_id,aggregate_id,version)
);
create table public.order_snapshots (
  device_id text not null, id text not null, version bigint not null,
  status integer not null check(status between 0 and 3), payload jsonb not null,
  updated_at timestamptz not null default now(), primary key(device_id,id)
);
create table public.provider_payments (
  transaction_id text primary key, merchant_id text not null, order_id text not null,
  amount bigint not null check(amount>0), status text not null,
  paid_at timestamptz not null, updated_at timestamptz not null default now()
);
-- A single transaction-scoped lock serializes notification inserts. Identity alone
-- is not a safe polling cursor: concurrent transactions can otherwise commit out of order.
create table public.payment_notifications (
  sequence bigint generated always as identity primary key,
  transaction_id text not null unique references public.provider_payments(transaction_id),
  amount bigint not null check(amount>0), paid_at timestamptz not null,
  received_at timestamptz not null default now()
);

create function public.save_catalog_draft(p_version bigint,p_products jsonb,p_actor text)
returns void language plpgsql security invoker set search_path=public,pg_temp as $$
begin
  if jsonb_typeof(p_products)<>'array' or jsonb_array_length(p_products)>300 then raise exception 'invalid catalog'; end if;
  update catalog_state set draft=p_products,draft_version=draft_version+1,updated_at=now() where id=1 and draft_version=p_version;
  if not found then raise exception 'catalog conflict' using errcode='40001'; end if;
  insert into audit_log(actor,action,details) values(p_actor,'catalog.draft',jsonb_build_object('version',p_version+1,'products',p_products));
end $$;
create function public.publish_catalog(p_version bigint,p_actor text)
returns void language plpgsql security invoker set search_path=public,pg_temp as $$
begin
  update catalog_state set published=draft,published_version=published_version+1,updated_at=now()
  where id=1 and draft_version=p_version and jsonb_array_length(draft)>0;
  if not found then raise exception 'catalog conflict or empty' using errcode='40001'; end if;
  insert into audit_log(actor,action,details) values(p_actor,'catalog.published',jsonb_build_object('draftVersion',p_version));
end $$;

create function public.ingest_device_events(p_device text,p_events jsonb)
returns jsonb language plpgsql security invoker set search_path=public,pg_temp as $$
declare e jsonb; saved jsonb; head order_snapshots%rowtype; accepted jsonb:='[]'; previous_status integer;
begin
  if length(p_device)<1 or jsonb_typeof(p_events)<>'array' or jsonb_array_length(p_events) not between 1 and 50 then raise exception 'invalid batch'; end if;
  perform pg_advisory_xact_lock(hashtextextended('device:'||p_device,0));
  for e in select value from jsonb_array_elements(p_events) loop
    if e->>'id' is distinct from (e->>'aggregateId')||':'||(e->>'version')
      or e#>>'{payload,order,id}' is distinct from e->>'aggregateId'
      or e#>>'{payload,order,version}' is distinct from e->>'version'
      then raise exception 'invalid envelope'; end if;
    select payload into saved from device_events where device_id=p_device and event_id=e->>'id';
    if found then
      if saved is distinct from e->'payload' then raise exception 'event id reused' using errcode='23505'; end if;
      accepted:=accepted||jsonb_build_array(e->>'id'); continue;
    end if;
    select * into head from order_snapshots where device_id=p_device and id=e->>'aggregateId';
    if (e->>'version')::bigint <> coalesce(head.version,0)+1 then raise exception 'version gap' using errcode='40001'; end if;
    if head.status in (2,3) then raise exception 'final order cannot change' using errcode='40001'; end if;
    insert into device_events(device_id,event_id,aggregate_id,version,payload)
      values(p_device,e->>'id',e->>'aggregateId',(e->>'version')::bigint,e->'payload');
    insert into order_snapshots(device_id,id,version,status,payload)
      values(p_device,e->>'aggregateId',(e->>'version')::bigint,(e#>>'{payload,order,status}')::integer,e->'payload')
      on conflict(device_id,id) do update set version=excluded.version,status=excluded.status,payload=excluded.payload,updated_at=now();
    accepted:=accepted||jsonb_build_array(e->>'id');
  end loop;
  return accepted;
end $$;

create function public.record_provider_payment(p_payment jsonb)
returns void language plpgsql security invoker set search_path=public,pg_temp as $$
declare previous provider_payments%rowtype; next_status text:=p_payment->>'status';
begin
  if next_status not in ('pending','settlement','deny','cancel','expire','refund','partial_refund') then raise exception 'unsupported status'; end if;
  perform pg_advisory_xact_lock(hashtextextended('provider-notification-stream',0));
  select * into previous from provider_payments where transaction_id=p_payment->>'transactionId';
  if found then
    if previous.amount<>(p_payment->>'amount')::bigint or previous.merchant_id<>p_payment->>'merchantId' or previous.order_id<>p_payment->>'orderId' then raise exception 'payment identity changed'; end if;
    -- A delayed request must not regress a recorded settlement/refund or create another popup.
    if previous.status='refund' or (previous.status='partial_refund' and next_status<>'refund') then return; end if;
    if previous.status='settlement' and next_status not in ('refund','partial_refund') then return; end if;
    if previous.status in ('cancel','expire','deny') and next_status='pending' then return; end if;
  end if;
  insert into provider_payments(transaction_id,merchant_id,order_id,amount,status,paid_at)
    values(p_payment->>'transactionId',p_payment->>'merchantId',p_payment->>'orderId',(p_payment->>'amount')::bigint,next_status,(p_payment->>'paidAt')::timestamptz)
    on conflict(transaction_id) do update set status=excluded.status,paid_at=excluded.paid_at,updated_at=now();
  if next_status='settlement' then
    insert into payment_notifications(transaction_id,amount,paid_at)
      values(p_payment->>'transactionId',(p_payment->>'amount')::bigint,(p_payment->>'paidAt')::timestamptz)
      on conflict(transaction_id) do nothing;
  end if;
end $$;

-- The browser and desktop never receive service_role credentials. All access is through authenticated APIs.
alter table public.catalog_state enable row level security;
alter table public.audit_log enable row level security;
alter table public.device_events enable row level security;
alter table public.order_snapshots enable row level security;
alter table public.provider_payments enable row level security;
alter table public.payment_notifications enable row level security;
revoke all on public.catalog_state,public.audit_log,public.device_events,public.order_snapshots,public.provider_payments,public.payment_notifications from anon,authenticated;
grant all on public.catalog_state,public.audit_log,public.device_events,public.order_snapshots,public.provider_payments,public.payment_notifications to service_role;
grant usage,select on all sequences in schema public to service_role;
revoke execute on function public.save_catalog_draft(bigint,jsonb,text),public.publish_catalog(bigint,text),public.ingest_device_events(text,jsonb),public.record_provider_payment(jsonb) from public,anon,authenticated;
grant execute on function public.save_catalog_draft(bigint,jsonb,text),public.publish_catalog(bigint,text),public.ingest_device_events(text,jsonb),public.record_provider_payment(jsonb) to service_role;
commit;
