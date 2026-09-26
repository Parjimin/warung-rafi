using System.ComponentModel;
using System.Printing;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using WarungRafi.Core;
using WarungRafi.Storage;

namespace WarungRafi.Desktop;

internal sealed class MaintenanceWindow : Window
{
    private readonly LocalStore store;private readonly SettingsFile file;private readonly DatabaseLease? lease;
    private readonly bool standalone;private DesktopSettings settings;private bool busy,unlocked;
    private readonly StackPanel content=new() {MaxWidth=740,Margin=new Thickness(22,8,22,22)};
    private readonly TextBlock status=new() {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(22,12,22,16),FontSize=14};
    private readonly StackPanel navigation=new() {Orientation=Orientation.Horizontal,Margin=new Thickness(22,12,22,6)};
    private readonly string startupMessage;private ScrollViewer viewport=null!;
    internal MaintenanceWindow(LocalStore store,SettingsFile file,DatabaseLease? lease=null,string message="")
    {
        this.store=store;this.file=file;this.lease=lease;standalone=lease is not null;settings=file.Load();startupMessage=message;
        Title="Warung Rafi — Pengaturan & Pemulihan";Width=850;Height=760;MinWidth=580;MinHeight=500;
        Background=new SolidColorBrush(Color.FromRgb(242,244,239));Foreground=new SolidColorBrush(Color.FromRgb(36,61,51));FontFamily=new FontFamily("Segoe UI");FontSize=17;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new Grid {Background=Background};foreach(var size in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})root.RowDefinitions.Add(new RowDefinition{Height=size});
        var title=new TextBlock {Text="Pengaturan & pemulihan",FontSize=28,FontWeight=FontWeights.SemiBold,Margin=new Thickness(22,20,22,4)};root.Children.Add(title);
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
        ClearContent();navigation.Children.Clear();Text(startupMessage.Length>0?startupMessage:"Pengaturan disimpan untuk akun Windows ini. Rahasia tidak disertakan dalam backup transaksi.");
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
        navigation.Children.Clear();Button("Koneksi & printer","SettingsConnection",()=>{RenderConnection();return Task.CompletedTask;},navigation);Button("Backup","SettingsBackup",()=>{RenderBackup();return Task.CompletedTask;},navigation);Button("Pemulihan","SettingsRestore",()=>{RenderRestore();return Task.CompletedTask;},navigation);
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
    }
    private void RenderBackup()
    {
        ClearContent();Text("Backup transaksi",true);Text("Backup mencakup pesanan, kas/refund, antrean, katalog dan bukti QRIS. Foto cache dan kredensial tidak ikut. Kata sandi backup berbeda dari PIN pengelola dan diperlukan saat laptop diganti.");
        var folder=Input("Folder backup otomatis · kosong untuk nonaktif","BackupFolder",settings.BackupFolder);
        Button("Pilih folder","ChooseBackupFolder",()=>{var dialog=new OpenFolderDialog();if(dialog.ShowDialog(this)==true)folder.Text=dialog.FolderName;return Task.CompletedTask;});
        var password=Secret("Kata sandi backup baru · minimal 12 karakter","BackupPassword");var confirm=Secret("Ulangi kata sandi baru","BackupPasswordConfirm");
        Text("Backup otomatis berjalan saat aplikasi terbuka, maksimal sekali per 24 jam, saat layar pembayaran tidak aktif. Simpan salinan di media lain. Tidak ada backup lama yang dihapus otomatis.");
        Button("Simpan jadwal backup","SaveBackupSettings",()=>
        {
            var secret=password.Password.Length==0?settings.BackupPassword:password.Password;
            if(password.Password.Length>0&&password.Password!=confirm.Password)throw new ArgumentException("Kedua kata sandi backup belum sama.");
            settings=settings with {BackupFolder=folder.Text.Trim(),BackupPassword=folder.Text.Trim().Length==0?"":secret,LastBackup=null};file.Save(settings);password.Clear();confirm.Clear();Say("Pengaturan backup tersimpan. Buka ulang kasir untuk menjalankan jadwal.");return Task.CompletedTask;
        });
        Button("Buat backup sekarang","BackupNow",async()=>
        {
            if(settings.BackupPassword.Length==0)throw new InvalidOperationException("Simpan folder dan kata sandi backup dahulu.");
            var dialog=new SaveFileDialog {Filter="Backup Warung Rafi|*.wrbackup",DefaultExt=".wrbackup",FileName="WarungRafi-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".wrbackup",InitialDirectory=settings.BackupFolder,OverwritePrompt=true};
            if(dialog.ShowDialog(this)!=true){Say("Backup dibatalkan.");return;}
            var result=await store.BackupAsync(dialog.FileName,settings.BackupPassword);Say($"Backup terverifikasi: {result.Orders} pesanan, {result.Payments} pembayaran, {result.FinanceEvents} jurnal. Simpan kata sandinya secara terpisah.");
        });
        Text(settings.LastBackup is null?"Belum ada backup otomatis yang tercatat.":"Backup otomatis terakhir: "+settings.LastBackup.Value.ToLocalTime().ToString("g"));
    }
    private void RenderRestore()
    {
        ClearContent();Text("Pulihkan backup",true);
        if(!standalone){Text("Tutup kasir, lalu buka pintasan Pemulihan Warung Rafi. Pemulihan hanya dapat dilakukan saat kasir berhenti agar transaksi baru tidak tertimpa.");return;}
        Text("Pemulihan mengganti database dengan isi backup yang dipilih. Data sesudah waktu backup tidak ikut dipulihkan. Salinan kondisi saat ini disimpan sebelum penggantian.");
        var path=Input("File backup","RestoreFile");Button("Pilih backup","ChooseRestoreFile",()=>{var dialog=new OpenFileDialog{Filter="Backup Warung Rafi|*.wrbackup"};if(dialog.ShowDialog(this)==true)path.Text=dialog.FileName;return Task.CompletedTask;});
        var password=Secret("Kata sandi file backup","RestorePassword");
        Button("Periksa isi backup","InspectRestore",async()=>
        {
            var selected=path.Text;var secret=password.Password;var info=await BackupArchive.InspectAsync(selected,secret);ClearContent();
            Text("Periksa sebelum memulihkan",true);Text($"Dibuat {info.CreatedAt.ToOffset(TimeSpan.FromHours(7)):dd MMM yyyy HH:mm} WIB\n{info.Orders} pesanan · {info.Payments} pembayaran · {info.FinanceEvents} jurnal kas\n{info.Pending} perubahan belum diakui server saat backup dibuat.");
            var corrupt=Id(new CheckBox {Content=new TextBlock{Text="Database saat ini rusak/tidak bisa dibuka; simpan berkas aslinya lalu pulihkan",TextWrapping=TextWrapping.Wrap},Margin=new Thickness(0,14,0,10)},"RestoreCorrupt");content.Children.Add(corrupt);
            Text("Berkas asli database rusak atau salinan rollback tersimpan lokal tanpa enkripsi di folder data. Jaga akses akun Windows. Jangan menghapusnya sebelum pemulihan dipastikan benar.");
            var confirm=Input("Ketik PULIHKAN untuk menyetujui penggantian","RestoreConfirm");
            Button("Pulihkan database","ConfirmRestore",async()=>
            {
                if(confirm.Text!="PULIHKAN")throw new InvalidOperationException("Ketik PULIHKAN setelah memeriksa tanggal dan jumlah data.");
                await BackupArchive.RestoreAsync(selected,secret,store.DatabasePath,lease!,Path.Combine(Path.GetDirectoryName(store.DatabasePath)!,"RecoverySafety"),corrupt.IsChecked==true,info.ArchiveHash);
                RenderRecoveryReview();Say("Database dipulihkan. Periksa kecocokan cloud sebelum membuka kasir.");
            });
            Button("Batal","CancelRestore",()=>{RenderRestore();Say("Database belum diganti.");return Task.CompletedTask;});
            Say("Kata sandi, seluruh isi terenkripsi, struktur database dan integritas telah diperiksa.");
        });
        Button("Lanjutkan pemeriksaan pemulihan sebelumnya","ReviewRestored",()=>{RenderRecoveryReview();return Task.CompletedTask;});
    }
    private void RenderRecoveryReview()
    {
        ClearContent();Text("Pemeriksaan sebelum berjualan",true);Text("Jika backup pernah terhubung cloud, isi pesanan dan jurnal harus cocok dengan server. Backup yang lebih lama dari cloud tidak boleh digunakan untuk membuat jurnal baru.");
        Button("Periksa kecocokan cloud","VerifyRestoredCloud",async()=>
        {
            settings=file.Load();using var sync=RemoteSync.FromSettings(store,settings)??throw new InvalidOperationException("Simpan alamat server dan token pada tab Koneksi terlebih dahulu.");
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));await sync.VerifyRecoveryAsync(timeout.Token);Say("Data cloud cocok. Tutup Pemulihan lalu buka kasir. Antrean dikirim ulang dengan identitas lama tanpa menggandakan catatan.");
        });
        Button("Gunakan database yang belum pernah terhubung cloud","ResumeOffline",async()=>
        {await store.CompleteOfflineRecoveryAsync();Say("Pemeriksaan offline selesai. Tutup Pemulihan lalu buka kasir.");});
    }
}
