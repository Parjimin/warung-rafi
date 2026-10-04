begin;
-- One durable lease per workbook, shared by all serverless instances.
create table public.live_sheets_state (
 target text primary key, lease_token text, lease_until timestamptz,
 next_attempt_at timestamptz not null default now(), content_hash text,
 verified_at timestamptz, error_code text, failures integer not null default 0
);
alter table public.live_sheets_state enable row level security;
revoke all on public.live_sheets_state from anon,authenticated;
grant select,insert,update on public.live_sheets_state to service_role;
create function public.claim_live_sheets(p_target text,p_token text) returns jsonb
language plpgsql security invoker set search_path=public,pg_temp as $$
declare job live_sheets_state%rowtype; today date := (now() at time zone 'Asia/Jakarta')::date; source jsonb;
begin
 if p_target is null or p_target !~ '^[A-Za-z0-9_-]{20,150}$' or p_token is null or p_token !~ '^[a-f0-9]{32}$' then raise exception 'invalid request' using errcode='22023'; end if;
 insert into live_sheets_state(target) values(p_target) on conflict do nothing;
 select * into job from live_sheets_state where target=p_target for update;
 if job.lease_until>now() or job.next_attempt_at>now() then
  return jsonb_build_object('claimed',false,'verifiedAt',job.verified_at,'code',job.error_code);
 end if;
 source:=report_source(jsonb_build_object('mode','calendar','from',date_trunc('month',today)::date,'to',today));
 update live_sheets_state set lease_token=p_token,lease_until=now()+interval '2 minutes',next_attempt_at=now()+interval '1 minute' where target=p_target;
 return jsonb_build_object('claimed',true,'source',source,'hash',job.content_hash,'verifiedAt',job.verified_at,'code',job.error_code);
end $$;
create function public.finish_live_sheets(p_target text,p_token text,p_hash text default null,p_code text default null) returns void
language plpgsql security invoker set search_path=public,pg_temp as $$
declare job live_sheets_state%rowtype;
begin
 select * into job from live_sheets_state where target=p_target for update;
 if not found or p_token is null or job.lease_token is distinct from p_token or job.lease_until<=now() then raise exception 'stale worker' using errcode='40001'; end if;
 if (p_code is null and (p_hash is null or p_hash !~ '^[a-f0-9]{64}$')) or (p_code is not null and p_code not in ('configuration','permission','quota','network','target_changed','verification','capacity','interrupted','service')) then raise exception 'invalid result' using errcode='22023';end if;
 update live_sheets_state set lease_token=null,lease_until=null,
  failures=case when p_code is null then 0 else least(failures+1,6) end,
  next_attempt_at=now()+make_interval(secs=>case when p_code is null then 60 else least(1800,60*power(2,least(job.failures,5))::integer) end),
  content_hash=case when p_code is null then p_hash else content_hash end,
  verified_at=case when p_code is null then now() else verified_at end,error_code=p_code where target=p_target;
end $$;
revoke execute on function public.claim_live_sheets(text,text),public.finish_live_sheets(text,text,text,text) from public,anon,authenticated;
grant execute on function public.claim_live_sheets(text,text),public.finish_live_sheets(text,text,text,text) to service_role;
create function public.release_live_sheets(p_target text,p_token text) returns void
language plpgsql security invoker set search_path=public,pg_temp as $$
begin
 update live_sheets_state set lease_token=null,lease_until=null,next_attempt_at=now()+interval '1 minute'
 where target=p_target and lease_token=p_token and lease_until>now();
 if not found then raise exception 'stale worker' using errcode='40001';end if;
end $$;
revoke execute on function public.release_live_sheets(text,text) from public,anon,authenticated;
grant execute on function public.release_live_sheets(text,text) to service_role;
commit;
