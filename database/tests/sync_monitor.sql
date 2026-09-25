\set ON_ERROR_STOP on
begin;
do $$
declare result jsonb; good jsonb:='{"id":"monitor-a:1","aggregateId":"monitor-a","version":1,"payload":{"order":{"id":"monitor-a","version":1,"status":0}}}';
begin
  perform report_device_status('monitor',12,3);
  if (select pending_count from device_sync_status where device_id='monitor')<>12 then raise exception 'missing backlog'; end if;
  result:=receive_device_batch('monitor',jsonb_build_array(good));
  if result->>'conflict'<>'false' or jsonb_array_length(result->'accepted')<>1 then raise exception 'missing ack'; end if;
  perform receive_device_batch('monitor',jsonb_build_array(good));
  if (select count(*) from device_events where device_id='monitor')<>1 then raise exception 'retry duplicate'; end if;
  result:=receive_device_batch('monitor','[{"id":"monitor-b:1","aggregateId":"monitor-b","version":1,"payload":{"order":{"id":"monitor-b","version":1,"status":0}}},{"id":"monitor-a:3","aggregateId":"monitor-a","version":3,"payload":{"order":{"id":"monitor-a","version":3,"status":0}}}]');
  if result->>'conflict'<>'true' or result->'accepted'<>'[]'::jsonb then raise exception 'conflict acknowledged'; end if;
  if exists(select 1 from device_events where aggregate_id='monitor-b') then raise exception 'partial batch committed'; end if;
  perform report_device_status('monitor',0,4);
  if (select conflict_at is null from device_sync_status where device_id='monitor') then raise exception 'heartbeat erased conflict'; end if;
  if (select jsonb_array_length(conflict_events) from device_sync_status where device_id='monitor')<>2 then raise exception 'conflict details lost'; end if;
  perform receive_device_batch('monitor',jsonb_build_array(good));
  if (select conflict_at is not null from device_sync_status where device_id='monitor') then raise exception 'successful retry did not clear conflict'; end if;
  result:=receive_device_batch('monitor',jsonb_build_array(jsonb_set(good,'{payload,order,status}','1')));
  if result->>'conflict'<>'true' then raise exception 'reused id accepted'; end if;
  if has_table_privilege('authenticated','public.device_sync_status','SELECT') or has_function_privilege('anon','public.report_device_status(text,integer,bigint)','EXECUTE') then raise exception 'public monitoring access'; end if;
end $$;
rollback;
