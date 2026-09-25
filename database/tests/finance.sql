\set ON_ERROR_STOP on
begin;
create temporary table finance_fixture(data jsonb);
\copy finance_fixture from 'database/tests/fixtures/finance-contract.json' with (format csv, delimiter E'\t', quote E'\x01')
-- The fixture is emitted by the real C# LocalStore, using its web JSON serializer.
insert into order_snapshots(device_id,id,version,status,payload)
select 'finance-fixture',s->'order'->>'id',(s->'order'->>'version')::bigint,2,s
from finance_fixture cross join lateral jsonb_array_elements(data->'sales') s;
do $$
declare batch jsonb; accepted jsonb; first_event jsonb; bad jsonb; expected_count integer; changed integer; rejected boolean;
begin
  select data->'events' into batch from finance_fixture;expected_count:=jsonb_array_length(batch);first_event:=batch->0;
  accepted:=receive_finance_batch('finance-fixture',batch);
  if jsonb_array_length(accepted)<>expected_count then raise exception 'missing finance acknowledgement'; end if;
  perform receive_finance_batch('finance-fixture',batch);
  if (select count(*) from finance_events where device_id='finance-fixture')<>expected_count then raise exception 'replayed journal duplicated'; end if;
  if (select expected from cash_sessions where device_id='finance-fixture' and not closed)<>14000 then raise exception 'drawer replay mismatch'; end if;
  if (select (payload->>'countedCash')::bigint from cash_sessions where device_id='finance-fixture' and closed)<>112000 then raise exception 'closing count lost'; end if;
  if (select sum(amount) from refunds where device_id='finance-fixture' and state=1)<>37500 then raise exception 'refund totals mismatch'; end if;
  begin
    perform receive_finance_batch('finance-fixture',jsonb_build_array(jsonb_set(first_event,'{amount}','0')));
    raise exception 'reused identity accepted';
  exception when serialization_failure then null; end;
  -- Batch begins valid but its second event skips a sequence: no opening may remain.
  begin
    perform receive_finance_batch('finance-rollback',jsonb_build_array(first_event,jsonb_set(batch->1,'{sequence}','3')));
    raise exception 'gap accepted';
  exception when serialization_failure then null; end;
  if exists(select 1 from cash_sessions where device_id='finance-rollback') or exists(select 1 from finance_events where device_id='finance-rollback') then raise exception 'partial finance batch persisted'; end if;
  -- Opening + sale without an order snapshot must also roll back, ready for retry.
  begin
    perform receive_finance_batch('finance-orders-first',jsonb_build_array(first_event,batch->1));
    raise exception 'missing sale accepted';
  exception when serialization_failure then null; end;
  if exists(select 1 from cash_sessions where device_id='finance-orders-first') then raise exception 'missing order partial write'; end if;
  insert into order_snapshots(device_id,id,version,status,payload)
    select 'finance-invalid',id,version,status,payload from order_snapshots where device_id='finance-fixture';
  for changed in 1..6 loop
    bad:=case changed
      when 1 then jsonb_set(batch,'{0,details,openingCash}','0')
      when 2 then jsonb_set(batch,'{3,cashDelta}','0')
      when 3 then jsonb_set(batch,'{6,amount}','6000')
      when 4 then jsonb_set(jsonb_set(batch,'{9,amount}','23000'),'{9,details,amount}','23000')
      when 5 then jsonb_set(batch,'{11,details,expectedAtClose}','0')
      else jsonb_set(batch,'{14,cashDelta}','0') end;
    rejected:=false;
    begin perform receive_finance_batch('finance-invalid',bad);
    exception when others then rejected:=true; end;
    if not rejected or exists(select 1 from finance_events where device_id='finance-invalid') or exists(select 1 from cash_sessions where device_id='finance-invalid') or exists(select 1 from refunds where device_id='finance-invalid') then raise exception 'forged finance event % did not roll back',changed; end if;
  end loop;
  begin
    update finance_events set amount=0 where device_id='finance-fixture';
    raise exception 'update allowed' using errcode='22000';
  exception when raise_exception then null; end;
  begin
    delete from finance_events where device_id='finance-fixture';
    raise exception 'delete allowed' using errcode='22000';
  exception when raise_exception then null; end;
  if has_table_privilege('authenticated','public.finance_events','SELECT') or has_table_privilege('service_role','public.finance_events','UPDATE') or has_function_privilege('anon','public.receive_finance_batch(text,jsonb)','EXECUTE') then raise exception 'public journal access'; end if;
end $$;
rollback;
