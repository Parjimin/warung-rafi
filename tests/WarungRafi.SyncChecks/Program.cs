using System.Net;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using WarungRafi.Core;
using WarungRafi.Desktop;
using WarungRafi.Storage;
using Microsoft.Data.Sqlite;

var folder=Path.Combine(Path.GetTempPath(),"warung-sync-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
var path=Path.Combine(folder,"test.db");var store=new LocalStore(path);await store.InitializeAsync();var passed=0;
void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
async Task Fails(Func<Task> action){try{await action();}catch(HttpRequestException){return;}catch(InvalidDataException){return;}throw new Exception("Expected transport failure");}
using var handler=new FakeHandler();using var http=new HttpClient(handler){BaseAddress=new Uri("https://fixture.invalid/")};var sync=new RemoteSync(store,http);
try
{
    var order=await store.SaveAsync(OrderRules.Add(Order.New(),new Product("p","Nasi","Nasi",5000)));
    handler.Reply=_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    await Fails(()=>sync.SendOutboxAsync(default));Check(await store.PendingCountAsync()==1,"server failure retains outbox");
    handler.Reply=_=>throw new HttpRequestException("offline");
    await Fails(()=>sync.SendOutboxAsync(default));
    order=await store.SaveAsync(OrderRules.Add(order,new Product("p","Nasi","Nasi",5000)));
    Check(order.Total==10000&&await store.PendingCountAsync()==2,"local order remains writable offline");
    handler.Reply=_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));
    await Fails(()=>sync.SendOutboxAsync(default));Check(await store.PendingCountAsync()==2,"conflict retains entire batch");
    handler.Reply=_=>Task.FromResult(JsonContentResponse(new { version=2,products=new[]{new Product("p","Nasi baru","Nasi",7000)} }));
    await sync.FetchCatalogAsync(default);Check((await store.CatalogAsync()).Version==2,"catalog can download independently after sync conflict");
    Check((await store.ListAsync(OrderStatus.Draft))[0].Total==10000,"catalog does not rewrite existing order price");
    handler.Reply=_=>Task.FromResult(JsonContentResponse(new { accepted=(string[]?)null }));
    await Fails(()=>sync.SendOutboxAsync(default));Check(await store.PendingCountAsync()==2,"malformed acknowledgement retains outbox");
    handler.Reply=_=>Task.FromResult(JsonContentResponse(new { accepted=new[]{order.Id+":1","unknown:1"} }));
    await sync.SendOutboxAsync(default);Check(await store.PendingCountAsync()==1,"only known acknowledged events leave queue");
    handler.Reply=async request=>
    {
        var doc=await request.Content!.ReadFromJsonAsync<JsonElement>();
        Check(doc.GetProperty("pendingCount").GetInt32()==1&&doc.GetProperty("catalogVersion").GetInt64()==2,"heartbeat reports local backlog and cached version");
        return JsonContentResponse(new { recorded=true });
    };
    await sync.ReportStatusAsync(default);
    handler.Reply=_=>Task.FromResult(JsonContentResponse(new { accepted=new[]{order.Id+":2"} }));
    await sync.SendOutboxAsync(default);Check(await store.PendingCountAsync()==0,"successful retry acknowledges remainder");
    handler.Reply=_=>throw new HttpRequestException("offline");await Fails(()=>sync.FetchCatalogAsync(default));
    Check((await new LocalStore(path).CatalogAsync()).Products[0].Price==7000,"cached catalog survives offline restart");
    var proof=new ProviderPayment(1,"payment-offline",22500,DateTimeOffset.UtcNow);
    handler.Reply=_=>throw new HttpRequestException("offline");
    await Fails(()=>sync.FetchPaymentsAsync(default));Check(await store.CursorAsync()==0,"offline payment poll preserves cursor");
    handler.Reply=_=>Task.FromResult(JsonContentResponse(new { payments=(ProviderPayment[]?)null }));
    await Fails(()=>sync.FetchPaymentsAsync(default));Check(await store.CursorAsync()==0,"malformed inbox preserves cursor");
    handler.Reply=request=>
    {
        Check(request.RequestUri!.Query=="?after=0","reconnection starts at last durable cursor");
        return Task.FromResult(JsonContentResponse(new { payments=new[]{proof} }));
    };
    Check((await sync.FetchPaymentsAsync(default)).Length==1,"reconnection delivers one proof");
    handler.Reply=request=>
    {
        Check(request.RequestUri!.Query=="?after=1","next poll resumes after durable proof");
        return Task.FromResult(JsonContentResponse(new { payments=new[]{proof} }));
    };
    Check((await sync.FetchPaymentsAsync(default)).Length==0,"replayed HTTP response creates no second alert");
    var cashSession=await store.OpenCashAsync(Guid.NewGuid().ToString("N"),50000);
    handler.Reply=_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));
    await Fails(()=>sync.SendFinanceAsync(default));Check(await store.PendingFinanceCountAsync()==1,"finance conflict keeps local event queued");
    handler.Reply=_=>Task.FromResult(JsonContentResponse(new { accepted=new[]{"unknown-finance"} }));
    await Fails(()=>sync.SendFinanceAsync(default));Check(await store.PendingFinanceCountAsync()==1,"unknown finance acknowledgement cannot discard queued entry");
    handler.Reply=async request=>
    {
        Check(request.RequestUri!.AbsolutePath=="/api/device/finance","finance uses separate ordered journal endpoint");
        var doc=await request.Content!.ReadFromJsonAsync<JsonElement>();
        Check(doc.GetProperty("events")[0].GetProperty("amount").GetInt64()==50000,"finance transport preserves whole-rupiah opening cash");
        return JsonContentResponse(new { accepted=new[]{"open-"+cashSession.Id} });
    };
    await sync.SendFinanceAsync(default);Check(await store.PendingFinanceCountAsync()==0&&(await store.ActiveCashAsync())!.Expected==50000,"acknowledgement leaves local drawer history intact");
    // A reconnect must drain large historical snapshots under the actual API byte limit.
    var large=Order.New() with {Lines=Enumerable.Range(0,300).Select(i=>new OrderLine("large-"+i,new string('飯',60),"Nasi",5000,1)).ToArray()};
    for(var i=0;i<50;i++)large=await store.SaveAsync(large);
    var sentIds=new List<string>();var requests=0;
    handler.Reply=async request=>
    {
        var bytes=await request.Content!.ReadAsByteArrayAsync();
        if(bytes.Length>256_000)return new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge);
        using var document=JsonDocument.Parse(bytes);
        var ids=document.RootElement.GetProperty("events").EnumerateArray().Select(e=>e.GetProperty("id").GetString()!).ToArray();
        requests++;sentIds.AddRange(ids);
        // An acknowledgement for a queued but unsent event must not discard it.
        return JsonContentResponse(new {accepted=ids.Append(large.Id+":50").ToArray()});
    };
    for(var attempt=0;attempt<50&&await store.PendingCountAsync()>0;attempt++)await sync.SendOutboxAsync(default);
    Check(requests>1&&await store.PendingCountAsync()==0,"large offline snapshots drain within server byte limit");
    Check(sentIds.SequenceEqual(Enumerable.Range(1,50).Select(i=>large.Id+":"+i)),"byte batching preserves every version and ignores unsent acknowledgements");
    // Restore review must retain its gate on any interrupted or inconsistent response.
    using(var connection=new SqliteConnection("Data Source="+path)){connection.Open();using var cmd=connection.CreateCommand();cmd.CommandText="INSERT INTO settings VALUES('recovery_required','1'),('sync_recheck','1')";cmd.ExecuteNonQuery();}
    var recoveryRequests=0;var recoveryState=new string('a',32);var changed=false;
    handler.Reply=request=>
    {
        if(request.RequestUri!.AbsolutePath.EndsWith("setup"))return Task.FromResult(JsonContentResponse(new {schema=6,deviceId="fixture-device"}));
        recoveryRequests++;
        return Task.FromResult(JsonContentResponse(new {state=changed&&recoveryRequests==2?new string('b',32):recoveryState,deviceId="fixture-device",orders=Array.Empty<object>(),finance=(object?)null,next=(string?)null}));
    };
    changed=true;await Fails(()=>sync.VerifyRecoveryAsync(default));
    Check(await store.SettingAsync("recovery_required")=="1","changed final recovery response cannot clear cashier gate");
    recoveryRequests=0;changed=false;await sync.VerifyRecoveryAsync(default);
    Check(recoveryRequests==2&&await store.SettingAsync("recovery_required") is null&&await store.SettingAsync("sync_recheck") is null,"stable server review rechecks source then unlocks recovered database");
    Check(await store.SettingAsync("cloud_device")=="fixture-device","recovery persists server device identity");
    handler.Reply=_=>Task.FromResult(JsonContentResponse(new{schema=5,deviceId="fixture-device"}));await Fails(()=>sync.CheckSetupAsync(default));Check(true,"older server migration is rejected before recovery");
    // Real WPF image decoder: fixture is a tiny PNG generated as test data, no external image request.
    var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGNU8LFjYGBgYmBgYGBgAAAIBACuE8zpaAAAAABJRU5ErkJggg==");
    handler.Reply=_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(png)});
    var image=await ProductImages.LoadAsync("https://fixture.invalid/menu.png",folder,http);
    Check(image is not null,"photo decodes and caches locally");
    handler.Reply=_=>throw new HttpRequestException("offline");
    Check(await ProductImages.LoadAsync("https://fixture.invalid/menu.png",folder,http) is not null,"photo remains available offline with a fresh load");
    Check(await ProductImages.LoadAsync("https://fixture.invalid/missing.png",folder,http) is null,"missing offline photo falls back without exception");
    handler.Reply=_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(png)});
    Check(await ProductImages.LoadAsync("https://fixture.invalid/missing.png",folder,http) is not null,"failed photo can retry when network recovers");
    Check(!Directory.EnumerateFiles(folder,"*.tmp").Any(),"photo cache leaves no temporary file");
}
finally { SqliteConnection.ClearAllPools();Directory.Delete(folder,true); }
Console.WriteLine($"{passed} sync/cache checks passed.");
static HttpResponseMessage JsonContentResponse(object value)=>new(HttpStatusCode.OK){Content=JsonContent.Create(value)};
sealed class FakeHandler:HttpMessageHandler
{
    public Func<HttpRequestMessage,Task<HttpResponseMessage>> Reply {get;set;}=null!;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Reply(request);
}
