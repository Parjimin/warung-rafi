\set ON_ERROR_STOP on
begin;
do $$
declare bucket text:=repeat('a',64); result jsonb; second jsonb; state text; i integer;
begin
 for i in 1..10 loop if not reserve_login_attempt(bucket) then raise exception 'early account throttle';end if;end loop;
 if reserve_login_attempt(bucket) then raise exception 'account limit missing';end if;
 if not reserve_login_attempt(repeat('b',64)) then raise exception 'independent account blocked';end if;
 update login_windows set attempts=120 where key='global';
 if reserve_login_attempt(repeat('c',64)) then raise exception 'global limit missing';end if;
 update login_windows set started_at=now()-interval '11 minutes';
 if not reserve_login_attempt(bucket) then raise exception 'expired buckets not released';end if;
 if exists(select 1 from login_windows where started_at<now()-interval '10 minutes') then raise exception 'expired bucket retained';end if;
 if has_table_privilege('anon','login_windows','SELECT') or has_table_privilege('authenticated','login_windows','INSERT') or has_function_privilege('authenticated','reserve_login_attempt(text)','EXECUTE') or has_function_privilege('anon','device_recovery_page(text,text,text)','EXECUTE') then raise exception 'public operations access';end if;
 if not has_function_privilege('service_role','device_setup()','EXECUTE') or (device_setup()->>'schema')::integer<>6 then raise exception 'setup not available';end if;
 result:=device_recovery_page('recovery-fixture');
 if result->'orders'<>'[]'::jsonb or result->'finance'<>'null'::jsonb then raise exception 'empty device recovery';end if;
 for i in 1..51 loop
  insert into order_snapshots(device_id,id,version,status,payload) values('recovery-fixture',lpad(to_hex(i),32,'0'),1,0,jsonb_build_object('order',jsonb_build_object('id',lpad(to_hex(i),32,'0'),'version',1,'status',0)));
 end loop;
 insert into order_snapshots(device_id,id,version,status,payload) values('another-device',repeat('f',32),1,0,'{}');
 result:=device_recovery_page('recovery-fixture');state:=result->>'state';
 if jsonb_array_length(result->'orders')<>50 or result->>'next' is null then raise exception 'first page missing';end if;
 second:=device_recovery_page('recovery-fixture',result->>'next',state);
 if jsonb_array_length(second->'orders')<>1 or second->>'next' is not null or second->>'deviceId'<>'recovery-fixture' then raise exception 'page boundary/device filter failed';end if;
 update order_snapshots set version=2 where device_id='recovery-fixture' and id=lpad('1',32,'0');
 begin perform device_recovery_page('recovery-fixture','',state);raise exception 'stale source accepted';exception when serialization_failure then null;end;
 result:=device_recovery_page('recovery-fixture');
 update order_snapshots set payload='{"changed":true}' where device_id='recovery-fixture' and id=lpad('2',32,'0');
 begin perform device_recovery_page('recovery-fixture','',result->>'state');raise exception 'changed content accepted';exception when serialization_failure then null;end;
end $$;
rollback;
