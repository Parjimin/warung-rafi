begin;
-- Keep the original paid day when a later webhook reports a refund.
-- The durable notification retains the first verified settlement timestamp.
update public.provider_payments p set paid_at=n.paid_at from public.payment_notifications n
 where p.transaction_id=n.transaction_id and p.paid_at is distinct from n.paid_at;
create function public.preserve_provider_paid_at() returns trigger language plpgsql as $$
begin
 if old.status in ('settlement','refund','partial_refund') then new.paid_at:=old.paid_at; end if;
 return new;
end $$;
create trigger provider_paid_day before update on public.provider_payments for each row execute function public.preserve_provider_paid_at();
create index provider_paid_report on public.provider_payments(paid_at,transaction_id);
create index completed_order_report on public.order_snapshots(status) where status=2;
create table public.finance_admin_state(id integer primary key check(id=1), revision bigint not null default 0);
insert into public.finance_admin_state values(1,0);
create table public.qris_matches(
 transaction_id text primary key references public.provider_payments(transaction_id),
 device_id text not null, order_id text not null, difference bigint not null, reason text not null,
 actor text not null, created_at timestamptz not null default now(),
 unique(device_id,order_id), foreign key(device_id,order_id) references public.order_snapshots(device_id,id)
);
create table public.qris_fee_profiles(
 id text primary key, merchant_id text not null, label text not null,
 valid_from date not null, valid_to date not null check(valid_to>=valid_from),
 rate_bps integer not null check(rate_bps between 0 and 10000), fixed_fee bigint not null check(fixed_fee between 0 and 1000000000),
 rounding text not null check(rounding in ('half_up','floor','ceiling')),
 actor text not null, created_at timestamptz not null default now()
);
create table public.qris_costs(
 transaction_id text primary key references public.provider_payments(transaction_id),
 estimated_fee bigint check(estimated_fee>=0), profile jsonb,
 actual_mdr bigint check(actual_mdr>=0), actual_other bigint check(actual_other>=0), actual_refund bigint check(actual_refund>=0),
 reference text, actor text not null, updated_at timestamptz not null default now(),
 check((actual_mdr is null and actual_other is null and actual_refund is null and reference is null)
    or (actual_mdr is not null and actual_other is not null and actual_refund is not null and reference is not null and length(reference)>=3))
);
create table public.qris_payouts(
 id text primary key, merchant_id text not null, reference text not null, reported_on date not null,
 bank_name text not null, account_last4 text not null check(account_last4 ~ '^[0-9]{4}$'),
 gross bigint not null, transaction_fees bigint not null, refunds bigint not null,
 fee bigint not null check(fee>=0), adjustment bigint not null, expected_net bigint not null check(expected_net>=0),
 received_amount bigint check(received_amount>=0), received_on date, bank_reference text,
 reason text not null, actor text not null, created_at timestamptz not null default now(), voided_at timestamptz,
 check((received_amount is null and received_on is null and bank_reference is null)
    or (received_amount is not null and received_on is not null and bank_reference is not null and length(bank_reference)>=3))
);
create unique index qris_payout_reference on public.qris_payouts(merchant_id,reference) where voided_at is null;
-- One complete provider transaction per payout. Partial allocation is an explicit future extension.
create table public.qris_payout_items(
 transaction_id text primary key references public.provider_payments(transaction_id),
 payout_id text not null references public.qris_payouts(id), gross bigint not null, fees bigint not null, refunded bigint not null
);
create table public.finance_admin_events(
 id text primary key, revision bigint unique not null, action text not null, actor text not null, reason text not null,
 command jsonb not null, before_value jsonb, after_value jsonb not null, created_at timestamptz not null default now()
);
create trigger finance_admin_no_change before update or delete on public.finance_admin_events for each row execute function public.keep_finance_history();
create trigger fee_profile_no_change before update or delete on public.qris_fee_profiles for each row execute function public.keep_finance_history();

create function public.apply_finance_admin(p_command jsonb,p_actor text)
returns jsonb language plpgsql security invoker set search_path=public,pg_temp as $$
declare
 cmd_id text:=p_command->>'id'; action_name text:=p_command->>'action'; why text:=trim(p_command->>'reason');
 tid text:=p_command->>'transactionId'; did text:=p_command->>'deviceId'; oid text:=p_command->>'orderId';
 rev bigint; prior finance_admin_events%rowtype; proof provider_payments%rowtype; profile_row qris_fee_profiles%rowtype;
 payout qris_payouts%rowtype; pay jsonb; before_data jsonb; result jsonb:='{}';
 fee_value bigint; gross_value bigint; fees_value bigint; refunds_value bigint; net_value bigint; ids text[]; merchant text;
 begin_date date; end_date date; mdr bigint; other_fee bigint; refund_value bigint;
