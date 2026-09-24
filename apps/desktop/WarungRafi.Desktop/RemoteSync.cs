using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WarungRafi.Core;
using WarungRafi.Storage;

namespace WarungRafi.Desktop;

internal sealed class RemoteSync(LocalStore store,HttpClient http)
{
    public static RemoteSync? FromEnvironment(LocalStore store)
    {
        var address=Environment.GetEnvironmentVariable("WARUNG_API_BASE_URL");
        var token=Environment.GetEnvironmentVariable("WARUNG_DEVICE_TOKEN");
        if(string.IsNullOrWhiteSpace(address)||string.IsNullOrWhiteSpace(token))return null;
        if(!Uri.TryCreate(address.TrimEnd('/')+"/",UriKind.Absolute,out var uri)||uri.Scheme!="https")return null;
        var http=new HttpClient { BaseAddress=uri,Timeout=TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
        return new(store,http);
    }
    public async Task SendOutboxAsync(CancellationToken token)
    {
        var outgoing=await store.PendingAsync();
        if(outgoing.Length>0)
        {
            var body=outgoing.Select(e=>new { id=e.Id,aggregateId=e.AggregateId,version=e.Version,payload=JsonSerializer.Deserialize<JsonElement>(e.Payload) }).ToArray();
            using var response=await http.PostAsJsonAsync("api/device/sync",new { events=body },token);
            response.EnsureSuccessStatusCode();
            var ack=await response.Content.ReadFromJsonAsync<Acknowledgement>(cancellationToken:token);
            if(ack is null)throw new InvalidDataException("Konfirmasi server kosong.");
            var known=outgoing.Select(x=>x.Id).ToHashSet();
            await store.AcknowledgeAsync(ack.Accepted.Where(known.Contains).ToArray());
        }
    }
    public async Task<ProviderPayment[]> FetchPaymentsAsync(CancellationToken token)
    {
        var cursor=await store.CursorAsync();
        var inbox=await http.GetFromJsonAsync<PaymentBatch>($"api/device/payments?after={cursor}",token);
        if(inbox is null)throw new InvalidDataException("Data pembayaran kosong.");
        return await store.ReceivePaymentsAsync(inbox.Payments);
    }
    public async Task FetchCatalogAsync(CancellationToken token)
    {
        var snapshot=await http.GetFromJsonAsync<CatalogSnapshot>("api/device/catalog",token);
        if(snapshot is null)throw new InvalidDataException("Katalog kosong.");
        await store.ReceiveCatalogAsync(snapshot);
    }
    private sealed record Acknowledgement(string[] Accepted);
    private sealed record PaymentBatch(ProviderPayment[] Payments);
}
