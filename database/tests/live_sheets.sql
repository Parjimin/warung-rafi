begin;
do $$
declare workbook text:='fixture_live_workbook_123456';job jsonb;verified timestamptz;today date:=(now() at time zone 'Asia/Jakarta')::date;
begin
 job:=claim_live_sheets(workbook,md5('first'));
 if (job->>'claimed')::boolean is distinct from true or (job#>>'{source,from}')::date<>date_trunc('month',today)::date or (job#>>'{source,to}')::date<>today then raise exception 'wrong live period';end if;
 if (claim_live_sheets(workbook,md5('second'))->>'claimed')::boolean then raise exception 'overlapping worker';end if;
 begin perform finish_live_sheets(workbook,md5('second'),repeat('a',64));raise exception 'wrong token accepted';exception when serialization_failure then null;end;
 perform finish_live_sheets(workbook,md5('first'),repeat('a',64));
 select verified_at into verified from live_sheets_state where live_sheets_state.target=workbook;
 if verified is null or (claim_live_sheets(workbook,md5('early'))->>'claimed')::boolean then raise exception 'missing verification or throttle';end if;
 update live_sheets_state set next_attempt_at=now()-interval '1 second' where live_sheets_state.target=workbook;
 job:=claim_live_sheets(workbook,md5('unchanged'));perform release_live_sheets(workbook,md5('unchanged'));
 if (select verified_at from live_sheets_state where live_sheets_state.target=workbook)<>verified then raise exception 'skip claimed fresh verification';end if;
 update live_sheets_state set next_attempt_at=now()-interval '1 second' where live_sheets_state.target=workbook;
 job:=claim_live_sheets(workbook,md5('failed'));perform finish_live_sheets(workbook,md5('failed'),null,'quota');
 if (select error_code from live_sheets_state where live_sheets_state.target=workbook)<>'quota' or (claim_live_sheets(workbook,md5('retry'))->>'claimed')::boolean then raise exception 'backoff missing';end if;
 update live_sheets_state set next_attempt_at=now()-interval '1 second' where live_sheets_state.target=workbook;
 job:=claim_live_sheets(workbook,md5('crash'));
 update live_sheets_state set lease_until=now()-interval '1 second',next_attempt_at=now()-interval '1 second' where live_sheets_state.target=workbook;
 job:=claim_live_sheets(workbook,md5('replacement'));
 if (job->>'claimed')::boolean is distinct from true then raise exception 'crash recovery';end if;
 begin perform finish_live_sheets(workbook,md5('crash'),repeat('b',64));raise exception 'stale token accepted';exception when serialization_failure then null;end;
 if has_table_privilege('anon','public.live_sheets_state','SELECT') or has_function_privilege('authenticated','public.claim_live_sheets(text,text)','EXECUTE') then raise exception 'public permission leak';end if;
end $$;
rollback;
