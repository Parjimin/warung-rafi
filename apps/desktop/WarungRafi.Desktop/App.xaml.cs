using System.Windows;
using Microsoft.Data.Sqlite;
using WarungRafi.Storage;
namespace WarungRafi.Desktop;
public partial class App : Application
{
    private DatabaseLease? lease;
    internal static string DataPath(string localRoot,bool demo) => demo?Path.Combine(localRoot,"WarungRafi","DemoQris","warung-rafi.db"):Path.Combine(localRoot,"WarungRafi","warung-rafi.db");
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);ShutdownMode=ShutdownMode.OnExplicitShutdown;
        try
        {
            var demo=e.Args.Contains("--demo-qris",StringComparer.OrdinalIgnoreCase);var path=DataPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),demo);
            lease=DatabaseLease.Acquire(path);var file=new SettingsFile(Path.Combine(Path.GetDirectoryName(path)!,"settings.protected"));
            var settings=demo?new DesktopSettings():file.ImportEnvironment();
            if(!demo&&!e.Args.Contains("--maintenance",StringComparer.OrdinalIgnoreCase)&&settings.ApiOrigin.Length==0)
            {
                new ActivationWindow(file).ShowDialog();settings=file.Load();
            }
            var storage=new LocalStore(path,settings.ManagerPinHash);
            var maintenance=!demo&&e.Args.Contains("--maintenance",StringComparer.OrdinalIgnoreCase);var notice="";
            if(!maintenance)
            {
                try{await storage.InitializeAsync();maintenance=await storage.SettingAsync("recovery_required")=="1";if(maintenance)notice="Pemulihan belum selesai diperiksa. Buka tab Pemulihan sebelum melanjutkan kasir.";}
                catch(Exception error) when(error is SqliteException or InvalidDataException) {maintenance=true;notice="Database tidak dapat dibuka. Berkas tetap disimpan; pulihkan backup yang sudah diperiksa.";}
            }
            MainWindow=maintenance?new MaintenanceWindow(storage,file,lease,notice):new MainWindow(storage,demo,demo,null,demo?null:file,settings);
            ShutdownMode=ShutdownMode.OnMainWindowClose;MainWindow.Show();
        }
        catch(Exception error){MessageBox.Show(error.Message,"Warung Rafi belum dapat dibuka",MessageBoxButton.OK,MessageBoxImage.Information);Shutdown(1);}
    }
    protected override void OnExit(ExitEventArgs e) {lease?.Dispose();base.OnExit(e);}
}
