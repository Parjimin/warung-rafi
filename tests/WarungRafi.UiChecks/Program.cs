using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WarungRafi.Core;
using WarungRafi.Desktop;
using WarungRafi.Storage;

internal static class Program
{
    private static int checks;
    private static MainWindow window=null!;
    private static FrameworkElement root=null!;
    private static double width=1280,height=720;
    private static readonly string directory=Path.Combine(Path.GetTempPath(),"WarungRafiUi-"+Guid.NewGuid().ToString("N"));
    private static readonly string artifacts=Path.GetFullPath("artifacts/UI-review");
    [STAThread]
    private static int Main()
    {
        var app=new App();app.InitializeComponent();app.StartupUri=null;app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        app.Startup+=async(_,_)=>
        {
            var result=1;
            try { await Verify();Console.WriteLine($"{checks} WPF layout/interaction checks passed.");result=0; }
            catch(Exception ex){Console.Error.WriteLine(ex);try{Screenshot("failure");}catch{}}
            finally
            {
                if(window is not null){window.Close();}
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try{Directory.Delete(directory,true);}catch{}
                app.Shutdown(result);
            }
        };
        return app.Run();
    }
    private static async Task Verify()
    {
        Directory.CreateDirectory(artifacts);
        var store=new LocalStore(Path.Combine(directory,"test.db"));await store.InitializeAsync();
        window=new MainWindow(store,true) { Width=1320,Height=850,ShowInTaskbar=false };
        window.Show();root=(FrameworkElement)window.Content;
        await Until(()=>Find<Button>("PayOrder") is not null);
        Layout();
        Check(!Get<Button>("PayOrder").IsEnabled,"Empty order cannot enter payment");Screenshot("01-empty");
        foreach(var id in new[]{"NAS-001","NAS-002","NAS-003","NAS-004"})await Click("Product-"+id,()=>Get<ScrollViewer>("CartViewport").Content is StackPanel p&&p.Children.Count==Array.IndexOf(new[]{"NAS-001","NAS-002","NAS-003","NAS-004"},id)+1);
        foreach(var size in new[]{(1280d,720d),(1366d,768d),(1536d,864d),(1920d,1080d),(900d,620d)})
        {
            width=size.Item1;height=size.Item2;Layout();
            var cart=Get<ScrollViewer>("CartViewport");
            Check(cart.ActualHeight>=220,$"Cart keeps useful height at {width}x{height}: {cart.ActualHeight:F0} DIP");
            if(height>=720)Check(cart.ScrollableHeight<1,$"Four items fit without scrolling at {width}x{height}");
            Check(Inside(Get<Button>("PayOrder"))&&Inside(Get<Button>("HoldOrder")),"Cart actions stay within viewport");
            Check(Inside(Get<TextBlock>("CartTotal")),"Total remains visible");
            Check(Get<Button>("Qty-Plus-NAS-001").ActualWidth>=44,"Quantity target at least 44 DIP");
            Check(Get<Button>("PayOrder").Foreground is SolidColorBrush b&&b.Color==Colors.White,"Primary button has white foreground");
            Screenshot($"02-cart-{width}x{height}");
        }
        width=1280;height=720;Layout();
        var name=Get<TextBox>("CustomerName");name.Text="Bu Rini";
        // Clicking while a name edit is pending must save the label before replacing the cart.
        await Click("Qty-Plus-NAS-001",()=>Get<TextBlock>("CartTotal").Text=="Rp46.000");
        Check(Get<TextBox>("CustomerName").Text=="Bu Rini","Customer name survives cart refresh");
        var saved=(await store.ListAsync(OrderStatus.Draft)).Single();Check(saved.CustomerLabel=="Bu Rini","Customer name persists to SQLite");
        await Click("ExpandCart",()=>Get<Button>("ExpandCart").Content as string=="Kembali ke menu");Layout();
        Check(Get<ScrollViewer>("CartViewport").ActualWidth>1000,"Expanded cart uses available width");Screenshot("03-expanded-cart");
        await Click("ExpandCart",()=>Get<Button>("ExpandCart").Content as string=="Perbesar");
        Get<TextBox>("MenuSearch").Text="Teh";Layout();Check(Find<Button>("Product-MIN-001") is not null,"Search finds menus across categories");
        await Click("Product-MIN-001",()=>Get<TextBlock>("CartTotal").Text=="Rp49.000");
        await Click("HoldOrder",()=>!Get<Button>("PayOrder").IsEnabled);
        Check(await store.CountAsync(OrderStatus.Held)==1,"Hold saves editable order");
        window.HeldNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>Find<Button>("Resume-"+saved.Id) is not null);Layout();Screenshot("04-held");
        await Click("Resume-"+saved.Id,()=>Find<Button>("PayOrder") is not null);
        Check(Get<TextBox>("CustomerName").Text=="Bu Rini","Resumed order keeps name");
        await Click("PayOrder",()=>Find<Button>("CompleteOrder") is not null);Layout();
        Check(!Get<Button>("CompleteOrder").IsEnabled,"Cash completion disabled before sufficient payment");
        foreach(var size in new[]{(1280d,720d),(900d,620d)})
        {width=size.Item1;height=size.Item2;Layout();Check(Inside(Get<Button>("CompleteOrder")),"Complete action remains visible on payment page");Screenshot($"05-payment-{width}x{height}");}
        width=1280;height=720;Layout();
        Get<TextBox>("Tendered").Text="abc";Check(!Get<Button>("CompleteOrder").IsEnabled,"Invalid cash input cannot complete");
        await Click("Cash-50000",()=>Get<Button>("CompleteOrder").IsEnabled);
        Check(Get<TextBlock>("ChangeLabel").Text=="Kembalian Rp1.000","Cash change is correct");Screenshot("06-cash-ready");
        await Click("MethodQris",()=>Find<TextBox>("Tendered") is null);Layout();Screenshot("07-qris");
        var focus=Keyboard.FocusedElement;
        Check(!window.Toast.IsHitTestVisible&&!window.Toast.Focusable,"QRIS popup cannot intercept input or focus");
        window.ToastAmount.Text="Rp49.000";window.ToastTime.Text="Pukul 18.44";window.Toast.Visibility=Visibility.Visible;Layout();
        Check(Keyboard.FocusedElement==focus,"Passive toast preserves keyboard focus");Screenshot("08-passive-toast");window.Toast.Visibility=Visibility.Collapsed;
        await Click("MethodCash",()=>Find<TextBox>("Tendered") is not null);
        await Click("CompleteOrder",()=>Find<TextBlock>("SuccessAmount") is not null);Layout();
        Check(Get<TextBlock>("SuccessAmount").Text=="Rp1.000","Success prominently shows change");Check(await store.CountAsync(OrderStatus.Completed)==1,"UI completes one durable sale");Screenshot("09-success");
        window.HistoryNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>Find<Button>("Detail-"+saved.Id) is not null);Layout();Screenshot("10-history");
        await Click("Detail-"+saved.Id,()=>Find<TextBox>("OrderSearch") is null);Layout();Screenshot("11-order-detail");
        window.CashNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>MainWindow.Descendants<TextBlock>(root).Any(x=>x.Text=="Penjualan tercatat"));Layout();Screenshot("12-daily-summary");
        // Reopen a real persisted draft in a new WPF window.
        var draft=OrderRules.Add(Order.New(),DummyCatalog.Products[0]) with { CustomerLabel="Pak Joko" };await store.SaveAsync(draft);
        window.Close();window=new MainWindow(store,true) { ShowInTaskbar=false };window.Show();root=(FrameworkElement)window.Content;
        await Until(()=>Find<TextBox>("CustomerName") is not null);Layout();
        Check(Get<TextBox>("CustomerName").Text=="Pak Joko","New app instance restores draft and customer name");
        Check(Get<TextBlock>("CartTotal").Text=="Rp5.000","New app instance restores cart amount");
    }
    private static void Layout()
    {
        root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
    }
    private static bool Inside(FrameworkElement element)
    {
        var p=element.TransformToAncestor(root).Transform(new Point());
        return p.X>=0&&p.Y>=0&&p.X+element.ActualWidth<=root.ActualWidth+1&&p.Y+element.ActualHeight<=root.ActualHeight+1;
    }
    private static T? Find<T>(string id) where T:DependencyObject => MainWindow.Descendants<T>(root).FirstOrDefault(x=>AutomationProperties.GetAutomationId(x)==id);
    private static T Get<T>(string id) where T:DependencyObject => Find<T>(id)??throw new Exception("Missing UI element: "+id);
    private static async Task Click(string id,Func<bool> done)
    {
        var button=Get<Button>(id);Check(button.IsEnabled,"Action enabled: "+id);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(done);Layout();
    }
    private static async Task Until(Func<bool> condition)
    {
        var end=DateTime.UtcNow.AddSeconds(15);
        do { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);if(condition()){await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);return;}await Task.Delay(20); } while(DateTime.UtcNow<end);
        throw new Exception("Timed out waiting for UI state. "+window.StatusText.Text);
    }
    private static void Check(bool condition,string label)
    { if(!condition)throw new Exception(label);checks++;Console.WriteLine("PASS "+label); }
    private static void Screenshot(string name)
    {
        Layout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(artifacts,name+".png"));encoder.Save(file);
    }
}
