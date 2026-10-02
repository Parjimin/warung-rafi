import { buildReport, wib, type Report, type ReportSource, type Cell } from "./reports.ts";

export function simpleSalesReport(id: string, s: ReportSource): Report {
 const report = buildReport(id,s); // Keep all accounting validation before making the simpler view.
 const sales=s.orders.filter(o=>o.status===2), refunds=s.refunds.filter(r=>r.state===1&&r.completed_in_period);
 const history: Cell[][]=[];
 for(const o of sales)for(const l of o.payload.order.lines)history.push([wib(o.payload.payment!.paidAt),o.payload.order.number,l.name,l.quantity,l.unitPrice,l.subtotal,o.payload.payment!.method===0?"Tunai":"QRIS","Penjualan","Lunas",""]);
 for(const r of s.refunds){
  if(r.state===1&&!r.completed_in_period)continue;
  const o=s.orders.find(o=>o.device_id===r.device_id&&o.id===r.order_id);
  history.push([wib(r.state===1?r.payload.completedAt:r.payload.requestedAt),o?.payload.order.number??r.order_id,"Pengembalian uang",null,null,r.state===1?-r.amount:0,r.payload.channel===0?"Tunai":"Transfer/provider","Refund",["Menunggu","Berhasil","Gagal"][r.state],`${r.payload.reason} · Nominal: Rp${r.amount}${r.payload.reference?" · "+r.payload.reference:""}${r.payload.failureReason?" · "+r.payload.failureReason:""}`]);
 }
 for(const o of s.orders.filter(o=>o.status===3))history.push([wib(o.payload.order.updatedAt),o.payload.order.number,"Pesanan dibatalkan",null,null,0,"—","Pembatalan","Batal",o.payload.order.cancellationReason??""]);
 history.sort((a,b)=>String(a[0]).localeCompare(String(b[0]))||String(a[1]).localeCompare(String(b[1])));
 const rows: Cell[][]=[];
 const add=(section:string,label:string,count:number|null,amount:number|null)=>rows.push([section,label,count,amount]);
 add("Periode",`${s.from} s.d. ${s.to} (WIB)`,null,null);
 add("Ringkasan","Penjualan sebelum refund",sales.length,report.totals.gross);
 add("Ringkasan","Refund berhasil",refunds.length,-report.totals.refunds);
 add("Ringkasan","Penjualan setelah refund",null,report.totals.gross-report.totals.refunds);
 add("Ringkasan","Item terjual (pcs; sebelum pengembalian barang)",sales.reduce((n,o)=>n+o.payload.order.lines.reduce((v,l)=>v+l.quantity,0),0),null);
 for(const method of [0,1])add("Pembayaran penjualan",method===0?"Tunai":"QRIS",sales.filter(o=>o.payload.payment!.method===method).length,method===0?report.totals.cash:report.totals.qris);
 const daily=report.tables.find(t=>t.name==="Ringkasan_Harian")!;
 for(const r of daily.rows){add("Harian",r[1]+" · Penjualan",Number(r[2]),Number(r[3]));add("Harian",r[1]+" · Refund berhasil",null,-Number(r[6]));add("Harian",r[1]+" · Setelah refund",null,Number(r[7]));}
 const products=report.tables.find(t=>t.name==="Rekap_Produk")!;
 for(const r of [...products.rows].sort((a,b)=>Number(b[4])-Number(a[4])))add("Menu terjual",String(r[2]),Number(r[4]),Number(r[5]));
 for(const e of s.events.filter(e=>e.in_period&&(e.kind==="cash_in"||e.kind==="cash_out")))add(e.kind==="cash_in"?"Kas masuk di luar penjualan":"Kas keluar di luar refund",wib(e.occurred_at)+" · "+e.payload.reason,null,e.kind==="cash_in"?e.amount:-e.amount);
 add("Catatan","Refund mengurangi nominal, bukan pcs per menu: barang yang dikembalikan belum dicatat.",null,null);
 add("Catatan","Kas masuk/keluar bukan penjualan. Penjualan setelah refund bukan laba atau saldo laci. QRIS sesuai catatan kasir.",null,null);
 const incomplete=!s.devices.length||s.devices.some(d=>d.pending_count==null||d.pending_count>0||!d.last_report_at||Date.parse(s.capturedAt)-Date.parse(d.last_report_at)>900000);
 add("Sinkronisasi",incomplete?"Data mungkin belum lengkap; periksa antrean aplikasi.":"Antrean perangkat terakhir dilaporkan kosong.",null,null);
 return {...report,tables:[{name:"Riwayat Penjualan",columns:["Waktu (WIB)","No. Pesanan","Menu","Pcs","Harga Satuan (Rp)","Subtotal (Rp)","Metode Pembayaran","Jenis","Status","Keterangan"],rows:history},{name:"Rekap Penjualan",columns:["Bagian","Keterangan","Jumlah","Nominal (Rp)"],rows}]};
}