begin
 if p_actor is null or length(trim(p_actor)) not between 1 and 200 or cmd_id is null or cmd_id !~ '^[a-f0-9]{32}$'
    or why is null or length(why) not between 3 and 200 or jsonb_typeof(p_command) is distinct from 'object'
    or p_command->>'revision' is null then raise exception 'invalid admin command' using errcode='22023'; end if;
 select revision into rev from finance_admin_state where id=1 for update;
 select * into prior from finance_admin_events where id=cmd_id;
 if found then
   if prior.command is distinct from p_command or prior.actor is distinct from p_actor then raise exception 'command identity reused' using errcode='40001'; end if;
   return jsonb_build_object('saved',true,'revision',prior.revision,'replayed',true);
 end if;
 if (p_command->>'revision')::bigint<>rev then raise exception 'finance revision conflict' using errcode='40001'; end if;
 if action_name in ('match','unmatch','estimate_fee','actual_cost') then
   select * into proof from provider_payments where transaction_id=tid for update;
   if not found or proof.status not in ('settlement','refund','partial_refund') then raise exception 'provider proof unavailable' using errcode='40001'; end if;
 end if;
 if action_name='match' then
   select payload->'payment' into pay from order_snapshots where device_id=did and id=oid and status=2;
   if pay is null or (pay->>'method')::integer is distinct from 1 then raise exception 'completed QRIS order required' using errcode='40001'; end if;
   if exists(select 1 from qris_matches where transaction_id=tid or (device_id=did and order_id=oid)) then raise exception 'payment already matched' using errcode='40001'; end if;
   if proof.amount<>(pay->>'amount')::bigint and (p_command->>'allowDifference')::boolean is distinct from true then raise exception 'difference needs explicit acknowledgement' using errcode='40001'; end if;
   insert into qris_matches values(tid,did,oid,proof.amount-(pay->>'amount')::bigint,why,p_actor,now());
   select to_jsonb(m) into result from qris_matches m where transaction_id=tid;
 elsif action_name='unmatch' then
   select to_jsonb(m) into before_data from qris_matches m where transaction_id=tid;
   if before_data is null then raise exception 'match unavailable' using errcode='40001'; end if;
   delete from qris_matches where transaction_id=tid;result:=jsonb_build_object('transactionId',tid,'matched',false);
 elsif action_name='fee_profile' then
   begin_date:=(p_command->>'from')::date;end_date:=(p_command->>'to')::date;
   merchant:=p_command->>'merchantId';
   if begin_date is null or end_date is null or end_date<begin_date or merchant is null or length(merchant) not between 1 and 200
     or coalesce(length(trim(p_command->>'label')),0) not between 3 and 80 then raise exception 'invalid profile' using errcode='22023'; end if;
   if exists(select 1 from qris_fee_profiles where merchant_id=merchant and valid_from<=end_date and valid_to>=begin_date) then raise exception 'fee periods overlap' using errcode='40001'; end if;
   insert into qris_fee_profiles values(cmd_id,merchant,p_command->>'label',begin_date,end_date,(p_command->>'rateBps')::integer,(p_command->>'fixedFee')::bigint,p_command->>'rounding',p_actor,now());
   select to_jsonb(f) into result from qris_fee_profiles f where id=cmd_id;
 elsif action_name='estimate_fee' then
   select * into profile_row from qris_fee_profiles where merchant_id=proof.merchant_id and (proof.paid_at at time zone 'Asia/Jakarta')::date between valid_from and valid_to;
   if not found then raise exception 'no applicable tariff profile' using errcode='40001'; end if;
   fee_value:=(case profile_row.rounding when 'floor' then floor(proof.amount::numeric*profile_row.rate_bps/10000)
      when 'ceiling' then ceil(proof.amount::numeric*profile_row.rate_bps/10000) else round(proof.amount::numeric*profile_row.rate_bps/10000) end)::bigint+profile_row.fixed_fee;
   if exists(select 1 from qris_costs where transaction_id=tid and estimated_fee is not null) then raise exception 'estimate snapshot already saved' using errcode='40001'; end if;
   select to_jsonb(c) into before_data from qris_costs c where transaction_id=tid;
   insert into qris_costs(transaction_id,estimated_fee,profile,actor) values(tid,fee_value,to_jsonb(profile_row),p_actor)
     on conflict(transaction_id) do update set estimated_fee=excluded.estimated_fee,profile=excluded.profile,actor=excluded.actor,updated_at=now();
   select to_jsonb(c) into result from qris_costs c where transaction_id=tid;
 elsif action_name='actual_cost' then
   mdr:=(p_command->>'mdr')::bigint;other_fee:=(p_command->>'other')::bigint;refund_value:=(p_command->>'refunded')::bigint;
   if mdr is null or other_fee is null or refund_value is null or least(mdr,other_fee,refund_value)<0 or mdr+other_fee>proof.amount or refund_value>proof.amount
     or coalesce(length(trim(p_command->>'reference')),0) not between 3 and 200 then raise exception 'invalid reported costs' using errcode='22023'; end if;
   if exists(select 1 from qris_payout_items where transaction_id=tid) then raise exception 'void payout before correcting its costs' using errcode='40001'; end if;
   select to_jsonb(c) into before_data from qris_costs c where transaction_id=tid;
   insert into qris_costs(transaction_id,actual_mdr,actual_other,actual_refund,reference,actor)
      values(tid,mdr,other_fee,refund_value,p_command->>'reference',p_actor)
     on conflict(transaction_id) do update set actual_mdr=excluded.actual_mdr,actual_other=excluded.actual_other,actual_refund=excluded.actual_refund,reference=excluded.reference,actor=excluded.actor,updated_at=now();
   select to_jsonb(c) into result from qris_costs c where transaction_id=tid;
 elsif action_name='payout' then
   if jsonb_typeof(p_command->'transactions') is distinct from 'array' or jsonb_array_length(p_command->'transactions') not between 1 and 50 then raise exception 'invalid payout items' using errcode='22023'; end if;
   select array_agg(value order by value) into ids from jsonb_array_elements_text(p_command->'transactions');
   if cardinality(ids)<>(select count(distinct v) from unnest(ids) v) then raise exception 'duplicate payout item' using errcode='22023'; end if;
   -- Lock the same proof rows that webhook processing updates; all amounts are server-derived.
   perform 1 from provider_payments where transaction_id=any(ids) order by transaction_id for update;
   if (select count(*) from provider_payments p join qris_costs c using(transaction_id)
       where p.transaction_id=any(ids) and p.status in ('settlement','refund','partial_refund') and c.actual_mdr is not null)<>cardinality(ids)
       or exists(select 1 from qris_payout_items where transaction_id=any(ids)) then raise exception 'actual costs required or item already allocated' using errcode='40001'; end if;
   if (select count(distinct merchant_id) from provider_payments where transaction_id=any(ids))<>1 then raise exception 'mixed merchants' using errcode='22023'; end if;
   select min(p.merchant_id),sum(p.amount),sum(c.actual_mdr+c.actual_other),sum(c.actual_refund)
     into merchant,gross_value,fees_value,refunds_value from provider_payments p join qris_costs c using(transaction_id) where p.transaction_id=any(ids);
   fee_value:=(p_command->>'fee')::bigint;
   if fee_value is null or fee_value not between 0 and 1000000000 or (p_command->>'adjustment')::bigint not between -1000000000 and 1000000000
      or p_command->>'adjustment' is null or coalesce(length(trim(p_command->>'reference')),0) not between 3 and 200
      or coalesce(length(trim(p_command->>'bankName')),0) not between 2 and 50 or (p_command->>'accountLast4') !~ '^[0-9]{4}$'
      then raise exception 'invalid payout details' using errcode='22023'; end if;
   if (p_command->>'reportedOn')::date<(select max((paid_at at time zone 'Asia/Jakarta')::date) from provider_payments where transaction_id=any(ids)) then raise exception 'payout precedes payments' using errcode='22023'; end if;
   net_value:=gross_value-fees_value-refunds_value-fee_value+(p_command->>'adjustment')::bigint;
   insert into qris_payouts(id,merchant_id,reference,reported_on,bank_name,account_last4,gross,transaction_fees,refunds,fee,adjustment,expected_net,reason,actor)
     values(cmd_id,merchant,p_command->>'reference',(p_command->>'reportedOn')::date,p_command->>'bankName',p_command->>'accountLast4',gross_value,fees_value,refunds_value,fee_value,(p_command->>'adjustment')::bigint,net_value,why,p_actor);
   insert into qris_payout_items select p.transaction_id,cmd_id,p.amount,c.actual_mdr+c.actual_other,c.actual_refund
     from provider_payments p join qris_costs c using(transaction_id) where p.transaction_id=any(ids);
   select to_jsonb(q)||jsonb_build_object('transactions',ids) into result from qris_payouts q where id=cmd_id;
 elsif action_name in ('bank_receipt','void_payout') then
   select * into payout from qris_payouts where id=p_command->>'payoutId';
   if not found or payout.voided_at is not null then raise exception 'payout unavailable' using errcode='40001'; end if;
   select to_jsonb(payout)||jsonb_build_object('transactions',coalesce(jsonb_agg(transaction_id),'[]')) into before_data from qris_payout_items where payout_id=payout.id;
   if action_name='bank_receipt' then
     if (p_command->>'amount')::bigint not between 0 and 100000000000 or p_command->>'amount' is null
        or coalesce(length(trim(p_command->>'reference')),0) not between 3 and 200 or (p_command->>'receivedOn')::date<payout.reported_on then raise exception 'invalid bank receipt' using errcode='22023'; end if;
     update qris_payouts set received_amount=(p_command->>'amount')::bigint,received_on=(p_command->>'receivedOn')::date,bank_reference=p_command->>'reference' where id=payout.id;
   else
     update qris_payouts set voided_at=now() where id=payout.id;
     delete from qris_payout_items where payout_id=payout.id;
   end if;
   select to_jsonb(q) into result from qris_payouts q where id=payout.id;
 else raise exception 'unknown finance action' using errcode='22023';
 end if;
 rev:=rev+1;update finance_admin_state set revision=rev where id=1;
 insert into finance_admin_events(id,revision,action,actor,reason,command,before_value,after_value) values(cmd_id,rev,action_name,p_actor,why,p_command,before_data,result);
 return jsonb_build_object('saved',true,'revision',rev,'replayed',false);
