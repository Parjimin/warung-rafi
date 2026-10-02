using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using WarungRafi.Core;
namespace WarungRafi.Desktop;

internal sealed class ActivationWindow : Window
{
    internal bool ContinueOffline {get;private set;}
    internal const string DefaultOrigin="https://d2fydw5nlxjhzmk.mpksmakaduta.web.id/";
    private readonly CancellationTokenSource closing=new();
    internal ActivationWindow(SettingsFile file,bool directSheets=false)
    {
        Title="Hubungkan Warung Rafi";Width=450;Height=570;MinWidth=380;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var panel=new StackPanel {Margin=new Thickness(28)};
        panel.Children.Add(new TextBlock{Text="Masuk sekali, selanjutnya otomatis",FontSize=21,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
        panel.Children.Add(new TextBlock{Text="Gunakan akun pengelola yang dipakai di web. Transaksi dan Google Sheets akan tersinkron saat aplikasi terbuka dan online.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
        panel.Children.Add(new TextBlock{Text="Email pengelola"});var email=new TextBox{Margin=new Thickness(0,4,0,10)};panel.Children.Add(email);
        panel.Children.Add(new TextBlock{Text="Kata sandi"});var password=new PasswordBox{Margin=new Thickness(0,4,0,10)};panel.Children.Add(password);
        var needsPin=file.Load().ManagerPinHash.Length==0;
        var pin=new PasswordBox{Margin=new Thickness(0,4,0,10)};var confirm=new PasswordBox{Margin=new Thickness(0,4,0,10)};
        if(needsPin){panel.Children.Add(new TextBlock{Text="Buat PIN pengelola (6 angka)"});panel.Children.Add(pin);panel.Children.Add(new TextBlock{Text="Ulangi PIN"});panel.Children.Add(confirm);}
        var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};panel.Children.Add(status);
        var connect=new Button{Content="Masuk & hubungkan",Margin=new Thickness(0,4,0,8),MinHeight=38};panel.Children.Add(connect);
        var offline=new Button{Content="Lewati, buka offline",MinHeight=32};panel.Children.Add(offline);
        offline.Click+=(_,_)=>{ContinueOffline=true;Close();};Closed+=(_,_)=>closing.Cancel();
        connect.Click+=async(_,_)=>
        {
            connect.IsEnabled=false;status.Text="Menghubungkan…";
            try
            {
                if(needsPin&&(pin.Password.Length!=6||pin.Password.Any(c=>c<'0'||c>'9')||pin.Password!=confirm.Password))throw new InvalidOperationException("Isi PIN 6 angka dan ulangi dengan nilai yang sama.");
                using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri(DefaultOrigin),Timeout=TimeSpan.FromSeconds(45)};
                using var response=await http.PostAsJsonAsync("api/device/activate",new{email=email.Text.Trim(),password=password.Password,directSheets},closing.Token);
                password.Clear();
                if(!response.IsSuccessStatusCode)throw new InvalidOperationException(response.StatusCode==System.Net.HttpStatusCode.Unauthorized?"Email atau kata sandi belum sesuai.":"Belum dapat terhubung. Periksa internet atau kesiapan server, lalu coba lagi.");
                var result=await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken:closing.Token);
                var settings=file.Load() with {ApiOrigin=DefaultOrigin,DeviceToken=result.GetProperty("deviceToken").GetString()??""};
                if(directSheets){
                    if(!result.TryGetProperty("directSheets",out var connection)||connection.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("Server perlu diperbarui untuk koneksi Sheets langsung.");
                    settings=settings with {DirectSheetsJson=connection.GetRawText()};
                }
                if(needsPin)settings=settings with {ManagerPinHash=ManagerPin.Hash(pin.Password)};
                SettingsFile.Validate(settings);closing.Token.ThrowIfCancellationRequested();
                file.Save(settings);pin.Clear();confirm.Clear();DialogResult=true;
            }
            catch(OperationCanceledException) when(closing.IsCancellationRequested) { }
            catch(Exception error){status.Text=error is InvalidOperationException?error.Message:"Koneksi belum berhasil. Coba lagi saat online.";}
            finally{password.Clear();connect.IsEnabled=true;}
        };
        Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
}
