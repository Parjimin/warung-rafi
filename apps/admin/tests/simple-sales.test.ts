import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {simpleSalesReport} from '../lib/simple-sales.ts';
import type {ReportSource} from '../lib/reports.ts';
const source=()=>JSON.parse(readFileSync(new URL('../../../database/tests/fixtures/report-contract.json',import.meta.url),'utf8')) as ReportSource;
test('two tabs retain item rows, payment methods and completed refunds without double-counting orders',()=>{
 const s=source(),r=simpleSalesReport('a'.repeat(32),s),[history,summary]=r.tables;
 assert.deepEqual(r.tables.map(t=>t.name),['Riwayat Penjualan','Rekap Penjualan']);
 assert.equal(history.rows.filter(r=>r[7]==='Penjualan').length,s.orders.filter(o=>o.status===2).reduce((n,o)=>n+o.payload.order.lines.length,0));
 assert.equal(history.rows.reduce((n,r)=>n+Number(r[5]),0),9000);
 assert.equal(summary.rows.find(r=>r[1]==='Penjualan sebelum refund')![2],4);
 assert.equal(summary.rows.find(r=>r[1]==='Penjualan setelah refund')![3],9000);
 assert.ok(history.rows.some(r=>r[6]==='QRIS'));
 for(const row of history.rows.filter(r=>r[7]==='Refund'&&r[8]!=='Berhasil'))assert.equal(row[5],0);
});
test('refund of an old order reduces this period without inventing product sales',()=>{
 const s=source(),refund=s.refunds.find(r=>r.state===1&&r.completed_in_period)!;
 s.orders=[];s.refunds=[refund];s.sessions=[];s.events=[];
 const r=simpleSalesReport('a'.repeat(32),s);
 assert.equal(r.tables[0].rows[0][5],-refund.amount);
 assert.equal(r.tables[0].rows[0][3],null);
 assert.equal(r.tables[1].rows.filter(r=>r[0]==='Menu terjual').length,0);
});
test('pending and failed refunds never reduce revenue; cash movements stay outside sales',()=>{
 const s=source();s.refunds=s.refunds.map(r=>({...r,state:0}));
 const r=simpleSalesReport('a'.repeat(32),s);
 assert.equal(r.tables[1].rows.find(r=>r[1]==='Penjualan setelah refund')![3],46500);
 assert.ok(r.tables[1].rows.some(r=>r[0]==='Kas keluar di luar refund'&&Number(r[3])<0));
});
