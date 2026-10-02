using System.Diagnostics;
using System.Text.Json;
namespace WarungRafi.Desktop;

internal sealed class DirectSheets(string? runtimeDirectory=null)
{
    private string? hash,verifiedAt;
    internal async Task<string> RefreshAsync(string config,CancellationToken cancellation)
    {
        if(string.IsNullOrWhiteSpace(config))return "Sheets: buka Pengaturan → Hubungkan Sheets langsung";
        var directory=runtimeDirectory??Path.Combine(AppContext.BaseDirectory,"sheets");
        var executable=Path.Combine(directory,"node.exe");var script=Path.Combine(directory,"worker.mjs");
        if(!File.Exists(executable)||!File.Exists(script))return "Sheets: paket tidak lengkap · ekstrak seluruh folder aplikasi";
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(65));
        using var process=new Process {StartInfo=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=directory}};
        process.StartInfo.ArgumentList.Add(script);
        // Prevent externally supplied Node flags from injecting code into the credential-bearing worker.
        process.StartInfo.Environment.Remove("NODE_OPTIONS");process.StartInfo.Environment.Remove("NODE_PATH");
        process.Start();
        using var stop=timeout.Token.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}});
        var output=process.StandardOutput.ReadToEndAsync(timeout.Token);var errors=process.StandardError.ReadToEndAsync(timeout.Token);
        try {
            var configuration=JsonSerializer.Deserialize<JsonElement>(config);
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(new{config=configuration,hash,verifiedAt}).AsMemory(),timeout.Token);process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);var result=await output;await errors;
            if(process.ExitCode!=0)throw new InvalidDataException("Sheets worker failed");
            var data=JsonSerializer.Deserialize<JsonElement>(result);
            if(data.GetProperty("state").GetString() is "verified" or "unchanged"){
                hash=data.GetProperty("hash").GetString();verifiedAt=data.GetProperty("verifiedAt").GetString();
                return "Sheets tersinkron langsung dari laptop";
            }
            hash=null;
            return data.GetProperty("code").GetString() switch {
                "permission"=>"Sheets: akses Google ditolak · hubungkan ulang di Pengaturan",
                "database_permission"=>"Sheets: akses database ditolak · hubungkan ulang di Pengaturan",
                "configuration"=>"Sheets: koneksi belum lengkap · hubungkan ulang di Pengaturan",
                "quota"=>"Sheets: batas Google tercapai · mencoba lagi nanti",
                "target_changed"=>"Sheets: tab atau tujuan berubah · periksa spreadsheet",
                "verification"=>"Sheets: data belum terverifikasi · mencoba lagi",
                _=>"Sheets belum tersinkron · mencoba lagi saat koneksi siap"
            };
        }
        finally {try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){} }
    }
}
