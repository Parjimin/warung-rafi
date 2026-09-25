begin;
create table public.device_sync_status (
  device_id text primary key,
  last_seen_at timestamptz not null default now(),
  last_sync_at timestamptz,
  pending_count integer check(pending_count>=0),
  catalog_version bigint check(catalog_version>=0),
  last_report_at timestamptz,
  conflict_at timestamptz,
  conflict_events jsonb not null default '[]'
);

create function public.report_device_status(p_device text,p_pending integer,p_catalog bigint)
returns void language plpgsql security invoker set search_path=public,pg_temp as $$
begin
  if p_device is null or length(p_device) not between 1 and 80 or p_pending is null or p_pending<0 or p_catalog is null or p_catalog<0 then raise exception 'invalid report'; end if;
  perform pg_advisory_xact_lock(hashtextextended('device:'||p_device,0));
  insert into device_sync_status(device_id,pending_count,catalog_version,last_report_at)
    values(p_device,p_pending,p_catalog,now())
    on conflict(device_id) do update set last_seen_at=now(),pending_count=excluded.pending_count,
      catalog_version=excluded.catalog_version,last_report_at=now();
  -- A heartbeat is not proof that a rejected batch was repaired. Preserve conflicts.
end $$;

create function public.receive_device_batch(p_device text,p_events jsonb)
returns jsonb language plpgsql security invoker set search_path=public,pg_temp as $$
declare accepted jsonb; event_ids jsonb;
begin
  if p_device is null or length(p_device) not between 1 and 80 or p_events is null or jsonb_typeof(p_events)<>'array' or jsonb_array_length(p_events) not between 1 and 50 then raise exception 'invalid batch'; end if;
  perform pg_advisory_xact_lock(hashtextextended('device:'||p_device,0));
  insert into device_sync_status(device_id) values(p_device)
    on conflict(device_id) do update set last_seen_at=now();
  begin
    accepted:=ingest_device_events(p_device,p_events);
  exception when serialization_failure or unique_violation then
    -- This subtransaction rolls back the entire batch; diagnostics survive outside it.
    select jsonb_agg(value->>'id') into event_ids from jsonb_array_elements(p_events);
    update device_sync_status set conflict_at=now(),conflict_events=event_ids where device_id=p_device;
    return jsonb_build_object('accepted','[]'::jsonb,'conflict',true);
  end;
  update device_sync_status set last_sync_at=now(),conflict_at=null,conflict_events='[]' where device_id=p_device;
  return jsonb_build_object('accepted',accepted,'conflict',false);
end $$;

alter table public.device_sync_status enable row level security;
revoke all on public.device_sync_status from anon,authenticated;
grant all on public.device_sync_status to service_role;
revoke execute on function public.report_device_status(text,integer,bigint),public.receive_device_batch(text,jsonb) from public,anon,authenticated;
grant execute on function public.report_device_status(text,integer,bigint),public.receive_device_batch(text,jsonb) to service_role;
commit;
