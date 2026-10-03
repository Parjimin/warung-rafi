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
            var storage=new LocalStore(path,settings.ManagerPinHash);
            await storage.InitializeAsync();
            if(await storage.SettingAsync("recovery_required")=="1"||await storage.SettingAsync("sync_recheck")=="1")
                throw new InvalidDataException("Database ini masih menunggu pemeriksaan hasil pemulihan versi lama. Selesaikan pemeriksaan melalui versi sebelumnya sebelum memakai aplikasi ini; data tidak diubah.");
            var maintenance=!demo&&e.Args.Contains("--maintenance",StringComparer.OrdinalIgnoreCase);
            if(!demo&&!maintenance&&settings.ApiOrigin.Length==0)
            {
                var activation=new ActivationWindow(file,true);
                if(activation.ShowDialog()!=true&&!activation.ContinueOffline){Shutdown();return;}
                settings=file.Load();storage=new LocalStore(path,settings.ManagerPinHash);
            }
            MainWindow=maintenance?new MaintenanceWindow(storage,file):new MainWindow(storage,demo,demo,null,demo?null:file,settings);
            ShutdownMode=ShutdownMode.OnMainWindowClose;MainWindow.Show();
        }
        catch(Exception error){MessageBox.Show(error.Message,"Warung Rafi belum dapat dibuka",MessageBoxButton.OK,MessageBoxImage.Information);Shutdown(1);}
    }
    protected override void OnExit(ExitEventArgs e) {lease?.Dispose();base.OnExit(e);}
}
