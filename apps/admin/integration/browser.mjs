import { chromium } from 'playwright';
import { startHarness } from './harness.ts';
import sharp from 'sharp';
import assert from 'node:assert/strict';
const app = await startHarness();
let browser;
try {
 browser = await chromium.launch({headless:true,args:['--no-sandbox']});
 const context=await browser.newContext({viewport:{width:1366,height:900}});
 await context.addCookies([{name:'warung_admin',value:'fixture-admin',url:app.origin}]);
 const page=await context.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(app.origin);await page.getByRole('button',{name:'Ubah menu'}).first().waitFor();
 await page.screenshot({path:'../../artifacts/M3-review/catalog.png',fullPage:true});
 await page.getByRole('button',{name:'Ubah menu'}).first().click();
 const png=await sharp({create:{width:300,height:200,channels:3,background:'#d3dfcf'}}).png().toBuffer();
 await page.getByLabel('Unggah foto').setInputFiles({name:'fixture.png',mimeType:'image/png',buffer:png});
 await page.getByAltText('Pratinjau foto menu').waitFor();
 await page.screenshot({path:'../../artifacts/M3-review/photo-editor.png',fullPage:true});
 await page.getByRole('button',{name:'Batal',exact:true}).click();
 await page.getByRole('button',{name:'+ Tambah menu'}).click();
 await page.getByLabel('Nama menu').fill('Sate Telur Uji');await page.getByLabel('Harga (rupiah)').fill('4000');await page.getByRole('combobox').selectOption('Sundukan');
 await page.getByRole('button',{name:'Terapkan ke draf'}).click();
 await page.getByRole('button',{name:'Simpan draf',exact:true}).click();await page.getByText('Draf tersimpan.',{exact:false}).waitFor();
 await page.getByRole('button',{name:'Terbitkan ke kasir',exact:true}).click();await page.getByText('Menu diterbitkan.',{exact:false}).waitFor();
 assert.equal(app.state.published.length,5);
 // Two-tab conflict retains browser changes until explicit reload.
 await page.getByRole('button',{name:'Ubah menu'}).first().click();await page.getByLabel('Nama menu').fill('Nama lokal tetap');await page.getByRole('button',{name:'Terapkan ke draf'}).click();
 app.state.draft_version++;
 await page.getByRole('button',{name:'Simpan draf',exact:true}).click();await page.getByText('Data berubah atau urutan data belum cocok.',{exact:false}).waitFor();
 assert.equal(await page.getByRole('heading',{name:'Nama lokal tetap'}).count(),1);
 page.on('dialog',d=>d.accept());await page.getByRole('button',{name:'Muat ulang draf'}).click();await page.getByText('Draf terbaru berhasil dimuat.').waitFor();
 app.control.devices=[{device_id:'kasir-utama',pending_count:18,catalog_version:1,last_seen_at:new Date().toISOString(),last_report_at:new Date().toISOString(),last_sync_at:new Date(Date.now()-300000).toISOString(),conflict_at:new Date().toISOString(),conflict_events:['0123456789abcdef0123456789abcdef:3']}];
 await page.goto(app.origin+'/sinkronisasi');await page.getByText('18 perubahan',{exact:true}).waitFor();
 await page.screenshot({path:'../../artifacts/M3-review/sync-monitor.png',fullPage:true});
 await page.setViewportSize({width:390,height:844});await page.screenshot({path:'../../artifacts/M3-review/sync-mobile.png',fullPage:true});
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth>window.innerWidth),false);
 assert.deepEqual(errors,[]);console.log('Browser flow passed: upload preview, edit, save, publish, conflict preservation, reload, monitor, mobile overflow.');
} finally {await browser?.close();await app.stop();}
