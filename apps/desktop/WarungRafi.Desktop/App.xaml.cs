using System.Windows;
using WarungRafi.Storage;
namespace WarungRafi.Desktop;
public partial class App : Application
{
    internal static string DataPath(string localRoot,bool demo) => demo
        ? Path.Combine(localRoot,"WarungRafi","DemoQris","warung-rafi.db")
        : Path.Combine(localRoot,"WarungRafi","warung-rafi.db");
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var demo=e.Args.Contains("--demo-qris",StringComparer.OrdinalIgnoreCase);
        var storage=new LocalStore(DataPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),demo));
        MainWindow=new MainWindow(storage,demo,demo);
        MainWindow.Show();
    }
}
