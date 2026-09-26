begin;
create table public.report_jobs(
 id text primary key check(id ~ '^[a-f0-9]{32}$'), filter jsonb not null, snapshot jsonb not null, actor text not null,
 created_at timestamptz not null default now(), target text,
 state text not null default 'ready' check(state in ('ready','queued','running','failed','succeeded')),
 attempts integer not null default 0, failures integer not null default 0,
 lease_token text, lease_until timestamptz, next_attempt_at timestamptz,
 error_code text, verified_at timestamptz, content_hash text
);
create table public.report_attempts(
 job_id text not null references public.report_jobs(id), attempt integer not null, token text not null,
 started_at timestamptz not null default now(), finished_at timestamptz, outcome text, error_code text,
 primary key(job_id,attempt)
);
create function public.keep_report_snapshot() returns trigger language plpgsql as $$
begin
 if new.id<>old.id or new.filter<>old.filter or new.snapshot<>old.snapshot or new.actor<>old.actor or new.created_at<>old.created_at
 or (old.target is not null and new.target is distinct from old.target) then raise exception 'report snapshot immutable'; end if;
 return new;
end $$;
create trigger report_snapshot_immutable before update on public.report_jobs for each row execute function public.keep_report_snapshot();

create function public.report_source(p_filter jsonb) returns jsonb language plpgsql stable security invoker set search_path=public,pg_temp as $$
declare start_at timestamptz; end_at timestamptz; first_day date; last_day date; session_row cash_sessions%rowtype; answer jsonb; mode text:=p_filter->>'mode';
begin
 if mode='calendar' then
  first_day:=(p_filter->>'from')::date;last_day:=(p_filter->>'to')::date;
  if first_day is null or last_day is null or last_day<first_day or last_day-first_day>30 then raise exception 'invalid report period' using errcode='22023'; end if;
  start_at:=first_day::timestamp at time zone 'Asia/Jakarta';end_at:=(last_day+1)::timestamp at time zone 'Asia/Jakarta';
 elsif mode='session' then
  select * into session_row from cash_sessions where device_id=p_filter->>'deviceId' and id=p_filter->>'sessionId';
  if not found then raise exception 'session unavailable' using errcode='22023'; end if;
  start_at:=(session_row.payload->>'openedAt')::timestamptz;end_at:=coalesce((session_row.payload->>'closedAt')::timestamptz+interval '1 microsecond',now());
  first_day:=(start_at at time zone 'Asia/Jakarta')::date;last_day:=(end_at at time zone 'Asia/Jakarta')::date;
 else raise exception 'unknown report mode' using errcode='22023'; end if;
 with orders_scope as (
  select o.*, (select session_id from finance_events e where e.device_id=o.device_id and e.order_id=o.id and e.kind='sale' limit 1) as session_id, (select payload->>'actor' from finance_events e where e.device_id=o.device_id and e.order_id=o.id and e.kind='sale' limit 1) as cashier
  from order_snapshots o where status in (2,3) and
   case when mode='calendar' then coalesce((o.payload#>>'{payment,paidAt}')::timestamptz,(o.payload#>>'{order,updatedAt}')::timestamptz)>=start_at and coalesce((o.payload#>>'{payment,paidAt}')::timestamptz,(o.payload#>>'{order,updatedAt}')::timestamptz)<end_at
   else o.device_id=session_row.device_id and (exists(select 1 from finance_events e where e.device_id=o.device_id and e.order_id=o.id and e.kind='sale' and e.session_id=session_row.id)
      or (o.status=3 and (o.payload#>>'{order,updatedAt}')::timestamptz>=start_at and (o.payload#>>'{order,updatedAt}')::timestamptz<end_at)) end
 ), sessions_scope as (
  select s.* from cash_sessions s where case when mode='session' then s.device_id=session_row.device_id and s.id=session_row.id
   else (s.payload->>'openedAt')::timestamptz<end_at and coalesce((s.payload->>'closedAt')::timestamptz,now())>=start_at end
 ), refund_scope as (
  select r.*, (select e.payload->>'actor' from finance_events e where e.device_id=r.device_id and e.kind='refund_requested' and e.payload#>>'{details,id}'=r.id limit 1) as requested_by,case when mode='session' then r.device_id=session_row.device_id and r.payload->>'sessionId'=session_row.id
   else (r.payload->>'completedAt')::timestamptz>=start_at and (r.payload->>'completedAt')::timestamptz<end_at end as completed_in_period
  from refunds r where exists(select 1 from orders_scope o where o.device_id=r.device_id and o.id=r.order_id)
   or (mode='session' and r.device_id=session_row.device_id and r.payload->>'sessionId'=session_row.id)
   or (mode='calendar' and ((r.payload->>'requestedAt')::timestamptz>=start_at and (r.payload->>'requestedAt')::timestamptz<end_at
    or (r.payload->>'completedAt')::timestamptz>=start_at and (r.payload->>'completedAt')::timestamptz<end_at))
 ), proof_scope as (
  select p.*, (select received_at from payment_notifications n where n.transaction_id=p.transaction_id) as received_at,case when mode='calendar' then p.paid_at>=start_at and p.paid_at<end_at else true end as in_period
  from provider_payments p where p.status in ('settlement','refund','partial_refund') and
    ((mode='calendar' and p.paid_at>=start_at and p.paid_at<end_at) or exists(select 1 from qris_matches m join orders_scope o on o.device_id=m.device_id and o.id=m.order_id where m.transaction_id=p.transaction_id))
 ), payout_scope as (
  select p.*,p.reported_on between first_day and last_day as reported_in_period,p.received_on between first_day and last_day as received_in_period
   from qris_payouts p where mode='calendar' and (p.reported_on between first_day and last_day or p.received_on between first_day and last_day)
 )
 select jsonb_build_object('schema',1,'capturedAt',now(),'revision',(select revision from finance_admin_state),'filter',p_filter,'start',start_at,'end',end_at,'from',first_day,'to',last_day,
  'orders',coalesce((select jsonb_agg((to_jsonb(o)#-'{payload,order,customerLabel}')||jsonb_build_object('match',(select to_jsonb(m) from qris_matches m where m.device_id=o.device_id and m.order_id=o.id)) order by o.device_id,o.id) from orders_scope o),'[]'),
  'refunds',coalesce((select jsonb_agg(to_jsonb(r) order by r.device_id,r.id) from refund_scope r),'[]'),
  'providers',coalesce((select jsonb_agg(to_jsonb(p)||jsonb_build_object('match',(select to_jsonb(m) from qris_matches m where m.transaction_id=p.transaction_id),'cost',(select to_jsonb(c) from qris_costs c where c.transaction_id=p.transaction_id)) order by p.transaction_id) from proof_scope p),'[]'),
  'payouts',coalesce((select jsonb_agg(to_jsonb(p)||jsonb_build_object('items',coalesce((select jsonb_agg(to_jsonb(i) order by transaction_id) from qris_payout_items i where i.payout_id=p.id),'[]')) order by p.id) from payout_scope p),'[]'),
  'sessions',coalesce((select jsonb_agg(to_jsonb(s) order by s.device_id,s.id) from sessions_scope s),'[]'),
  'events',coalesce((select jsonb_agg(to_jsonb(e)||jsonb_build_object('in_period',case when mode='session' then true else e.occurred_at>=start_at and e.occurred_at<end_at end) order by e.device_id,e.sequence) from finance_events e where exists(select 1 from sessions_scope s where s.device_id=e.device_id and s.id=e.session_id) or (e.occurred_at>=start_at and e.occurred_at<end_at and (mode='calendar' or (e.device_id=session_row.device_id and exists(select 1 from orders_scope o where o.device_id=e.device_id and o.id=e.order_id))))),'[]'),
  'devices',coalesce((select jsonb_agg(to_jsonb(d) order by device_id) from device_sync_status d),'[]'),
  'activity',coalesce((select jsonb_agg(a order by a->>'created_at',a->>'id') from (
    select jsonb_build_object('id','finance/'||id,'created_at',created_at,'actor',actor,'action',action,'reason',reason,'object',coalesce(command->>'transactionId',command->>'payoutId',id)) as a from finance_admin_events where created_at>=start_at and created_at<end_at
    union all select jsonb_build_object('id','catalog/'||id,'created_at',created_at,'actor',actor,'action',action,'reason','Perubahan katalog','object',details->>'version') from audit_log where created_at>=start_at and created_at<end_at
  ) t),'[]')
 ) into answer;
 if octet_length(answer::text)>6000000 or jsonb_array_length(answer->'orders')>5000 or jsonb_array_length(answer->'events')>20000 then raise exception 'report too large; choose a shorter period' using errcode='22023'; end if;
 return answer;
end $$;

create function public.create_report(p_id text,p_filter jsonb,p_actor text) returns jsonb language plpgsql security invoker set search_path=public,pg_temp as $$
declare old report_jobs%rowtype; data jsonb;
begin
 if p_id is null or p_id !~ '^[a-f0-9]{32}$' or coalesce(length(p_actor),0) not between 1 and 200 then raise exception 'invalid report' using errcode='22023';end if;
 perform pg_advisory_xact_lock(hashtextextended('report:'||p_id,0));
 select * into old from report_jobs where id=p_id;
 if found then
  if old.filter<>p_filter or old.actor<>p_actor then raise exception 'report identity reused' using errcode='40001';end if;
 else
  data:=report_source(p_filter);
  insert into report_jobs(id,filter,snapshot,actor) values(p_id,p_filter,data,p_actor);
 end if;
 return jsonb_build_object('id',p_id,'saved',true);
end $$;
create function public.queue_report(p_id text,p_target text) returns void language plpgsql security invoker set search_path=public,pg_temp as $$
declare job report_jobs%rowtype;
begin
 if p_target is null or p_target !~ '^[A-Za-z0-9_-]{20,150}$' then raise exception 'invalid target' using errcode='22023';end if;
 select * into job from report_jobs where id=p_id for update;
 if not found or (job.target is not null and job.target<>p_target) or (job.state='running' and job.lease_until>now()) then raise exception 'report busy or target changed' using errcode='40001';end if;
 if job.state='queued' then return;end if;
 update report_jobs set target=p_target,state='queued',next_attempt_at=now(),failures=0,error_code=null,lease_token=null,lease_until=null where id=p_id;
end $$;
create function public.claim_report(p_token text,p_id text default null) returns jsonb language plpgsql security invoker set search_path=public,pg_temp as $$
declare job report_jobs%rowtype;
begin
 if p_token is null or p_token !~ '^[a-f0-9]{32}$' then raise exception 'invalid lease' using errcode='22023';end if;
 -- A single short dispatcher lock coordinates selection; no network occurs in the transaction.
 perform pg_advisory_xact_lock(hashtextextended('report-dispatch',0));
 select * into job from report_jobs j where (p_id is null or j.id=p_id) and j.target is not null and
  ((state in ('queued','failed') and next_attempt_at<=now()) or (state='running' and lease_until<=now()))
  and not exists(select 1 from report_jobs other where other.target=j.target and other.state='running' and other.lease_until>now())
  order by created_at,id limit 1 for update;
 if not found then return null;end if;
 update report_attempts set finished_at=now(),outcome='lease_expired',error_code='interrupted' where job_id=job.id and finished_at is null;
 update report_jobs set state='running',attempts=attempts+1,lease_token=p_token,lease_until=now()+interval '2 minutes',next_attempt_at=null where id=job.id returning * into job;
 insert into report_attempts(job_id,attempt,token) values(job.id,job.attempts,p_token);
 return to_jsonb(job);
end $$;
create function public.finish_report(p_id text,p_token text,p_success boolean,p_code text default null,p_hash text default null,p_retry boolean default false) returns void language plpgsql security invoker set search_path=public,pg_temp as $$
declare job report_jobs%rowtype;
begin
 select * into job from report_jobs where id=p_id for update;
 if not found or job.state<>'running' or job.lease_token<>p_token or p_token is null then raise exception 'stale export worker' using errcode='40001';end if;
 if p_success is null or (p_success and (p_hash is null or p_hash !~ '^[a-f0-9]{64}$')) or (not p_success and p_code not in ('configuration','permission','quota','network','target_changed','verification','capacity','interrupted','service')) then raise exception 'invalid outcome' using errcode='22023';end if;
 update report_jobs set state=case when p_success then 'succeeded' else 'failed' end,
  failures=case when p_success then 0 else failures+1 end,
  next_attempt_at=case when not p_success and p_retry and job.failures<5 then now()+make_interval(secs=>least(1800,30*power(2,job.failures)::integer)+floor(random()*10)::integer) else null end,
  error_code=case when p_success then null else p_code end,verified_at=case when p_success then now() else verified_at end,
  content_hash=case when p_success then p_hash else content_hash end,lease_token=null,lease_until=null where id=p_id;
 update report_attempts set finished_at=now(),outcome=case when p_success then 'verified' else 'failed' end,error_code=p_code where job_id=p_id and token=p_token;
end $$;
alter table public.report_jobs enable row level security;alter table public.report_attempts enable row level security;
revoke all on public.report_jobs,public.report_attempts from anon,authenticated;
grant select,insert,update on public.report_jobs,public.report_attempts to service_role;
revoke execute on function public.report_source(jsonb),public.create_report(text,jsonb,text),public.queue_report(text,text),public.claim_report(text,text),public.finish_report(text,text,boolean,text,text,boolean) from public,anon,authenticated;
grant execute on function public.report_source(jsonb),public.create_report(text,jsonb,text),public.queue_report(text,text),public.claim_report(text,text),public.finish_report(text,text,boolean,text,text,boolean) to service_role;
commit;
