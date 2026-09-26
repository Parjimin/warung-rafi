begin;
create table public.login_windows(key text primary key,started_at timestamptz not null default now(),attempts integer not null default 0);
alter table public.login_windows enable row level security;
revoke all on public.login_windows from anon,authenticated;
grant select,insert,update,delete on public.login_windows to service_role;
create function public.reserve_login_attempt(p_key text) returns boolean language plpgsql security invoker set search_path=public,pg_temp as $$
declare account_count integer; total_count integer;
begin
 if p_key is null or p_key !~ '^[a-f0-9]{64}$' then raise exception 'invalid bucket';end if;
 perform pg_advisory_xact_lock(hashtextextended('admin-login-limiter',0));
 delete from login_windows where started_at<=now()-interval '10 minutes';
 insert into login_windows(key) values(p_key),('global') on conflict do nothing;
 select attempts into account_count from login_windows where key=p_key;
 select attempts into total_count from login_windows where key='global';
 if account_count>=10 or total_count>=120 then return false;end if;
 update login_windows set attempts=attempts+1 where key in (p_key,'global');return true;
end $$;
create function public.device_recovery_page(p_device text,p_after text default '',p_state text default null) returns jsonb language plpgsql stable security invoker set search_path=public,pg_temp as $$
declare state text; orders jsonb; finance jsonb; next_id text;
begin
 if p_device is null or length(p_device) not between 1 and 80 or p_after is null or length(p_after)>80 then raise exception 'invalid recovery request';end if;
 select jsonb_build_object('sequence',sequence,'payload',payload) into finance from finance_events where device_id=p_device order by sequence desc limit 1;
 select md5(coalesce(string_agg(id||':'||version||':'||md5(payload::text),',' order by id),'')||coalesce(finance::text,'')) into state from order_snapshots where device_id=p_device;
 if p_state is not null and p_state<>state then raise exception 'recovery source changed' using errcode='40001';end if;
 select coalesce(jsonb_agg(jsonb_build_object('id',id,'version',version,'payload',payload) order by id),'[]'),case when count(*)=50 then max(id) else null end into orders,next_id
 from(select id,version,payload from order_snapshots where device_id=p_device and id>p_after order by id limit 50) t;
 return jsonb_build_object('state',state,'orders',orders,'finance',finance,'next',next_id,'deviceId',p_device);
end $$;
create function public.device_setup() returns jsonb language sql stable security invoker set search_path=public,pg_temp as $$
 select jsonb_build_object('schema',6,'serverTime',now());
$$;
revoke all on function public.reserve_login_attempt(text),public.device_recovery_page(text,text,text),public.device_setup() from public,anon,authenticated;
grant execute on function public.reserve_login_attempt(text),public.device_recovery_page(text,text,text),public.device_setup() to service_role;
commit;
