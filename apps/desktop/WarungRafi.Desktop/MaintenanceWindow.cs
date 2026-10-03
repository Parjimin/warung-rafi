using System.ComponentModel;
using System.Printing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using WarungRafi.Core;
using WarungRafi.Storage;

namespace WarungRafi.Desktop;

internal sealed class MaintenanceWindow : Window
{
    private readonly LocalStore store;private readonly SettingsFile file;
    private DesktopSettings settings;private bool busy,unlocked;
    private readonly StackPanel content=new() {MaxWidth=740,Margin=new Thickness(22,8,22,22)};
    private readonly TextBlock status=new() {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(22,12,22,16),FontSize=14};
    private readonly StackPanel navigation=new() {Orientation=Orientation.Horizontal,Margin=new Thickness(22,12,22,6)};
    private ScrollViewer viewport=null!;
    internal MaintenanceWindow(LocalStore store,SettingsFile file)
    {
        this.store=store;this.file=file;settings=file.Load();
        Title="Warung Rafi — Pengaturan";Width=850;Height=760;MinWidth=580;MinHeight=500;
        Background=new SolidColorBrush(Color.FromRgb(242,244,239));Foreground=new SolidColorBrush(Color.FromRgb(36,61,51));FontFamily=new FontFamily("Segoe UI");FontSize=17;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new Grid {Background=Background};foreach(var size in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})root.RowDefinitions.Add(new RowDefinition{Height=size});
        var title=new TextBlock {Text="Pengaturan",FontSize=28,FontWeight=FontWeights.SemiBold,Margin=new Thickness(22,20,22,4)};root.Children.Add(title);
        root.Children.Add(navigation);Grid.SetRow(navigation,1);
        var scroll=viewport=new ScrollViewer {Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};root.Children.Add(scroll);Grid.SetRow(scroll,2);
        root.Children.Add(status);Grid.SetRow(status,3);Content=root;Closing+=OnClosing;Loaded+=(_,_)=>RenderUnlock();
    }
    private void OnClosing(object? sender,CancelEventArgs e) {if(busy){e.Cancel=true;status.Text="Tunggu pemeriksaan/penyimpanan selesai sebelum menutup.";}}
    private static T Id<T>(T element,string id) where T:DependencyObject {AutomationProperties.SetAutomationId(element,id);return element;}
    private void ClearContent(){content.Children.Clear();viewport.ScrollToTop();}
    private void Say(string text)=>status.Text=text;
    private void Text(string text,bool heading=false)=>content.Children.Add(new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,FontSize=heading?23:16,FontWeight=heading?FontWeights.SemiBold:FontWeights.Normal,Margin=new Thickness(0,12,0,8)});
    private TextBox Input(string label,string id,string value="") {Text(label);var box=Id(new TextBox{Text=value,MinHeight=42,MaxLength=1024,Padding=new Thickness(9)},id);content.Children.Add(box);return box;}
    private PasswordBox Secret(string label,string id) {Text(label);var box=Id(new PasswordBox{MinHeight=42,MaxLength=512,Padding=new Thickness(9)},id);content.Children.Add(box);return box;}
    private Button Button(string label,string id,Func<Task> action,Panel? parent=null)
    {
        var button=Id(new Button {Content=label,MinHeight=42,Padding=new Thickness(14,8,14,8),Margin=new Thickness(0,10,10,0)},id);
        button.Click+=async(_,_)=>await Run(action);(parent??content).Children.Add(button);return button;
    }
    private async Task Run(Func<Task> action)
    {
        if(busy)return;busy=true;content.IsEnabled=false;navigation.IsEnabled=false;Say("Sedang memeriksa…");
        try{await action();}catch(Exception e){Say("Belum berhasil: "+e.Message);}finally{busy=false;content.IsEnabled=true;navigation.IsEnabled=true;}
    }
    private void RenderUnlock()
    {
        ClearContent();navigation.Children.Clear();Text("Pengaturan disimpan untuk akun Windows ini.");
        if(settings.ManagerPinHash.Length==0)
        {
            Text("Buat PIN pengelola",true);Text("PIN 6–12 angka diperlukan untuk mengubah pengaturan dan menyetujui pengembalian uang.");
            var pin=Secret("PIN baru","SetupPin");var confirm=Secret("Ulangi PIN","SetupPinConfirm");
            Button("Simpan PIN pengelola","SaveSetupPin",()=>
            {if(pin.Password!=confirm.Password)throw new ArgumentException("Kedua PIN belum sama.");settings=settings with {ManagerPinHash=ManagerPin.Hash(pin.Password)};file.Save(settings);pin.Clear();confirm.Clear();unlocked=true;RenderNavigation();RenderConnection();Say("PIN tersimpan. Buka ulang kasir setelah selesai mengatur.");return Task.CompletedTask;});
        }
        else
        {
            Text("Masuk sebagai pengelola",true);var pin=Secret("PIN pengelola","UnlockSettingsPin");
            Button("Buka pengaturan","UnlockSettings",()=>{settings=file.Authorize(pin.Password);pin.Clear();unlocked=true;RenderNavigation();RenderConnection();Say("Pengaturan terbuka. Tutup jendela ini setelah selesai.");return Task.CompletedTask;});
        }
    }
    private void RenderNavigation()
    {
        navigation.Children.Clear();Button("Printer & PIN","SettingsConnection",()=>{RenderConnection();return Task.CompletedTask;},navigation);
    }
    private void RenderConnection()
    {
        if(!unlocked)return;ClearContent();Text("Koneksi laptop",true);Text("Kosongkan alamat untuk memakai kasir offline. Kunci Supabase, Midtrans dan Google hanya dipasang pada server, bukan di laptop.");
        var origin=Input("Alamat HTTPS admin","SettingsOrigin",settings.ApiOrigin);var token=Secret("Token laptop baru · kosong berarti tetap","SettingsToken");
        Text("Printer Windows");var printer=Id(new ComboBox {IsEditable=true,Text=settings.PrinterName,MinHeight=42},"SettingsPrinter");content.Children.Add(printer);
        try{using var server=new LocalPrintServer();printer.ItemsSource=server.GetPrintQueues().Select(q=>q.FullName).ToArray();}catch{Text("Daftar printer belum dapat dibaca. Nama printer dapat diisi setelah driver dipasang.");}
        var pin=Secret("PIN baru · kosong berarti tetap","SettingsNewPin");var confirm=Secret("Ulangi PIN baru","SettingsNewPinConfirm");
        DesktopSettings Draft()
        {
            var updated=settings with {ApiOrigin=SettingsFile.NormalizeOrigin(origin.Text.Trim()),DeviceToken=token.Password.Length==0?settings.DeviceToken:token.Password,PrinterName=printer.Text.Trim()};
            if(pin.Password.Length>0){if(pin.Password!=confirm.Password)throw new ArgumentException("Kedua PIN belum sama.");updated=updated with {ManagerPinHash=ManagerPin.Hash(pin.Password)};}
            SettingsFile.Validate(updated);return updated;
        }
        Button("Periksa koneksi server","CheckServer",async()=>
        {
            using var sync=RemoteSync.FromSettings(store,Draft())??throw new InvalidOperationException("Isi alamat dan token dahulu.");
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));var device=await sync.CheckSetupAsync(timeout.Token);Say("Server dan migrasi siap untuk perangkat "+device+". Ini belum membuktikan pembayaran atau pencairan nyata.");
        });
        Button("Simpan pengaturan","SaveConnection",async()=>
        {
            var draft=Draft();if(draft.ApiOrigin.Length>0&&File.Exists(store.DatabasePath))
            {var bound=await store.SettingAsync("cloud_origin");if(bound is not null&&bound!=draft.ApiOrigin)throw new InvalidOperationException("Database terhubung ke server lain. Alamat tidak boleh dipindah tanpa rekonsiliasi data.");}
            settings=draft;file.Save(settings);token.Clear();pin.Clear();confirm.Clear();Say("Pengaturan tersimpan terlindungi. Tutup dan buka ulang kasir untuk menerapkannya.");
        });
        Text("Nama printer disimpan tanpa mencetak. Setelah perangkat tersedia, uji struk asli, kertas habis dan kabel terputus sebelum dipakai berjualan.");
        Button("Hubungkan Sheets langsung","ConnectDirectSheets",()=>{
            var activation=new ActivationWindow(file,true){Owner=this};
            if(activation.ShowDialog()==true){settings=file.Load();Say("Sheets terhubung. Tutup pengaturan; pembaruan berjalan dari laptop setiap satu menit.");}
            return Task.CompletedTask;
        });
        // Routine settings stay short; connection repair is available when needed.
        var advancedContent=new StackPanel();
        for(var i=0;i<6;i++){var child=content.Children[0];content.Children.RemoveAt(0);advancedContent.Children.Add(child);}
        var check=content.Children.OfType<Button>().First(b=>AutomationProperties.GetAutomationId(b)=="CheckServer");
        content.Children.Remove(check);advancedContent.Children.Add(check);
        content.Children.Add(Id(new Expander {Header="Koneksi lanjutan",Content=advancedContent,IsExpanded=settings.DeviceToken.Length==0,Margin=new Thickness(0,18,0,0)},"AdvancedConnection"));

    }
}
