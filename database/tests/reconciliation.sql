\set ON_ERROR_STOP on
begin;
create function pg_temp.command(action text, extra jsonb default '{}') returns jsonb language sql as $$
 select jsonb_build_object('id',md5(random()::text),'revision',(select revision from finance_admin_state where id=1),'action',action,'reason','Pemeriksaan fixture')||extra
$$;
create function pg_temp.reject_command(cmd jsonb, expected text) returns void language plpgsql as $$
begin
 begin perform apply_finance_admin(cmd,'owner');
 exception when others then
   if sqlstate=expected then return; end if;
   raise exception 'Expected %, got %: %',expected,sqlstate,sqlerrm;
 end;
 raise exception 'Invalid command accepted: %',cmd;
end $$;
insert into order_snapshots(device_id,id,version,status,payload)
select 'kasir',md5(v::text),1,2,jsonb_build_object('order',jsonb_build_object('number','R-'||v),'payment',jsonb_build_object('method',case when v=3 then 0 else 1 end,'amount',10000,'paidAt','2026-09-25T18:00:00Z')) from generate_series(1,3) v;
insert into order_snapshots(device_id,id,version,status,payload) values('kasir',md5('old'),1,2,'{"order":{"number":"Yesterday"},"payment":{"method":1,"amount":9000,"paidAt":"2026-09-25T16:59:59Z"}}');
insert into refunds values('kasir',md5('refund'),md5('1'),1000,1,'{"completedAt":"2026-09-26T10:00:00Z"}');
select record_provider_payment(jsonb_build_object('transactionId','proof-'||v,'merchantId','merchant','orderId','qr-'||v,'amount',10000,'status','settlement','paidAt','2026-09-25T18:00:00Z')) from generate_series(1,3) v;
select record_provider_payment('{"transactionId":"difference","merchantId":"merchant","orderId":"qr-d","amount":11000,"status":"settlement","paidAt":"2026-09-25T18:00:00Z"}');
do $$
declare cmd jsonb; saved jsonb; snap jsonb; target_payout text; count_before integer; rev_before bigint;
begin
 snap:=finance_dashboard('2026-09-26','2026-09-26');
 if (snap#>>'{summary,gross}')::bigint<>30000 or (snap#>>'{summary,qrisSales}')::bigint<>20000 or (snap#>>'{summary,providerGross}')::bigint<>41000 or (snap#>>'{summary,refunds}')::bigint<>1000 or (snap#>>'{summary,unmatchedOrders}')::integer<>2 or (snap#>>'{summary,unmatchedProviders}')::integer<>4 or (snap#>>'{summary,actualKnown}')::integer<>0 then raise exception 'baseline totals/timezone/null costs'; end if;
 cmd:=pg_temp.command('match',jsonb_build_object('transactionId','proof-1','deviceId','kasir','orderId',md5('1')));
 saved:=apply_finance_admin(cmd,'owner');
 if (apply_finance_admin(cmd,'owner')->>'replayed')::boolean is distinct from true then raise exception 'retry not replayed'; end if;
 perform pg_temp.reject_command(cmd||'{"reason":"changed identity"}','40001');
 perform pg_temp.reject_command(pg_temp.command('match',jsonb_build_object('transactionId','proof-2','deviceId','kasir','orderId',md5('1'))),'40001');
 perform pg_temp.reject_command(pg_temp.command('match',jsonb_build_object('transactionId','proof-2','deviceId','kasir','orderId',md5('3'))),'40001');
 perform pg_temp.reject_command(pg_temp.command('match',jsonb_build_object('transactionId','difference','deviceId','kasir','orderId',md5('2'))),'40001');
 perform apply_finance_admin(pg_temp.command('match',jsonb_build_object('transactionId','difference','deviceId','kasir','orderId',md5('2'),'allowDifference',true)),'owner');
 if (select difference from qris_matches where transaction_id='difference')<>1000 then raise exception 'difference lost'; end if;
 perform pg_temp.reject_command(pg_temp.command('unmatch','{"transactionId":"proof-1"}')||'{"revision":0}','40001');
 perform apply_finance_admin(pg_temp.command('unmatch','{"transactionId":"proof-1"}'),'owner');
 if not exists(select 1 from finance_admin_events where action='unmatch' and before_value->>'transaction_id'='proof-1') then raise exception 'match history lost'; end if;
 perform pg_temp.reject_command(pg_temp.command('estimate_fee','{"transactionId":"proof-1"}'),'40001');
 cmd:=pg_temp.command('fee_profile','{"merchantId":"merchant","label":"Fixture tariff","from":"2026-09-01","to":"2026-09-30","rateBps":65,"fixedFee":10,"rounding":"half_up"}');
 perform apply_finance_admin(cmd,'owner');
 perform pg_temp.reject_command(pg_temp.command('fee_profile',cmd-'id'-'revision'-'action'-'reason'),'40001');
 perform apply_finance_admin(pg_temp.command('estimate_fee','{"transactionId":"proof-1"}'),'owner');
 if (select estimated_fee from qris_costs where transaction_id='proof-1')<>75 or (select actual_mdr from qris_costs where transaction_id='proof-1') is not null then raise exception 'estimate became actual'; end if;
 perform pg_temp.reject_command(pg_temp.command('estimate_fee','{"transactionId":"proof-1"}'),'40001');
 perform pg_temp.reject_command(pg_temp.command('actual_cost','{"transactionId":"proof-1","mdr":10001,"other":0,"refunded":0,"reference":"fixture"}'),'22023');
 perform pg_temp.reject_command(pg_temp.command('actual_cost','{"transactionId":"proof-1","mdr":0,"other":0,"reference":"fixture"}'),'22023');
 cmd:=pg_temp.command('payout','{"transactions":["proof-1"],"reference":"Report 001","reportedOn":"2026-09-26","bankName":"Bank Uji","accountLast4":"1234","fee":100,"adjustment":-50}');
 perform pg_temp.reject_command(cmd,'40001');
 perform apply_finance_admin(pg_temp.command('actual_cost','{"transactionId":"proof-1","mdr":70,"other":5,"refunded":1000,"reference":"Statement 001"}'),'owner');
 if (select estimated_fee from qris_costs where transaction_id='proof-1')<>75 then raise exception 'actual replaced estimate'; end if;
 cmd:=jsonb_set(cmd,'{revision}',to_jsonb((select revision from finance_admin_state where id=1)));
 target_payout:=cmd->>'id';perform apply_finance_admin(cmd,'owner');
 if (select expected_net from qris_payouts where id=target_payout)<>8775 or (select received_amount from qris_payouts where id=target_payout) is not null then raise exception 'payout net/receipt incorrect'; end if;
 perform pg_temp.reject_command(pg_temp.command('payout',cmd-'id'-'revision'-'action'-'reason'),'40001');
 perform pg_temp.reject_command(pg_temp.command('actual_cost','{"transactionId":"proof-1","mdr":0,"other":0,"refunded":0,"reference":"changed"}'),'40001');
 perform pg_temp.reject_command(pg_temp.command('bank_receipt',jsonb_build_object('payoutId',target_payout,'receivedOn','2026-09-25','amount',8700,'reference','Bank 01')),'22023');
 perform apply_finance_admin(pg_temp.command('bank_receipt',jsonb_build_object('payoutId',target_payout,'receivedOn','2026-09-26','amount',8700,'reference','Bank 01')),'owner');
 snap:=finance_dashboard('2026-09-26','2026-09-26');
 if (snap#>>'{summary,gross}')::bigint<>30000 or (snap#>>'{summary,receivedBank}')::bigint<>8700 or (snap#>>'{summary,payoutNet}')::bigint<>8775 or (snap#>>'{summary,actualFees}')::bigint<>75 or (snap#>>'{summary,actualKnown}')::integer<>1 then raise exception 'double-counting/fee/bank totals'; end if;
 if jsonb_array_length(snap->'audit')<>(select count(*) from finance_admin_events) or snap#>'{audit,0,before_value}' is null then raise exception 'audit details missing'; end if;
 -- A later refund callback retains the original paid period and does not duplicate a notice.
 select count(*) into count_before from payment_notifications;
 perform record_provider_payment('{"transactionId":"proof-1","merchantId":"merchant","orderId":"qr-1","amount":10000,"status":"refund","paidAt":"2026-09-27T11:00:00Z"}');
 if (select paid_at from provider_payments where transaction_id='proof-1')<>'2026-09-25T18:00:00Z'::timestamptz or (select count(*) from payment_notifications)<>count_before then raise exception 'refund shifted paid day/notice'; end if;
 perform apply_finance_admin(pg_temp.command('void_payout',jsonb_build_object('payoutId',target_payout)),'owner');
 if exists(select 1 from qris_payout_items where payout_id=target_payout) then raise exception 'allocation not released'; end if;
 snap:=finance_dashboard('2026-09-26','2026-09-26');
 if (snap#>>'{summary,receivedBank}')::bigint<>0 or (snap#>>'{summary,payoutNet}')::bigint<>0 or not exists(select 1 from finance_admin_events where action='void_payout' and jsonb_array_length(before_value->'transactions')=1) then raise exception 'void totals/history incorrect'; end if;
 perform apply_finance_admin(pg_temp.command('actual_cost','{"transactionId":"proof-1","mdr":0,"other":0,"refunded":10000,"reference":"Correction 002"}'),'owner');
 -- Force a late failure: payout and allocation must both roll back with revision unchanged.
 select revision into rev_before from finance_admin_state where id=1;
 begin
   update finance_admin_events set reason='edit';raise exception 'history editable' using errcode='22000';
 exception when raise_exception then null; end;
 begin
   delete from qris_fee_profiles;raise exception 'profile removable' using errcode='22000';
 exception when raise_exception then null; end;
 if (select revision from finance_admin_state where id=1)<>rev_before then raise exception 'failed action advanced revision'; end if;
end $$;
-- A storage failure at the final journal insert rolls back projections and revision.
create function pg_temp.fail_admin_journal() returns trigger language plpgsql as $$
begin if new.reason='Failure fixture' then raise check_violation using message='fixture storage full'; end if; return new; end $$;
create trigger fail_admin_journal before insert on finance_admin_events for each row execute function pg_temp.fail_admin_journal();
do $$ declare rev bigint; cmd jsonb; begin
 select revision into rev from finance_admin_state;
 cmd:=pg_temp.command('actual_cost','{"transactionId":"proof-2","mdr":1,"other":0,"refunded":0,"reference":"fixture"}')||'{"reason":"Failure fixture"}';
 perform pg_temp.reject_command(cmd,'23514');
 if exists(select 1 from qris_costs where transaction_id='proof-2') or (select revision from finance_admin_state)<>rev then raise exception 'late journal failure did not roll back'; end if;
 perform apply_finance_admin(pg_temp.command('actual_cost','{"transactionId":"proof-2","mdr":0,"other":0,"refunded":0,"reference":"fixture"}'),'owner');
 cmd:=pg_temp.command('payout','{"transactions":["proof-2"],"reference":"Report failed","reportedOn":"2026-09-26","bankName":"Bank Uji","accountLast4":"1234","fee":0,"adjustment":0}')||'{"reason":"Failure fixture"}';
 select revision into rev from finance_admin_state;
 perform pg_temp.reject_command(cmd,'23514');
 if exists(select 1 from qris_payouts where id=cmd->>'id') or exists(select 1 from qris_payout_items where transaction_id='proof-2') or (select revision from finance_admin_state)<>rev then raise exception 'payout partially committed'; end if;
 perform pg_temp.reject_command(cmd||'{"reason":"Check date","reportedOn":"2026-09-25"}','22023');
 perform pg_temp.reject_command(cmd||'{"reason":"Check duplicates","transactions":["proof-2","proof-2"]}','22023');
end $$;
-- All aggregates use the entire result set; detail pages are only 50 records.
insert into provider_payments(transaction_id,merchant_id,order_id,amount,status,paid_at)
select 'page-'||v,'page-merchant','qr-page-'||v,1,'settlement','2026-09-26T10:00:00Z' from generate_series(1,60) v;
do $$ declare snap jsonb;begin
 snap:=finance_dashboard('2026-09-26','2026-09-26',0,1,0);
 if (snap#>>'{counts,providers}')::integer<>64 or jsonb_array_length(snap->'providers')<>14 or (snap#>>'{summary,providerGross}')::bigint<>41060 then raise exception 'pagination truncated totals'; end if;
 if has_table_privilege('anon','qris_payouts','SELECT') or has_table_privilege('authenticated','qris_costs','SELECT') or has_function_privilege('anon','apply_finance_admin(jsonb,text)','EXECUTE') or has_function_privilege('authenticated','finance_dashboard(date,date,integer,integer,integer)','EXECUTE') or has_table_privilege('service_role','finance_admin_events','UPDATE') then raise exception 'finance exposure'; end if;
end $$;
-- Execute real RPCs as the backend role, not just postgres.
set local role service_role;
select finance_dashboard('2026-09-26','2026-09-26');
select apply_finance_admin(jsonb_build_object('id',md5('backend-command'),'revision',(select revision from finance_admin_state),'action','unmatch','transactionId','difference','reason','Backend role test'),'owner');
reset role;
rollback;
