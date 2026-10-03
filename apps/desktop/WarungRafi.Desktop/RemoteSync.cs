using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WarungRafi.Core;
using WarungRafi.Storage;

namespace WarungRafi.Desktop;

internal sealed class RemoteSync(LocalStore store,HttpClient http) : IDisposable
{
    public static RemoteSync? FromEnvironment(LocalStore store)
    {
        return FromSettings(store,new DesktopSettings(Environment.GetEnvironmentVariable("WARUNG_API_BASE_URL")??"",Environment.GetEnvironmentVariable("WARUNG_DEVICE_TOKEN")??""));
    }
    public static RemoteSync? FromSettings(LocalStore store,DesktopSettings settings)
    {
        if(settings.ApiOrigin.Length==0)return null;
        SettingsFile.Validate(settings);
        var http=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { BaseAddress=new Uri(SettingsFile.NormalizeOrigin(settings.ApiOrigin)),Timeout=TimeSpan.FromSeconds(55) };
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",settings.DeviceToken);return new(store,http);
    }
    public void Dispose()=>http.Dispose();
    public async Task<string> CheckSetupAsync(CancellationToken token)
    {
        var setup=await http.GetFromJsonAsync<JsonElement>("api/device/setup",token);
        if(setup.GetProperty("schema").GetInt32()!=6)throw new InvalidDataException("Versi server belum sesuai. Terapkan migrasi 006 terlebih dahulu.");
        var id=setup.GetProperty("deviceId").GetString();
        if(string.IsNullOrWhiteSpace(id)||id.Length>80)throw new InvalidDataException("Identitas laptop kosong/tidak valid.");return id;
    }
    public async Task SendOutboxAsync(CancellationToken token)
    {
        var outgoing=await store.PendingAsync();
        if(outgoing.Length>0)
        {
            // The API limits bytes as well as event count. Large offline snapshots must
            // drain in ordered prefixes instead of retrying the same oversized batch.
            var body=outgoing.Select(e=>new { id=e.Id,aggregateId=e.AggregateId,version=e.Version,payload=JsonSerializer.Deserialize<JsonElement>(e.Payload) }).ToArray();
            var count=body.Length;byte[] encoded;
            while(true)
            {
                encoded=JsonSerializer.SerializeToUtf8Bytes(new { events=body.Take(count) },new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if(encoded.Length<=256_000)break;
                if(count==1)throw new InvalidDataException("Satu perubahan pesanan melebihi batas server. Data tetap tersimpan; hubungi pengelola.");
                count=Math.Max(1,count/2);
            }
            using var content=new ByteArrayContent(encoded);
            content.Headers.ContentType=new MediaTypeHeaderValue("application/json");
            using var response=await http.PostAsync("api/device/sync",content,token);
            response.EnsureSuccessStatusCode();
            var ack=await response.Content.ReadFromJsonAsync<Acknowledgement>(cancellationToken:token);
            if(ack?.Accepted is null)throw new InvalidDataException("Konfirmasi server kosong.");
            var known=outgoing.Take(count).Select(x=>x.Id).ToHashSet();
            await store.AcknowledgeAsync(ack.Accepted.Where(known.Contains).ToArray());
        }
    }
    public async Task SendFinanceAsync(CancellationToken token)
    {
        var outgoing=await store.PendingFinanceAsync();if(outgoing.Length==0)return;
        using var response=await http.PostAsJsonAsync("api/device/finance",new { events=outgoing },token);
        response.EnsureSuccessStatusCode();
        var ack=await response.Content.ReadFromJsonAsync<Acknowledgement>(cancellationToken:token);
        if(ack?.Accepted is null)throw new InvalidDataException("Konfirmasi keuangan kosong.");
        var known=outgoing.Select(x=>x.Id).ToHashSet();
        if(ack.Accepted.Any(id=>!known.Contains(id)))throw new InvalidDataException("Konfirmasi keuangan tidak sesuai kiriman.");
        await store.AcknowledgeFinanceAsync(ack.Accepted);
    }
    public async Task<ProviderPayment[]> FetchPaymentsAsync(CancellationToken token)
    {
        var cursor=await store.CursorAsync();
        var inbox=await http.GetFromJsonAsync<PaymentBatch>($"api/device/payments?after={cursor}",token);
        if(inbox?.Payments is null)throw new InvalidDataException("Data pembayaran kosong.");
        return await store.ReceivePaymentsAsync(inbox.Payments);
    }
    public async Task FetchCatalogAsync(CancellationToken token)
    {
        var snapshot=await http.GetFromJsonAsync<CatalogSnapshot>("api/device/catalog",token);
        if(snapshot is null)throw new InvalidDataException("Katalog kosong.");
        await store.ReceiveCatalogAsync(snapshot);
    }
    public async Task<string> RefreshSheetsAsync(CancellationToken token)
    {
        using var response=await http.PostAsync("api/device/sheets",null,token);
        if(!response.IsSuccessStatusCode)return (int)response.StatusCode switch {
            404=>"Sheets: server perlu diperbarui",
            401 or 403=>"Sheets: akses ditolak · periksa koneksi di Pengaturan",
            409=>"Sheets: proses sebelumnya berubah · mencoba lagi",
            503=>"Sheets: layanan belum siap · periksa konfigurasi/migrasi server",
            _=>$"Sheets gagal · kode {(int)response.StatusCode}"
        };
        var result=await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken:token);
        var state=result.GetProperty("state").GetString();
        return state switch {
            "verified" or "unchanged"=>"Sheets tersinkron",
            "waiting"=>"Sheets menunggu pemeriksaan berikutnya",
            "unavailable"=>"Sheets belum dikonfigurasi",
            _=>"Sheets belum tersinkron · periksa akses Google dan konfigurasi server"
        };
    }
    public async Task ReportStatusAsync(CancellationToken token)
    {
        var snapshot=await store.CatalogAsync();
        using var response=await http.PostAsJsonAsync("api/device/status",new {
            pendingCount=await store.PendingCountAsync()+await store.PendingFinanceCountAsync(),catalogVersion=snapshot.Version
        },token);
        response.EnsureSuccessStatusCode();
    }
    private sealed record Acknowledgement(string[] Accepted);
    private sealed record PaymentBatch(ProviderPayment[] Payments);
}
