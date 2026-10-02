import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {runDirectSheets,validateDirectConfig} from '../lib/direct-sheets.ts';
import {simpleSalesReport} from '../lib/simple-sales.ts';
const source=JSON.parse(readFileSync(new URL('../../../database/tests/fixtures/report-contract.json',import.meta.url),'utf8'));
const config={supabaseUrl:'https://fixture.supabase.co',serviceKey:'sb_secret_fixture',google:{target:'fixture_workbook_1234567890',email:'fixture@fixture.iam.gserviceaccount.com',privateKey:'invalid'}};
const now=new Date('2026-09-25T15:00:00Z');
test('desktop reads Supabase directly, uses WIB month and skips unchanged Sheets without contacting Vercel',async()=>{
 const hash=createHash('sha256').update(JSON.stringify([config.supabaseUrl,config.google.target,simpleSalesReport('0'.repeat(32),source).tables])).digest('hex');
 let calls=0;
 const request:typeof fetch=async(url,init)=>{calls++;assert.equal(String(url),'https://fixture.supabase.co/rest/v1/rpc/report_source');assert.equal(init!.redirect,'error');assert.equal((init!.headers as Record<string,string>).apikey,config.serviceKey);assert.equal((init!.headers as Record<string,string>).Authorization,undefined);assert.deepEqual(JSON.parse(String(init!.body)),{p_filter:{mode:'calendar',from:'2026-09-01',to:'2026-09-25'}});return Response.json(source);};
 assert.equal((await runDirectSheets({config,hash,verifiedAt:now.toISOString()},request,now)).state,'unchanged');assert.equal(calls,1);
});
test('database outages and denied credentials never produce verified status',async()=>{
 for(const status of [401,403,503])await assert.rejects(runDirectSheets({config},async()=>Response.json({}, {status}),now));
 for(const url of ['http://fixture.supabase.co','https://user:pass@fixture.supabase.co','https://fixture.supabase.co/path'])assert.throws(()=>validateDirectConfig({...config,supabaseUrl:url}));
});
test('changed data is written and verified through Google directly with exactly two tabs',async()=>{
 const {generateKeyPairSync}=await import('node:crypto');
 const pair=generateKeyPairSync('rsa',{modulusLength:2048});
 const current={...config,google:{...config.google,privateKey:pair.privateKey.export({type:'pkcs8',format:'pem'}).toString()}};
 const sheets:any[]=[];const hosts=new Set<string>();
 const request:typeof fetch=async(url,init)=>{
  const u=new URL(String(url));hosts.add(u.hostname);
  if(u.hostname==='fixture.supabase.co')return Response.json(source);
  if(u.hostname==='oauth2.googleapis.com')return Response.json({access_token:'fixture-token'});
  assert.equal(u.hostname,'sheets.googleapis.com');
  if(init?.method==='POST'){
   for(const r of JSON.parse(String(init.body)).requests){
    if(r.addSheet)sheets.push({properties:r.addSheet.properties,data:[{rowData:[]}]});
    if(r.updateCells){const sheet=sheets.find(s=>s.properties.sheetId===r.updateCells.range.sheetId);r.updateCells.rows.forEach((row:unknown,i:number)=>sheet.data[0].rowData[r.updateCells.range.startRowIndex+i]=row);}
   }
   return Response.json({});
  }
  return Response.json({sheets});
 };
 const result=await runDirectSheets({config:current},request,now);
 assert.equal(result.state,'verified');assert.deepEqual(sheets.map(s=>s.properties.title),['Riwayat Penjualan','Rekap Penjualan']);
 assert.deepEqual([...hosts],['fixture.supabase.co','oauth2.googleapis.com','sheets.googleapis.com']);
});
