\set ON_ERROR_STOP on
begin;
-- A failed notification insert must also roll back the provider state.
create function pg_temp.reject_fixture_notice() returns trigger language plpgsql as $$
begin
  if new.transaction_id='fixture-fail' then raise check_violation using message='fixture storage failure'; end if;
  return new;
end $$;
create trigger reject_fixture_notice before insert on payment_notifications for each row execute function pg_temp.reject_fixture_notice();
do $$
declare payment jsonb; second jsonb; original_sequence bigint; rejected boolean;
begin
  payment:='{"transactionId":"fixture-a","merchantId":"fixture","orderId":"QRIS-a","amount":22500,"status":"pending","paidAt":"2026-09-25T11:44:00Z"}';
  perform record_provider_payment(payment);
  if exists(select 1 from payment_notifications) then raise exception 'pending produced notification'; end if;
  payment:=jsonb_set(payment,'{status}','"settlement"');
  perform record_provider_payment(payment);
  select sequence into original_sequence from payment_notifications where transaction_id='fixture-a';
  perform record_provider_payment(payment);
  perform record_provider_payment(jsonb_set(payment,'{status}','"pending"'));
  if (select count(*) from payment_notifications)<>1 or (select status from provider_payments where transaction_id='fixture-a')<>'settlement' then raise exception 'retry/pending regressed settlement'; end if;
  second:=jsonb_set(jsonb_set(payment,'{transactionId}','"fixture-b"'),'{orderId}','"QRIS-b"');
  perform record_provider_payment(second);
  if (select count(*) from payment_notifications where amount=22500)<>2 then raise exception 'same amount collapsed different payments'; end if;
  if (select sequence from payment_notifications where transaction_id='fixture-b')<=original_sequence then raise exception 'cursor order'; end if;
  rejected:=false;
  begin perform record_provider_payment(jsonb_set(payment,'{amount}','23000'));
  exception when raise_exception then rejected:=true; end;
  if not rejected then raise exception 'changed identity accepted'; end if;
  perform record_provider_payment(jsonb_set(payment,'{status}','"partial_refund"'));
  perform record_provider_payment(payment);
  if (select status from provider_payments where transaction_id='fixture-a')<>'partial_refund' then raise exception 'partial refund regressed'; end if;
  perform record_provider_payment(jsonb_set(payment,'{status}','"refund"'));
  perform record_provider_payment(payment);
  if (select status from provider_payments where transaction_id='fixture-a')<>'refund' then raise exception 'refund regressed'; end if;
  if (select count(*) from payment_notifications)<>2 then raise exception 'refund produced second notification'; end if;
  rejected:=false;
  begin perform record_provider_payment(jsonb_set(payment,'{transactionId}','"fixture-fail"'));
  exception when check_violation then rejected:=true; end;
  if not rejected or exists(select 1 from provider_payments where transaction_id='fixture-fail') then raise exception 'provider and notification insert not atomic'; end if;
  if has_table_privilege('anon','public.payment_notifications','SELECT') or has_table_privilege('authenticated','public.provider_payments','SELECT') then raise exception 'public payment exposure'; end if;
  if not has_function_privilege('service_role','public.record_provider_payment(jsonb)','EXECUTE') then raise exception 'backend cannot persist'; end if;
end $$;
rollback;
