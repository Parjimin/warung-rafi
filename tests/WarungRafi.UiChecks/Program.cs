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
    private static void VerifyReceiptDesign()
    {
        var now=new DateTimeOffset(2026,10,3,15,46,0,TimeSpan.Zero);
        var order=Order.New("12345678abcdef",now) with { Status=OrderStatus.Completed,Lines=[
            new("rice","Nasi Putih","Nasi",3000,2),new("chicken","Ayam Ungkep","Lauk",7000,2),new("tea","Teh","Minuman",3000,2)] };
        var sale=new CompletedSale(order,new Payment(order.Id,PaymentMethod.Cash,26000,30000,4000,now));
        const double mm=96d/25.4;
        var doc=ReceiptPrinter.CreateDocument(sale,true,58*mm,1000,ReceiptPrinter.SafePadding(58*mm,1000,0,0,58*mm,1000));
        var content=new System.Windows.Documents.TextRange(doc.ContentStart,doc.ContentEnd).Text;
        foreach(var expected in new[]{"WARUNG RAFI","Nasi Sayur Murah","SALINAN","Rp26.000","Rp4.000","+62 851-5650-4119","@asoyyy_group"})
            Check(content.Contains(expected),"Designed receipt retains "+expected);
        var paginator=((System.Windows.Documents.IDocumentPaginatorSource)doc).DocumentPaginator;
        paginator.ComputePageCount();Check(paginator.PageCount==1,"Three-item receipt fits one thermal page");
        var page=paginator.GetPage(0);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(58*mm*2),2000,192,192,PixelFormats.Pbgra32);
        bitmap.Render(page.Visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output=File.Create(Path.Combine(artifacts,"22-receipt-design.png"));encoder.Save(output);
    }
    private static void VerifyReceiptMargins()
    {
        const double mm=96d/25.4;
        foreach(var area in new[]{(0d,58d),(5d,48d),(7d,44d)})
        {
            var margin=ReceiptPrinter.SafePadding(58*mm,1000,area.Item1*mm,0,area.Item2*mm,1000);
            Check(margin.Left>=5*mm&&margin.Left>=area.Item1*mm+mm,"Receipt starts beyond left hardware margin");
            Check(58*mm-margin.Right<=(area.Item1+area.Item2)*mm-mm+0.001,"Receipt ends before right hardware margin");
            Check(58*mm-margin.Left-margin.Right<=48*mm+0.001,"58 mm receipt uses at most 48 mm of text");
        }
        var rejected=false;
        try { ReceiptPrinter.SafePadding(58*mm,1000,0,0,15*mm,1000); }
        catch(InvalidOperationException) { rejected=true; }
        Check(rejected,"Unusable printer media rejected instead of clipping the sale");
    }
    private static async Task Verify()
    {
        VerifyReceiptMargins();
        Directory.CreateDirectory(artifacts);
        VerifyReceiptDesign();
        var store=new LocalStore(Path.Combine(directory,"test.db"),ManagerPin.Hash("728491"));await store.InitializeAsync();
        window=new MainWindow(store,true) { Width=1320,Height=850,ShowInTaskbar=false };
        window.Show();AttachReviewRoot();
        await Until(()=>Find<Button>("PayOrder") is not null);
        Layout();
        Check(!Get<Button>("PayOrder").IsEnabled,"Empty order cannot enter payment");Screenshot("01-empty");
        VerifyFullMenu();
        Check(window.PreviewNotice.Visibility==Visibility.Collapsed,"Normal cashier has no preview label");
        Check(Get<Image>("BrandLogo").Source is not null,"Application logo is bundled");
        Check(window.SellNav.TransformToAncestor(root).Transform(new Point()).Y<80,"Cashier navigation is at top");
        var originalProductButton=Get<Button>("Product-NAS-001");
        foreach(var id in new[]{"NAS-001","NAS-002","NAS-003","NAS-004"})await Click("Product-"+id,()=>Get<ScrollViewer>("CartViewport").Content is StackPanel p&&p.Children.Count==Array.IndexOf(new[]{"NAS-001","NAS-002","NAS-003","NAS-004"},id)+1);
        Check(ReferenceEquals(originalProductButton,Get<Button>("Product-NAS-001")),"adding products does not rebuild the product grid");
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
        Check(Inside(window.SellNav)&&Inside(window.HistoryNav),"Top navigation remains inside the working area");
        var first=Get<Button>("Product-NAS-001").TransformToAncestor(root).Transform(new Point());
        var second=Get<Button>("Product-NAS-002").TransformToAncestor(root).Transform(new Point());
        var third=Get<Button>("Product-NAS-003").TransformToAncestor(root).Transform(new Point());
        var fourth=Get<Button>("Product-NAS-004").TransformToAncestor(root).Transform(new Point());
        Check(Math.Abs(first.Y-second.Y)<1&&Math.Abs(first.Y-third.Y)<1&&third.X>second.X&&Math.Abs(first.Y-fourth.Y)<1&&fourth.X>third.X,"Menu grid fits four columns at 1280x720");
        Check(Get<Button>("Product-NAS-001").ActualWidth>=150&&Get<Button>("Product-NAS-001").ActualHeight>=56,"Compact menu cards retain large touch targets");
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
        window.HistoryNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Until(()=>Find<Button>("Detail-"+saved.Id) is not null);
        await Click("Detail-"+saved.Id,()=>Find<Button>("OrderRefund") is not null);
        await Click("OrderRefund",()=>Find<TextBox>("RefundAmount") is not null);
        Get<TextBox>("RefundAmount").Text="5000";Get<TextBox>("RefundReason").Text="Satu nasi dikembalikan";
        await Click("RequestRefund",()=>MainWindow.Descendants<Button>(root).Any(x=>AutomationProperties.GetAutomationId(x).StartsWith("Resolve-")));
        var refund=(await store.RefundsAsync(saved.Id)).Single();
        await Click("Resolve-"+refund.Id,()=>Find<PasswordBox>("ManagerPin") is not null);Layout();Screenshot("16-refund-approval");
        Check(!Get<Button>("CompleteRefund").IsEnabled,"refund completion requires explicit returned-money confirmation");
        Get<TextBox>("RefundReference").Text="Diserahkan kepada Bu Rini";Get<PasswordBox>("ManagerPin").Password="728491";Get<CheckBox>("RefundReturned").IsChecked=true;
        await Click("CompleteRefund",()=>Find<TextBox>("RefundAmount") is not null);
        Screenshot("17-refund-history");
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
        Check(window.Toast.HorizontalAlignment==HorizontalAlignment.Left&&window.Toast.VerticalAlignment==VerticalAlignment.Bottom,"QRIS toast is bottom-left");
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
        await Click("PayOrder",()=>Find<TextBox>("Tendered") is not null);
        Check(Find<TextBox>("OpeningCash") is null&&Get<TextBlock>("CartTotal").Text=="Rp5.000","payment opens directly without cash setup");

    }
    private static void VerifyFullMenu()
    {
        // Exercise the actual 32-item workload without changing the test database.
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var field=typeof(MainWindow).GetField("catalog",flags)!;
        var category=typeof(MainWindow).GetField("category",flags)!;
        var render=typeof(MainWindow).GetMethod("RenderSelling",flags)!;
        var original=field.GetValue(window);var originalCategory=category.GetValue(window);
        var names=new[]{"Bakso Balado","Ceker Bacem","Tahu Opor","Mie Goreng oseng","Oseng Mihun","Tumis Kangkung","Toge Tahu Oseng","Oseng Tempe","Trancam","Tempura Barbeque","Sosis Barbeque","Sayur Bayem","Sayur Sop","Sambel Goreng Kentang","Terong Balado","Telur Dadar","Telur balado","Pindang Balado","Rempelo Ati Ungkep","Telur Opor","Soto","Ayam Ungkep","Sayap Bacem","Paha Bacem","Ayam Opor","Tempe Mendoan","Tahu Goreng","Bakwan Goreng","Tahu Bacem","Tempe Bacem","Tahu Isi","Pisang Goreng"};
        try
        {
            field.SetValue(window,names.Select((n,i)=>new Product("REVIEW-"+i,n,"Lauk",2000)).ToArray());
            category.SetValue(window,"Lauk");render.Invoke(window,null);
            foreach(var size in new[]{(1280d,720d),(1536d,864d)})
            {
                width=size.Item1;height=size.Item2;Layout();
                var cards=Enumerable.Range(0,32).Select(i=>Get<Button>("Product-REVIEW-"+i)).ToArray();
                var rows=cards.Select(c=>Math.Round(c.TransformToAncestor(root).Transform(new Point()).Y)).Distinct().Count();
                Check(rows<=8,"32 menus fit in at most eight rows at "+width);
                if(width>=1536)Check(rows==8,"Wide layout shows 32 menus in eight rows");
                Check(cards.All(c=>c.ActualWidth>=150&&c.ActualHeight>=56),"Full menu retains usable touch targets");
                Screenshot("21-full-menu-"+width+"x"+height);

            }
        }
        finally {field.SetValue(window,original);category.SetValue(window,originalCategory);width=1280;height=720;render.Invoke(window,null);Layout();}
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
