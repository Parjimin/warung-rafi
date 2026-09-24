import { test } from "node:test";
import assert from "node:assert/strict";
import { products } from "../lib/catalog.ts";
import { events } from "../lib/sync.ts";
import { body } from "../lib/http.ts";
const item = { id:"p",name:"Nasi",category:"Nasi",price:8000,available:true,imageUrl:null };
const id="0123456789abcdef0123456789abcdef";
function event() { return { id:id+":1",aggregateId:id,version:1,payload:{order:{id,version:1,status:2,number:"WR-1",customerLabel:"",createdAt:"2026-09-22T10:00:00Z",updatedAt:"2026-09-22T10:00:00Z",total:16000,lines:[{productId:"p",name:"Nasi",category:"Nasi",unitPrice:8000,quantity:2}]},payment:{orderId:id,method:0,amount:16000,tendered:20000,change:4000,paidAt:"2026-09-22T10:00:00Z"}}}; }
test("catalog allows a new menu but rejects duplicate IDs and unsafe images", () => {
  assert.equal(products([item])[0].price,8000);
  for (const value of [[item,item],[{...item,price:0}],[{...item,imageUrl:"javascript:alert(1)"}],[{...item,category:"Unknown"}]]) assert.throws(() => products(value));
});
test("sync recomputes totals and change from historical line snapshots", () => {
  assert.equal(events([event()]).length,1);
  const bad=event(); bad.payload.order.total=15000; assert.throws(() => events([bad]));
  const change=event(); change.payload.payment.change=5000; assert.throws(() => events([change]));
  const qris=event(); qris.payload.payment.method=1; assert.throws(() => events([qris]));
});
test("sync rejects invalid timestamps and duplicate line IDs", () => {
  const wrong=event(); wrong.payload.order.lines.push(wrong.payload.order.lines[0]); assert.throws(() => events([wrong]));
  const date=event(); date.payload.order.updatedAt="invalid"; assert.throws(() => events([date]));
});
test("body has an actual streamed size limit, independent of Content-Length", async () => {
  await assert.rejects(body(new Request("http://localhost",{method:"POST",body:JSON.stringify({x:"a".repeat(100)})}),20));
  assert.deepEqual(await body(new Request("http://localhost",{method:"POST",body:'{"x":1}'})),{x:1});
});