end $$;

create function public.finance_dashboard(p_from date,p_to date,p_order_page integer default 0,p_provider_page integer default 0,p_payout_page integer default 0)
returns jsonb language plpgsql stable security invoker set search_path=public,pg_temp as $$
declare answer jsonb; start_at timestamptz; end_at timestamptz;
begin
 if p_from is null or p_to is null or p_to<p_from or p_to-p_from>30 or least(p_order_page,p_provider_page,p_payout_page)<0
    or greatest(p_order_page,p_provider_page,p_payout_page)>100000 or p_order_page is null or p_provider_page is null or p_payout_page is null then raise exception 'invalid report period' using errcode='22023'; end if;
 start_at:=p_from::timestamp at time zone 'Asia/Jakarta';end_at:=(p_to+1)::timestamp at time zone 'Asia/Jakarta';
 with sales as (
   select device_id,id,payload#>>'{order,number}' as number,(payload#>>'{payment,paidAt}')::timestamptz as paid_at,
     (payload#>>'{payment,amount}')::bigint as amount,(payload#>>'{payment,method}')::integer as method
   from order_snapshots where status=2 and (payload#>>'{payment,paidAt}')::timestamptz>=start_at and (payload#>>'{payment,paidAt}')::timestamptz<end_at
 ), proofs as (
   select * from provider_payments where status in ('settlement','refund','partial_refund') and paid_at>=start_at and paid_at<end_at
 ), payouts_period as (
   select * from qris_payouts where reported_on between p_from and p_to
 )
 select jsonb_build_object(
  'revision',(select revision from finance_admin_state where id=1),'generatedAt',now(),'from',p_from,'to',p_to,
  'summary',jsonb_build_object(
    'salesCount',(select count(*) from sales),'gross',coalesce((select sum(amount) from sales),0),
    'cashSales',coalesce((select sum(amount) from sales where method=0),0),'qrisSales',coalesce((select sum(amount) from sales where method=1),0),
    'refunds',coalesce((select sum(amount) from refunds where state=1 and (payload->>'completedAt')::timestamptz>=start_at and (payload->>'completedAt')::timestamptz<end_at),0),
    'providerCount',(select count(*) from proofs),'providerGross',coalesce((select sum(amount) from proofs),0),
    'unmatchedOrders',(select count(*) from sales s where method=1 and not exists(select 1 from qris_matches m where m.device_id=s.device_id and m.order_id=s.id)),
    'unmatchedProviders',(select count(*) from proofs p where not exists(select 1 from qris_matches m where m.transaction_id=p.transaction_id)),
    'differences',(select count(*) from qris_matches m join proofs p using(transaction_id) where m.difference<>0),
    'actualFees',coalesce((select sum(c.actual_mdr+c.actual_other) from qris_costs c join proofs p using(transaction_id)),0),
    'actualKnown',(select count(*) from qris_costs c join proofs p using(transaction_id) where c.actual_mdr is not null),
    'estimateFees',coalesce((select sum(c.estimated_fee) from qris_costs c join proofs p using(transaction_id)),0),
    'estimateKnown',(select count(*) from qris_costs c join proofs p using(transaction_id) where c.estimated_fee is not null),
    'providerRefunds',coalesce((select sum(c.actual_refund) from qris_costs c join proofs p using(transaction_id)),0),
    'payoutNet',coalesce((select sum(expected_net) from payouts_period where voided_at is null),0),
    'receivedBank',coalesce((select sum(received_amount) from qris_payouts where voided_at is null and received_on between p_from and p_to),0)
  ),
  'counts',jsonb_build_object('orders',(select count(*) from sales where method=1),'providers',(select count(*) from proofs),'payouts',(select count(*) from payouts_period)),
  'orders',coalesce((select jsonb_agg(to_jsonb(s)||jsonb_build_object('match',(select to_jsonb(m) from qris_matches m where m.device_id=s.device_id and m.order_id=s.id))) from (select * from sales where method=1 order by paid_at desc,device_id,id limit 50 offset p_order_page*50) s),'[]'),
  'providers',coalesce((select jsonb_agg(to_jsonb(p)||jsonb_build_object('match',(select to_jsonb(m) from qris_matches m where m.transaction_id=p.transaction_id),'cost',(select to_jsonb(c) from qris_costs c where c.transaction_id=p.transaction_id),'payout_id',(select payout_id from qris_payout_items i where i.transaction_id=p.transaction_id))) from (select * from proofs order by paid_at desc,transaction_id limit 50 offset p_provider_page*50) p),'[]'),
  'payouts',coalesce((select jsonb_agg(to_jsonb(q)||jsonb_build_object('transactions',coalesce((select jsonb_agg(transaction_id order by transaction_id) from qris_payout_items i where i.payout_id=q.id),'[]'))) from (select * from payouts_period order by reported_on desc,id limit 50 offset p_payout_page*50) q),'[]'),
  'profiles',coalesce((select jsonb_agg(to_jsonb(f)) from (select * from qris_fee_profiles where valid_from<=p_to and valid_to>=p_from order by merchant_id,valid_from) f),'[]'),
  'devices',coalesce((select jsonb_agg(jsonb_build_object('device_id',device_id,'last_seen_at',last_seen_at,'last_report_at',last_report_at,'pending_count',pending_count)) from device_sync_status),'[]'),
  'audit',coalesce((select jsonb_agg(jsonb_build_object('id',id,'action',action,'actor',actor,'reason',reason,'created_at',created_at,'revision',revision,'before_value',before_value,'after_value',after_value)) from (select * from finance_admin_events order by revision desc limit 50) a),'[]')
 ) into answer;
 return answer;
end $$;

alter table public.finance_admin_state enable row level security;
alter table public.qris_matches enable row level security;
alter table public.qris_fee_profiles enable row level security;
alter table public.qris_costs enable row level security;
alter table public.qris_payouts enable row level security;
alter table public.qris_payout_items enable row level security;
alter table public.finance_admin_events enable row level security;
revoke all on public.finance_admin_state,public.qris_matches,public.qris_fee_profiles,public.qris_costs,public.qris_payouts,public.qris_payout_items,public.finance_admin_events from anon,authenticated;
grant select,update on public.finance_admin_state to service_role;
grant select,insert,delete on public.qris_matches,public.qris_payout_items to service_role;
grant select,insert on public.qris_fee_profiles,public.finance_admin_events to service_role;
grant select,insert,update on public.qris_costs,public.qris_payouts to service_role;
revoke execute on function public.apply_finance_admin(jsonb,text),public.finance_dashboard(date,date,integer,integer,integer) from public,anon,authenticated;
grant execute on function public.apply_finance_admin(jsonb,text),public.finance_dashboard(date,date,integer,integer,integer) to service_role;
commit;
