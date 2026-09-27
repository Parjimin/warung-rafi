using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using WarungRafi.Core;
using WarungRafi.Desktop;
using WarungRafi.Storage;

internal static class Program
{
    private static int checks;
    private static MaintenanceWindow window=null!;
    private static FrameworkElement root=null!;
    private static readonly string directory=Path.Combine(Path.GetTempPath(),"WarungRafi-operations-"+Guid.NewGuid().ToString("N"));
    private static readonly string artifacts=Path.GetFullPath("artifacts/M6-review");
    [STAThread]
    private static int Main()
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/WarungRafi;component/Theme.xaml",UriKind.Relative)});
        app.DispatcherUnhandledException+=(_,e)=>{Console.Error.WriteLine(e.Exception);e.Handled=true;app.Shutdown(1);};
        _=Task.Run(async()=>{await Task.Delay(TimeSpan.FromSeconds(90));Environment.Exit(1);});
        app.Startup+=async(_,_)=>
        {
            var result=1;
            try{await Verify();Console.WriteLine($"{checks} operations/security checks passed.");result=0;}
            catch(Exception e){Console.Error.WriteLine(e);try{Screenshot("failure");}catch{}}
            finally{window?.Close();SqliteConnection.ClearAllPools();try{Directory.Delete(directory,true);}catch{}app.Shutdown(result);}
        };
        return app.Run();
    }
    private static async Task Verify()
    {
        Directory.CreateDirectory(directory);Directory.CreateDirectory(artifacts);
        var file=new SettingsFile(Path.Combine(directory,"settings.protected"));var profile=new DesktopSettings(ManagerPinHash:ManagerPin.Hash("654321"),DeviceToken:"fixture-token-at-least-thirty-two-characters",BackupPassword:"fixture-password-no-real-secret");file.Save(profile);
        Check(new SettingsFile(file.FilePath).Load()==profile,"protected settings survive process-style reload");
        Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file.FilePath)).Contains(profile.DeviceToken),"credentials are not stored as plaintext");
        Reject(()=>file.Save(profile with {ApiOrigin="http://fixture.invalid/"}),"HTTP configuration cannot be saved");
        Reject(()=>file.Save(profile with {ApiOrigin="https://fixture.invalid/path"}),"path and token forwarding ambiguity is rejected");
        Check(file.Load()==profile,"invalid configuration preserves previous settings");
        for(var i=0;i<5;i++)Reject(()=>new SettingsFile(file.FilePath).Authorize("111111"),"wrong owner PIN persists attempt "+(i+1));
        Reject(()=>new SettingsFile(file.FilePath).Authorize("654321"),"PIN cooldown survives file reload and blocks even the correct PIN");
        file.Save(profile);Check(file.Authorize("654321").PinFailures==0,"correct PIN clears failed attempts");
        var original=File.ReadAllBytes(file.FilePath);File.WriteAllBytes(file.FilePath,[1,2,3]);Reject(()=>file.Load(),"damaged protected settings fail closed");Check(File.ReadAllBytes(file.FilePath).Length==3,"failed settings read does not overwrite evidence");File.WriteAllBytes(file.FilePath,original);
        file.Save(new DesktopSettings());
        var path=Path.Combine(directory,"test.db");var store=new LocalStore(path);await store.InitializeAsync();await store.SaveAsync(OrderRules.Add(Order.New(),new Product("fixture","Menu uji","Nasi",5000)));
        const string password="fixture-backup-password-2026";var archive=Path.Combine(directory,"snapshot.wrbackup");await store.BackupAsync(archive,password);
        await store.SaveAsync(OrderRules.Add(Order.New(),new Product("later","Menu berikut","Nasi",1000)));
        using var lease=DatabaseLease.Acquire(path);
        window=new MaintenanceWindow(store,file,lease){ShowInTaskbar=false};window.Show();
        root=(FrameworkElement)window.Content;window.Content=null;var canvas=new Canvas();canvas.Children.Add(root);window.Content=canvas;Layout(806,690);
        await Until(()=>Find<PasswordBox>("SetupPin") is not null);
        Get<PasswordBox>("SetupPin").Password="654321";Get<PasswordBox>("SetupPinConfirm").Password="654321";await Click("SaveSetupPin");
        Check(ManagerPin.Verify(file.Load().ManagerPinHash,"654321"),"first-run owner PIN is saved through the UI");
        Get<TextBox>("SettingsOrigin").Text="https://fixture.invalid";Get<PasswordBox>("SettingsToken").Password=profile.DeviceToken;Get<ComboBox>("SettingsPrinter").Text="Fixture printer";await Click("SaveConnection");
        Check(file.Load().ApiOrigin=="https://fixture.invalid/"&&file.Load().PrinterName=="Fixture printer","connection and printer selection save through the UI");
        Check(Get<PasswordBox>("SettingsToken").Password.Length==0,"token input clears after saving");
        Layout(560,450);var scroll=MainWindow.Descendants<ScrollViewer>(root).First();Check(scroll.ScrollableHeight>0,"small settings window scrolls instead of clipping controls");Get<Button>("SaveConnection").BringIntoView();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);Screenshot("01-connection-small");
        Layout(806,690);Get<TextBox>("SettingsOrigin").Text="";await Click("SaveConnection");
        await Click("SettingsBackup");Get<TextBox>("BackupFolder").Text=Path.Combine(directory,"scheduled");Get<PasswordBox>("BackupPassword").Password=password;Get<PasswordBox>("BackupPasswordConfirm").Password=password;await Click("SaveBackupSettings");
        Check(MainWindow.Descendants<ScrollViewer>(root).First().VerticalOffset<1,"switching tabs returns to the top of the form");
        Check(file.Load().BackupPassword==password&&Path.IsPathFullyQualified(file.Load().BackupFolder),"automatic backup schedule saves protected password and folder");Screenshot("02-backup");
        await Click("SettingsRestore");Get<TextBox>("RestoreFile").Text=archive;Get<PasswordBox>("RestorePassword").Password=password;await Click("InspectRestore");
        Check(Find<TextBox>("RestoreConfirm") is not null&&await store.CountAsync()==2,"inspection presents review before changing the database");Screenshot("03-restore-review");
        await Click("ConfirmRestore");Check(await store.CountAsync()==2,"restore without typed confirmation preserves newer transactions");
        Get<TextBox>("RestoreConfirm").Text="PULIHKAN";await Click("ConfirmRestore");
        Check(await store.CountAsync()==1&&await store.SettingAsync("recovery_required")=="1","confirmed restore replaces database and keeps cashier gated");
        Check(Directory.GetFiles(Path.Combine(directory,"RecoverySafety"),"*.wrbackup").Length==1,"restore saves the current database before replacement");Screenshot("04-recovery-gate");
        await Click("ResumeOffline");Check(await store.SettingAsync("recovery_required") is null&&await store.SettingAsync("sync_recheck")=="1","offline review resumes cashier but preserves future cloud check");
        window.Close();window=new MaintenanceWindow(store,file,lease){ShowInTaskbar=false};window.Show();root=(FrameworkElement)window.Content;
        await Until(()=>Find<PasswordBox>("UnlockSettingsPin") is not null);Check(Find<TextBox>("SettingsOrigin") is null,"reopening settings requires PIN again");
        Get<PasswordBox>("UnlockSettingsPin").Password="654321";await Click("UnlockSettings");Check(Get<ComboBox>("SettingsPrinter").Text=="Fixture printer","saved settings are restored after PIN unlock");
    }
    private static bool Ready=>MainWindow.Descendants<StackPanel>(root).All(x=>x.IsEnabled);
    private static async Task Click(string id){await Until(()=>Ready);var button=Get<Button>(id);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>Ready);}
    private static T? Find<T>(string id) where T:DependencyObject=>MainWindow.Descendants<T>(root).FirstOrDefault(x=>AutomationProperties.GetAutomationId(x)==id);
    private static T Get<T>(string id) where T:DependencyObject=>Find<T>(id)??throw new Exception("Missing control "+id);
    private static async Task Until(Func<bool> condition){var end=DateTime.UtcNow.AddSeconds(20);do{await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);if(condition())return;await Task.Delay(20);}while(DateTime.UtcNow<end);throw new Exception("UI wait timed out");}
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
    private static void Reject(Action action,string message){try{action();}catch(Exception e) when(e is ArgumentException or InvalidDataException or InvalidOperationException){Check(true,message);return;}throw new Exception("Accepted: "+message);}
    private static void Layout(double width,double height){root.Width=width;root.Height=height;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();}
    private static void Screenshot(string name){root.UpdateLayout();var image=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var file=File.Create(Path.Combine(artifacts,name+".png"));encoder.Save(file);}
}
