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
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("/WarungRafi;component/Theme.xaml",UriKind.Relative) });
        app.DispatcherUnhandledException+=(_,e)=>{Console.Error.WriteLine(e.Exception);e.Handled=true;app.Shutdown(1);};
        _=Task.Run(async()=>{await Task.Delay(TimeSpan.FromSeconds(60));Console.Error.WriteLine("WPF review exceeded 60 second watchdog.");Environment.Exit(1);});
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
        var store=new LocalStore(Path.Combine(directory,"test.db"),ManagerPin.Hash("728491"));await store.InitializeAsync();await store.OpenCashAsync(Guid.NewGuid().ToString("N"),0);
        window=new MainWindow(store,true) { Width=1320,Height=850,ShowInTaskbar=false };
        window.Show();AttachReviewRoot();
        await Until(()=>Find<Button>("PayOrder") is not null);
        Layout();
        Check(!Get<Button>("PayOrder").IsEnabled,"Empty order cannot enter payment");Screenshot("01-empty");
        foreach(var id in new[]{"NAS-001","NAS-002","NAS-003","NAS-004"})await Click("Product-"+id,()=>Get<ScrollViewer>("CartViewport").Content is StackPanel p&&p.Children.Count==Array.IndexOf(new[]{"NAS-001","NAS-002","NAS-003","NAS-004"},id)+1);
        foreach(var size in new[]{(1280d,720d),(1366d,768d),(1536d,864d),(1920d,1080d),(900d,620d)})
        {
            width=size.Item1;height=size.Item2;Layout();
            var cart=Get<ScrollViewer>("CartViewport");
            Check(cart.ActualHeight>=210,$"Cart keeps useful height at {width}x{height}: {cart.ActualHeight:F0} DIP");
            if(height>=720)Check(cart.ScrollableHeight<1,$"Four items fit without scrolling at {width}x{height}");
            Check(Inside(Get<Button>("PayOrder"))&&Inside(Get<Button>("HoldOrder")),"Cart actions stay within viewport");
            Check(Inside(Get<TextBlock>("CartTotal")),"Total remains visible");
            Check(Get<Button>("Qty-Plus-NAS-001").ActualWidth>=44,"Quantity target at least 44 DIP");
            Check(Get<Button>("PayOrder").Foreground is SolidColorBrush b&&b.Color==Colors.White,"Primary button has white foreground");
            Screenshot($"02-cart-{width}x{height}");
        }
        width=1280;height=720;Layout();
        Check(Inside(window.SellNav)&&Inside(window.CashNav),"Top navigation remains inside the working area");
        var first=Get<Button>("Product-NAS-001").TransformToAncestor(root).Transform(new Point());
        var second=Get<Button>("Product-NAS-002").TransformToAncestor(root).Transform(new Point());
        var third=Get<Button>("Product-NAS-003").TransformToAncestor(root).Transform(new Point());
        Check(Math.Abs(first.Y-second.Y)<1&&third.Y>first.Y,"Four-menu category forms balanced two by two layout");
        Check(Get<ScrollViewer>("MenuViewport").ScrollableHeight<1,"Four menu cards fit the catalog at 1280x720");
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
        Check(Get<TextBlock>("ExpectedCash").Text=="Rp49.000","cash page counts sale net of change");
        foreach(var size in new[]{(1280d,720d),(900d,620d)})
        {
            width=size.Item1;height=size.Item2;Layout();
            Check(Inside(Get<TextBlock>("ExpectedCash"))&&Inside(Get<Button>("CloseCash")),"drawer total and close action fit finance viewport");
            foreach(var id in new[]{"CashIn","CashOut"})
                Check(VisibleWithinParents(Get<Button>(id)),id+" stays visible without scrolling at "+width+"x"+height);
            Get<ScrollViewer>("CashBalanceViewport").ScrollToEnd();Layout();
            Check(VisibleWithinParents(Get<Button>("CashIn"))&&VisibleWithinParents(Get<Button>("CashOut")),"cash actions stay pinned when reviewing drawer entries");
            Get<ScrollViewer>("CashBalanceViewport").ScrollToHome();Layout();
            Screenshot($"15-cash-{width}x{height}");
        }
        width=1280;height=720;Layout();
        await Click("CashOut",()=>Find<TextBox>("MovementAmount") is not null);
        Get<TextBox>("MovementAmount").Text="2000";Get<TextBox>("MovementReason").Text="Beli es batu";
        await Click("SaveCashMovement",()=>Find<TextBlock>("ExpectedCash") is not null);
        Check(Get<TextBlock>("ExpectedCash").Text=="Rp47.000","expense updates drawer immediately");
        window.HistoryNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>Find<Button>("Detail-"+saved.Id) is not null);
        await Click("Detail-"+saved.Id,()=>Find<Button>("OrderRefund") is not null);
        await Click("OrderRefund",()=>Find<TextBox>("RefundAmount") is not null);
        Get<TextBox>("RefundAmount").Text="5000";Get<TextBox>("RefundReason").Text="Satu nasi dikembalikan";
        await Click("RequestRefund",()=>MainWindow.Descendants<Button>(root).Any(x=>AutomationProperties.GetAutomationId(x).StartsWith("Resolve-")));
        var refund=(await store.RefundsAsync(saved.Id)).Single();
        Check((await store.ActiveCashAsync())!.Expected==47000,"request alone leaves cash unchanged");
        await Click("Resolve-"+refund.Id,()=>Find<PasswordBox>("ManagerPin") is not null);Layout();Screenshot("16-refund-approval");
        Check(!Get<Button>("CompleteRefund").IsEnabled,"refund completion requires explicit returned-money confirmation");
        Get<TextBox>("RefundReference").Text="Diserahkan kepada Bu Rini";Get<PasswordBox>("ManagerPin").Password="728491";Get<CheckBox>("RefundReturned").IsChecked=true;
        await Click("CompleteRefund",()=>Find<TextBox>("RefundAmount") is not null);
        Check((await store.ActiveCashAsync())!.Expected==42000&&(await store.SaleAsync(saved.Id))!.Payment.Amount==49000,"approved refund updates drawer and preserves sale");
        Screenshot("17-refund-history");
        window.CashNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>Find<Button>("CloseCash") is not null);
        await Click("CloseCash",()=>Find<TextBox>("CountedCash") is not null);
        Get<TextBox>("CountedCash").Text="41900";Get<TextBox>("ClosingNote").Text="Selisih hitungan seratus rupiah";
        Check(Get<TextBlock>("CashDifference").Text.Contains("-100"),"closing form shows shortage before commit");
        await Click("ReviewCloseCash",()=>Find<Button>("ConfirmCloseCash") is not null);
        await Click("ReviseCloseCash",()=>Find<TextBox>("CountedCash") is not null);
        Check(Get<TextBox>("CountedCash").Text=="41900","return from closing review preserves entered count");
        await Click("ReviewCloseCash",()=>Find<Button>("ConfirmCloseCash") is not null);Screenshot("18-close-cash-review");
        await Click("ConfirmCloseCash",()=>MainWindow.Descendants<Button>(root).Any(x=>AutomationProperties.GetAutomationId(x).StartsWith("CashDetail-")));
        Check(await store.ActiveCashAsync() is null&&(await store.CashHistoryAsync()).Single().Session.Difference==-100,"closing UI persists actual counted cash and variance");
        Screenshot("19-cash-history");
        // Reopen a real persisted draft in a new WPF window.
        var draft=OrderRules.Add(Order.New(),DummyCatalog.Products[0]) with { CustomerLabel="Pak Joko" };await store.SaveAsync(draft);
        window.Close();window=new MainWindow(store,true) { ShowInTaskbar=false };window.Show();AttachReviewRoot();
        await Until(()=>Find<TextBox>("CustomerName") is not null);Layout();
        Check(Get<TextBox>("CustomerName").Text=="Pak Joko","New app instance restores draft and customer name");
        Check(Get<TextBlock>("CartTotal").Text=="Rp5.000","New app instance restores cart amount");
        Get<TextBox>("CustomerName").Focus();
        Check(Get<TextBox>("CustomerName").MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),"Keyboard focus can move from customer name to next control");
        window.Close();
        var restored=(await store.ListAsync(OrderStatus.Draft)).Single();
        foreach(var product in DummyCatalog.Products.Skip(1))restored=OrderRules.Add(restored,product);
        await store.SaveAsync(restored);
        window=new MainWindow(store,true) { ShowInTaskbar=false };window.Show();AttachReviewRoot();
        await Until(()=>Find<ScrollViewer>("CartViewport") is not null);Layout();
        Check(Get<ScrollViewer>("CartViewport").ScrollableHeight>0,"Long order scrolls within its own viewport");
        Get<ScrollViewer>("CartViewport").ScrollToEnd();Layout();
        Check(Inside(Get<Button>("PayOrder"))&&Inside(Get<TextBlock>("CartTotal")),"Long order keeps total and payment action pinned");
        Screenshot("13-long-order");
        window.Close();
        var demoPath=App.DataPath(directory,true);
        Check(demoPath!=App.DataPath(directory,false)&&demoPath.Contains("DemoQris"),"demo database is separate from normal cashier database");
        var demoStore=new LocalStore(demoPath);await demoStore.InitializeAsync();
        var sounds=0;
        window=new MainWindow(demoStore,true,true,()=>sounds++) { ShowInTaskbar=false };
        window.Show();AttachReviewRoot();await Until(()=>Find<TextBox>("CustomerName") is not null);Layout();
        Check(window.DemoQris.Visibility==Visibility.Visible&&window.PreviewBadge.Text.Contains("BUKAN PEMBAYARAN NYATA"),"demo is visibly labeled");
        await Click("Product-NAS-001",()=>Get<TextBlock>("CartTotal").Text=="Rp5.000");
        var input=Get<TextBox>("CustomerName");input.Text="Bu Rini";window.Activate();input.Focus();
        var beforeFocus=Keyboard.FocusedElement;
        Check(ReferenceEquals(beforeFocus,input),"customer input has keyboard focus before payment arrival");
        var proof=new ProviderPayment(1,"fixture-qris",22500,DateTimeOffset.UtcNow);
        await window.ReceivePaymentAlertsAsync([proof]);Layout();
        Check(window.Toast.Visibility==Visibility.Visible&&window.ToastTitle.Text.StartsWith("SIMULASI"),"durable proof displays labeled passive popup");
        Check(window.ToastAmount.Text=="Rp22.500"&&sounds==1,"new proof shows amount and requests sound once");
        Check(Keyboard.FocusedElement==beforeFocus&&input.Text=="Bu Rini","payment arrival preserves typing and keyboard focus");
        Check(!window.Toast.IsHitTestVisible&&!window.Toast.Focusable&&!MainWindow.Descendants<Button>(window.Toast).Any(),"popup contains no buttons and never intercepts input");
        await window.ReceivePaymentAlertsAsync([proof]);Check(sounds==1,"duplicate proof never requests another sound");
        Check(await demoStore.CountAsync(OrderStatus.Completed)==0&&Get<TextBlock>("CartTotal").Text=="Rp5.000","proof neither completes nor changes active order");
        Check(window.Toast.TransformToAncestor(root).Transform(new Point(0,window.Toast.ActualHeight)).Y<=window.MainContent.TransformToAncestor(root).Transform(new Point()).Y,"popup stays above order panel and cashier workspace");
        Screenshot("14-qris-simulation");
        await Until(()=>window.Toast.Visibility==Visibility.Collapsed);
        await window.ReceivePaymentAlertsAsync([proof with { Sequence=2,TransactionId="late",PaidAt=DateTimeOffset.UtcNow.AddMinutes(-5) }]);
        Check(sounds==1&&window.Toast.Visibility==Visibility.Collapsed,"late proof is silent and does not reopen popup");
        window.DemoQris.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Until(()=>sounds==2);
        Check(window.ToastAmount.Text=="Rp5.000"&&await demoStore.CursorAsync()==3,"demo button uses durable inbox and active order amount");
        window.Close();
        window=new MainWindow(demoStore,true,true,()=>throw new InvalidOperationException("fixture audio unavailable")) { ShowInTaskbar=false };
        window.Show();AttachReviewRoot();await Until(()=>Find<TextBox>("CustomerName") is not null);
        await window.ReceivePaymentAlertsAsync([proof with { Sequence=4,TransactionId="audio-failure",PaidAt=DateTimeOffset.UtcNow }]);
        Check(window.Toast.Visibility==Visibility.Visible,"audio failure does not suppress popup");
        await Until(()=>window.Toast.Visibility==Visibility.Collapsed);
        Check(window.Toast.Visibility==Visibility.Collapsed,"audio failure does not prevent popup expiry");
        await Click("PayOrder",()=>Find<TextBox>("OpeningCash") is not null);Layout();Screenshot("20-open-cash");
        Check(Get<TextBox>("OpeningCash").Text=="","first payment asks for actual opening cash, no silent default");
        Get<TextBox>("OpeningCash").Text="100000";
        await Click("ConfirmOpenCash",()=>Find<TextBox>("Tendered") is not null);
        Check((await demoStore.ActiveCashAsync())!.Expected==100000&&Get<TextBlock>("CartTotal").Text=="Rp5.000","opening cash returns to intact payment order");
    }
    private static void AttachReviewRoot()
    {
        root=(FrameworkElement)window.Content;
        window.Content=null;
        root.Width=width-40;root.Height=height-22;
        // The CI desktop may be only 1024px wide. An unbounded Canvas keeps that
        // host from applying a layout clip to the independently sized review surface.
        var host=new Canvas();host.Children.Add(root);window.Content=host;
    }
    private static void Layout()
    {
        // Explicit constraints keep the hosted window's display size from overriding
        // a test arrange during SizeChanged/UpdateLayout callbacks.
        root.Width=width-40;root.Height=height-22;
        root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
        if(Math.Abs(root.ActualWidth-(width-40))>1||Math.Abs(root.ActualHeight-(height-22))>1)
            throw new Exception($"Host did not honor requested layout size: {root.ActualWidth}x{root.ActualHeight}");
        var clip=VisualTreeHelper.GetClip(root);
        if(clip is not null&&!clip.Bounds.Contains(new Rect(0,0,root.ActualWidth,root.ActualHeight)))
            throw new Exception("Host clipped the review surface.");
    }
    private static bool Inside(FrameworkElement element)
    {
        var p=element.TransformToAncestor(root).Transform(new Point());
        return p.X>=0&&p.Y>=0&&p.X+element.ActualWidth<=root.ActualWidth+1&&p.Y+element.ActualHeight<=root.ActualHeight+1;
    }
    private static bool VisibleWithinParents(FrameworkElement element)
    {
        if(!element.IsVisible||!Inside(element))return false;
        for(DependencyObject? parent=VisualTreeHelper.GetParent(element);parent is not null&&parent!=root;parent=VisualTreeHelper.GetParent(parent))
        {
            if(parent is not FrameworkElement frame)continue;
            var bounds=element.TransformToAncestor(frame).TransformBounds(new Rect(0,0,element.ActualWidth,element.ActualHeight));
            if(bounds.Left<-.5||bounds.Top<-.5||bounds.Right>frame.ActualWidth+.5||bounds.Bottom>frame.ActualHeight+.5)return false;
        }
        return true;
    }
    private static T? Find<T>(string id) where T:DependencyObject => MainWindow.Descendants<T>(root).FirstOrDefault(x=>AutomationProperties.GetAutomationId(x)==id);
    private static T Get<T>(string id) where T:DependencyObject => Find<T>(id)??throw new Exception("Missing UI element: "+id);
    private static async Task Click(string id,Func<bool> done)
    {
        // Rendering can precede the final asynchronous status update. Wait until
        // Run has released its action gate before starting or completing a test step.
        await Until(()=>window.Navigation.IsEnabled);
        var button=Get<Button>(id);Check(button.IsEnabled,"Action enabled: "+id);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>window.Navigation.IsEnabled&&done());Layout();
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
        Layout();foreach(var element in MainWindow.Descendants<UIElement>(root))element.BeginAnimation(UIElement.OpacityProperty,null);
        var bitmap=new RenderTargetBitmap((int)width,(int)height,96,96,PixelFormats.Pbgra32);
        var background=new DrawingVisual();using(var drawing=background.RenderOpen())drawing.DrawRectangle(window.Background,null,new Rect(0,0,width,height));
        bitmap.Render(background);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(artifacts,name+".png"));encoder.Save(file);
    }
}
