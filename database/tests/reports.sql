\set ON_ERROR_STOP on
begin;
create temporary table report_fixture(data jsonb);
\copy report_fixture from 'database/tests/fixtures/finance-contract.json' with (format csv, delimiter E'\t', quote E'\x01')
insert into order_snapshots(device_id,id,version,status,payload)
select 'report-fixture',s->'order'->>'id',(s->'order'->>'version')::bigint,2,s from report_fixture cross join lateral jsonb_array_elements(data->'sales') s;
select receive_finance_batch('report-fixture',data->'events') from report_fixture;
create function pg_temp.expect_report_failure(sql text,code text) returns void language plpgsql as $$
begin
 begin execute sql; exception when others then if sqlstate=code then return;end if;raise;end;
 raise exception 'invalid operation accepted: %',sql;
end $$;
do $$
declare source jsonb; session_id text; before_data jsonb; job jsonb; active text; id1 text:=md5('report-one');id2 text:=md5('report-two');
 filter jsonb:='{"mode":"calendar","from":"2026-09-25","to":"2026-09-25"}'; target text:='fixture_workbook_1234567890';
begin
 source:=report_source(filter);
 if exists(select 1 from jsonb_array_elements(source->'orders') o where (o#>'{payload,order}') ? 'customerLabel') then raise exception 'customer label entered report snapshot';end if;
 if jsonb_array_length(source->'orders')<>4 or jsonb_array_length(source->'events')<>17 or jsonb_array_length(source->'sessions')<>2 then raise exception 'report contract counts';end if;
 if (select sum((o#>>'{payload,payment,amount}')::bigint) from jsonb_array_elements(source->'orders') o)<>46500 then raise exception 'report gross';end if;
 if (select sum((r->>'amount')::bigint) from jsonb_array_elements(source->'refunds') r where r->>'state'='1' and (r->>'completed_in_period')::boolean)<>37500 then raise exception 'refund period';end if;
 select id into session_id from cash_sessions where device_id='report-fixture' and closed;
 source:=report_source(jsonb_build_object('mode','session','deviceId','report-fixture','sessionId',session_id));
 if jsonb_array_length(source->'orders')<>2 or (source#>>'{sessions,0,expected}')::bigint<>112500 then raise exception 'session scope';end if;
 -- Move close across midnight; calendar and session still have distinct scope.
 update cash_sessions set payload=jsonb_set(payload,'{closedAt}','"2026-09-26T01:00:00Z"') where device_id='report-fixture' and id=session_id;
 source:=report_source(jsonb_build_object('mode','session','deviceId','report-fixture','sessionId',session_id));
 if source->>'from'=source->>'to' or jsonb_array_length(source->'orders')<>2 then raise exception 'overnight session split';end if;
 insert into qris_payouts(id,merchant_id,reference,reported_on,bank_name,account_last4,gross,transaction_fees,refunds,fee,adjustment,expected_net,received_amount,received_on,bank_reference,reason,actor)
 values(md5('cross-period-payout'),'fixture','Earlier payout','2026-09-24','Bank Uji','1234',10000,100,0,0,0,9900,9800,'2026-09-25','Bank fixture','Cross-period fixture','owner');
 source:=report_source(filter);
 if jsonb_array_length(source->'payouts')<>1 or (source#>>'{payouts,0,reported_in_period}')::boolean or (source#>>'{payouts,0,received_in_period}')::boolean is distinct from true then raise exception 'bank receipt across payout period lost';end if;
 perform create_report(id1,filter,'owner');select snapshot into before_data from report_jobs where id=id1;
 update device_sync_status set pending_count=999;
 perform create_report(id1,filter,'owner');
 if (select snapshot from report_jobs where id=id1)<>before_data then raise exception 'retry recaptured source';end if;
 perform pg_temp.expect_report_failure(format('select create_report(%L,%L::jsonb,%L)',id1,filter,'forged'),'40001');
 perform pg_temp.expect_report_failure(format('update report_jobs set snapshot=%L::jsonb where id=%L','{}',id1),'P0001');
 perform create_report(id2,filter,'owner');perform queue_report(id1,target);perform queue_report(id1,target);perform queue_report(id2,target);
 job:=claim_report(md5('worker-one'),id1);active:=job->>'id';
 if active<>id1 or job->>'state'<>'running' or claim_report(md5('worker-two'),id2) is not null then raise exception 'concurrent workbook claim';end if;
 perform pg_temp.expect_report_failure(format('select finish_report(%L,%L,true,null,%L)',id1,md5('wrong'),repeat('a',64)),'40001');
 update report_jobs set lease_until=now()-interval '1 second' where id=id1;
 job:=claim_report(md5('replacement'),id1);
 if (job->>'attempts')::integer<>2 or (select outcome from report_attempts where job_id=id1 and attempt=1)<>'lease_expired' then raise exception 'lease recovery';end if;
 perform pg_temp.expect_report_failure(format('select finish_report(%L,%L,true,null,%L)',id1,md5('worker-one'),repeat('a',64)),'40001');
 perform finish_report(id1,md5('replacement'),false,'quota',null,true);
 if (select next_attempt_at from report_jobs where id=id1)<=now() or claim_report(md5('too-early'),id1) is not null then raise exception 'backoff missing';end if;
 perform queue_report(id1,target);job:=claim_report(md5('third'),id1);perform finish_report(id1,md5('third'),true,null,repeat('a',64));
 if (select state from report_jobs where id=id1)<>'succeeded' or (select verified_at from report_jobs where id=id1) is null then raise exception 'verification missing';end if;
 perform pg_temp.expect_report_failure(format('select queue_report(%L,%L)',id1,'other_workbook_1234567890'),'40001');
 job:=claim_report(md5('second-job'),id2);perform finish_report(id2,md5('second-job'),false,'permission',null,false);
 if (select next_attempt_at from report_jobs where id=id2) is not null then raise exception 'permission must require intervention';end if;
 if has_table_privilege('authenticated','report_jobs','SELECT') or has_function_privilege('anon','report_source(jsonb)','EXECUTE') or has_table_privilege('service_role','report_jobs','DELETE') then raise exception 'report exposure';end if;
end $$;
set local role service_role;
select create_report(md5('backend-report'),'{"mode":"calendar","from":"2026-09-25","to":"2026-09-25"}','owner');
reset role;
rollback;
