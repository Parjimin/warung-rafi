using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using WarungRafi.Core;
using WarungRafi.Storage;
var folder=Path.Combine(Path.GetTempPath(),"WarungRafi-backup-checks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var checks=0;
void Check(bool value,string message) {if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
async Task Reject(Func<Task> action,string message) {try{await action();}catch(Exception e) when(e is InvalidDataException or IOException or InvalidOperationException or ArgumentException){Check(true,message);return;}throw new Exception("Accepted: "+message);}
var password="fixture-backup-password-2026";
try
{
 var path=Path.Combine(folder,"cashier.db");var store=new LocalStore(path,ManagerPin.Hash("654321"));await store.InitializeAsync();
 var identity=await store.SettingAsync("database_id");await store.InitializeAsync();Check(identity==await store.SettingAsync("database_id"),"initialization preserves database identity");
 var session=Guid.NewGuid().ToString("N");await store.OpenCashAsync(session,100000);
 var order=await store.SaveAsync(OrderRules.Add(Order.New(),new Product("fixture","Menu uji","Nasi",12500)));
 var sale=await store.CompleteAsync(order.Id,order.Version,PaymentMethod.Cash,20000);
 await store.RecordCashMovementAsync(Guid.NewGuid().ToString("N"),session,false,5000,"Pengeluaran uji");
 var draft=await store.SaveAsync(OrderRules.Hold(OrderRules.Add(Order.New(),new Product("fixture","Menu uji","Nasi",5000)),"TEST"));
 await store.ReceivePaymentsAsync([new(1,"provider-fixture",12500,DateTimeOffset.UtcNow)]);
 var pending=await store.PendingAsync();await store.AcknowledgeAsync(pending.Select(x=>x.Id).ToArray());
 var finance=await store.PendingFinanceAsync();await store.AcknowledgeFinanceAsync(finance.Select(x=>x.Id).ToArray());
 var backup=Path.Combine(folder,"snapshot.wrbackup");var info=await store.BackupAsync(backup,password);
 Check(info.Orders==2&&info.Payments==1&&info.FinanceEvents==3&&info.Pending==0,"online backup includes WAL transactions, drafts and acknowledgements");
 var bytes=File.ReadAllBytes(backup);Check(!System.Text.Encoding.UTF8.GetString(bytes).Contains("Menu uji"),"archive does not expose sale content");
 var checkedInfo=await BackupArchive.InspectAsync(backup,password);Check(checkedInfo==info,"archive inspection verifies identity and counts");
 await Reject(()=>store.BackupAsync(backup,password),"existing backups cannot be overwritten");
 await Reject(()=>BackupArchive.InspectAsync(backup,"wrong-password-fixture"),"wrong password is rejected");
 foreach(var index in new[]{10,HeaderOffset(),bytes.Length/2,bytes.Length-1})
 {
  var altered=bytes.ToArray();altered[index]^=1;var file=Path.Combine(folder,"altered.wrbackup");File.WriteAllBytes(file,altered);await Reject(()=>BackupArchive.InspectAsync(file,password),"tampered header/ciphertext/tag is rejected at "+index);
 }
 var truncated=Path.Combine(folder,"truncated.wrbackup");File.WriteAllBytes(truncated,bytes[..^1]);await Reject(()=>BackupArchive.InspectAsync(truncated,password),"truncated archive is rejected");
 var extra=Path.Combine(folder,"extra.wrbackup");File.WriteAllBytes(extra,[..bytes,0]);await Reject(()=>BackupArchive.InspectAsync(extra,password),"trailing data is rejected");
 using(var lease=DatabaseLease.Acquire(path))
 {
  await Reject(()=>Task.Run(()=>DatabaseLease.Acquire(path).Dispose()),"second cashier/maintenance lease is rejected");
  var newer=await store.SaveAsync(OrderRules.Add(Order.New(),new Product("next","Menu berikut","Nasi",1000)));
  await Reject(()=>BackupArchive.RestoreAsync(backup,"wrong-password-fixture",path,lease,Path.Combine(folder,"safety")),"failed restore keeps current data");
  Check(await store.OrderAsync(newer.Id) is not null,"newer sale draft remains after failure");
  await Reject(()=>BackupArchive.RestoreAsync(backup,password,path,lease,Path.Combine(folder,"safety"),expectedHash:new string('0',64)),"archive changed after review is rejected");
  await BackupArchive.RestoreAsync(backup,password,path,lease,Path.Combine(folder,"safety"));
  var restored=new LocalStore(path);await restored.InitializeAsync();
  Check(await restored.CountAsync()==2&&(await restored.SaleAsync(sale.Order.Id))!.Payment==sale.Payment,"restore returns exact payments without duplicating sales");
  Check((await restored.ActiveCashAsync())!.Expected==107500,"restore retains cash session and ledger balance");
  Check((await restored.OrderAsync(draft.Id))!.Status==OrderStatus.Held&&await restored.CursorAsync()==1,"held orders and provider cursor survive restore");
  Check(await restored.PendingCountAsync()==pending.Length&&await restored.PendingFinanceCountAsync()==finance.Length,"restore schedules exact historical IDs for replay");
  Check(await restored.SettingAsync("recovery_required")=="1","restored database requires review before cashier starts");
  var safety=Directory.GetFiles(Path.Combine(folder,"safety"),"*.wrbackup").Single();Check((await BackupArchive.InspectAsync(safety,password)).Orders==3,"pre-restore encrypted safety backup preserves newer work");
  await restored.CompleteOfflineRecoveryAsync();Check(await restored.SettingAsync("recovery_required") is null,"never-connected database can resume offline after review");
  await restored.BindCloudAsync("https://fixture.invalid/");await Reject(()=>restored.BindCloudAsync("https://another.invalid/"),"cloud identity cannot silently move");
  await Reject(()=>restored.CompleteOfflineRecoveryAsync(),"previously connected database requires cloud reconciliation");
  await restored.BindDeviceAsync("fixture-device");await Reject(()=>restored.BindDeviceAsync("another-device"),"device identity cannot silently change");
  await BackupArchive.RestoreAsync(backup,password,path,lease,Path.Combine(folder,"safety"));
  Check(await restored.SettingAsync("cloud_origin")=="https://fixture.invalid/"&&await restored.SettingAsync("cloud_device")=="fixture-device","older offline archive retains the current cloud binding");
  await Reject(()=>restored.CompleteOfflineRecoveryAsync(),"restoring an older offline backup cannot bypass cloud review");
  var replay=await restored.PendingAsync();var latest=replay.Where(x=>x.AggregateId==sale.Order.Id).MaxBy(x=>x.Version)!;
  var page=JsonSerializer.SerializeToElement(new {orders=new[]{new{id=sale.Order.Id,version=latest.Version,payload=JsonSerializer.Deserialize<JsonElement>(latest.Payload)}},finance=new{sequence=finance.Last().Sequence,payload=finance.Last() }},new JsonSerializerOptions(JsonSerializerDefaults.Web));
  await restored.ValidateRecoveryPageAsync(page);Check(true,"matching cloud frontier is accepted");
  var unknown=JsonSerializer.SerializeToElement(new{orders=new[]{new{id=Guid.NewGuid().ToString("N"),version=1,payload=new{}}},finance=(object?)null});await Reject(()=>restored.ValidateRecoveryPageAsync(unknown),"newer cloud order prevents unsafe recovery");
  var later=JsonSerializer.SerializeToElement(new{orders=Array.Empty<object>(),finance=new{sequence=finance.Last().Sequence+1,payload=new{}}});await Reject(()=>restored.ValidateRecoveryPageAsync(later),"newer cloud journal prevents unsafe recovery");
 }
 var corrupt=Path.Combine(folder,"corrupt.db");File.WriteAllText(corrupt,"corrupt database fixture");
 using(var lease=DatabaseLease.Acquire(corrupt))
 {
  await Reject(()=>BackupArchive.RestoreAsync(backup,password,corrupt,lease,Path.Combine(folder,"corrupt-safety")),"corrupt current database needs explicit recovery mode");
  await BackupArchive.RestoreAsync(backup,password,corrupt,lease,Path.Combine(folder,"corrupt-safety"),true);
  Check(File.ReadAllText(Directory.GetFiles(Path.Combine(folder,"corrupt-safety"),"warung-rafi.db",SearchOption.AllDirectories).Single())=="corrupt database fixture","corrupt original is preserved byte for byte");
  Check(await new LocalStore(corrupt).CountAsync()==2,"corrupt database can be replaced by validated archive");
 }
 Console.WriteLine($"{checks} backup/recovery checks passed.");
}
finally {SqliteConnection.ClearAllPools();Directory.Delete(folder,true);}
static int HeaderOffset()=>48;
