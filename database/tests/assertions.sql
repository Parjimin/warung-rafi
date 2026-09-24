\set ON_ERROR_STOP on
begin;
do $$
declare event jsonb; payment jsonb; count_rows integer;
begin
  event:='{"id":"a:1","aggregateId":"a","version":1,"payload":{"order":{"id":"a","version":1,"status":0}}}';
  perform ingest_device_events('test',jsonb_build_array(event));
  perform ingest_device_events('test',jsonb_build_array(event));
  if (select count(*) from device_events)<>1 then raise exception 'duplicate sync'; end if;
  begin
    perform ingest_device_events('test',jsonb_build_array(jsonb_set(event,'{payload,order,status}','1')));
    raise exception 'must reject reused event id';
  exception when unique_violation then null; end;
  begin
    perform ingest_device_events('test','[{"id":"b:1","aggregateId":"b","version":1,"payload":{"order":{"id":"b","version":1,"status":0}}},{"id":"a:3","aggregateId":"a","version":3,"payload":{"order":{"id":"a","version":3,"status":0}}}]');
    raise exception 'must reject gap';
  exception when serialization_failure then null; end;
  if exists(select 1 from order_snapshots where id='b') then raise exception 'batch not atomic'; end if;
  payment:='{"transactionId":"t-1","merchantId":"merchant","orderId":"static-order","amount":22500,"status":"settlement","paidAt":"2026-09-22T11:44:00Z"}';
  perform record_provider_payment(payment); perform record_provider_payment(payment);
  if (select count(*) from payment_notifications)<>1 then raise exception 'duplicate notification'; end if;
  perform record_provider_payment(jsonb_set(payment,'{status}','"refund"'));
  perform record_provider_payment(payment);
  if (select status from provider_payments where transaction_id='t-1')<>'refund' then raise exception 'refund regressed'; end if;
  perform save_catalog_draft(0,'[{"id":"p","name":"Nasi","category":"Nasi","price":5000,"available":true,"imageUrl":null}]','test');
  perform publish_catalog(1,'test');
  begin
    perform save_catalog_draft(0,'[]','test'); raise exception 'must reject stale catalog';
  exception when serialization_failure then null; end;
  if (select published_version from catalog_state where id=1)<>1 then raise exception 'publish failed'; end if;
  if has_function_privilege('anon','public.record_provider_payment(jsonb)','EXECUTE') then raise exception 'anon can record payment'; end if;
  if has_table_privilege('authenticated','public.order_snapshots','SELECT') then raise exception 'public sales exposure'; end if;
end $$;
rollback;
