using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WarungRafi.Core;
using WarungRafi.Storage;

namespace WarungRafi.Desktop;

public partial class MainWindow : Window
{
    private readonly LocalStore store = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WarungRafi","warung-rafi.db"));
    private readonly SemaphoreSlim actions = new(1,1);
    private readonly CancellationTokenSource closing = new();
    private Order current=Order.New();
    private Product[] catalog=DummyCatalog.Products;
    private string category="Nasi";
    private string page="sell";
    private bool ready, busy;
    private PaymentMethod method=PaymentMethod.Cash;
    private TextBox? tenderedInput;
    private TextBlock? changeLabel;
    private string tendered="";
    private readonly Queue<ProviderPayment> notifications=new();
    private readonly DispatcherTimer toastTimer=new() { Interval=TimeSpan.FromSeconds(6) };
    private CompletedSale? lastSale;

    public MainWindow()
    {
        InitializeComponent();
        toastTimer.Tick+=(_,_)=> { Toast.Visibility=Visibility.Hidden; toastTimer.Stop(); ShowNextToast(); };
    }

    private async void OnLoaded(object sender,RoutedEventArgs e)
    {
        await Run(async()=>
        {
            await store.InitializeAsync();
            catalog=(await store.CatalogAsync()).Products;
            var drafts=await store.ListAsync(OrderStatus.Draft);
            if(drafts.Length>0) current=drafts[0];
            ready=true; RenderSelling(); await UpdateStatus();
        });
        if(ready) _=ConnectAsync(closing.Token);
    }

    private async Task Run(Func<Task> action)
    {
        if(!await actions.WaitAsync(0)) return;
        busy=true; MainContent.IsEnabled=false;
        try { await action(); }
        catch(Exception ex) { StatusText.Text=$"Belum berhasil: {ex.Message}"; }
        finally { busy=false; MainContent.IsEnabled=true; actions.Release(); }
    }

    private void OnClosing(object? sender,CancelEventArgs e)
    {
        if(busy) { e.Cancel=true; StatusText.Text="Tunggu penyimpanan selesai sebelum menutup."; return; }
        closing.Cancel(); toastTimer.Stop();
    }

    private async Task Save(Order next)
    {
        current=await store.SaveAsync(next);
        await UpdateStatus();
    }
    private async Task UpdateStatus()
    {
        var count=await store.PendingCountAsync();
        StatusText.Text=$"Tersimpan di laptop · {count} perubahan menunggu dikirim";
        HeldNav.Content=$"Pesanan Ditunda ({(await store.ListAsync(OrderStatus.Held)).Length})";
    }

    private static TextBlock Text(string value,double size=20,bool bold=false) => new() { Text=value,TextWrapping=TextWrapping.Wrap,FontSize=size,FontWeight=bold?FontWeights.Bold:FontWeights.Normal,Margin=new Thickness(0,4,0,4) };
    private Button ActionButton(string label,Func<Task> action,bool primary=false)
    {
        var button=new Button { Content=label };
        if(primary) { button.Background=(Brush)FindResource("Forest");button.Foreground=Brushes.White; }
        button.Click+=async(_,_)=>await Run(action); return button;
    }
    private static ScrollViewer Scroll(UIElement content) => new() { Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,PanningMode=PanningMode.VerticalOnly };
    private static Border Surface(UIElement child) => new() { Child=child,Background=Brushes.White,CornerRadius=new CornerRadius(20),Padding=new Thickness(18),Margin=new Thickness(5) };
    private static Grid TwoColumns()
    {
        var grid=new Grid(); grid.ColumnDefinitions.Add(new() { Width=new GridLength(1.7,GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width=new GridLength(1,GridUnitType.Star),MinWidth=320 }); return grid;
    }

    private void RenderSelling()
    {
        if(!ready) return;
        page="sell";
        var grid=TwoColumns(); var left=new DockPanel(); var tabs=new WrapPanel();
        foreach(var name in new[]{"Nasi","Lauk","Sundukan","Minuman"})
        {
            var chosen=name;
            tabs.Children.Add(ActionButton(name,()=> { category=chosen;RenderSelling();return Task.CompletedTask; },category==name));
        }
        DockPanel.SetDock(tabs,Dock.Top);left.Children.Add(tabs);
        var cards=new WrapPanel();
        foreach(var product in catalog.Where(x=>x.Category==category))
        {
            var card=new StackPanel();
            var illustration=new Grid { Height=76 };
            illustration.Children.Add(Text(product.Category switch { "Nasi"=>"🍚","Lauk"=>"🍽","Sundukan"=>"🍢",_=>"☕" },44));
            if(product.ImageUrl is not null)
            {
                var image=new Image { Stretch=Stretch.UniformToFill,Height=76,Visibility=Visibility.Hidden };
                illustration.Children.Add(image);_=LoadPhoto(image,product.ImageUrl);
            }
            card.Children.Add(illustration);
            card.Children.Add(Text(product.Name,22,true)); card.Children.Add(Text(Money.Format(product.Price),21));
            if(!product.Available) card.Children.Add(Text("Habis",16));
            var button=ActionButton("",async()=> { await Save(OrderRules.Add(current,product)); RenderSelling(); });
            button.Content=card;button.Width=205;button.Height=196;button.IsEnabled=product.Available;cards.Children.Add(button);
        }
        left.Children.Add(Scroll(cards));grid.Children.Add(left);
        var right=Cart(true); Grid.SetColumn(right,1);grid.Children.Add(right);MainContent.Content=grid;
    }

    private static async Task LoadPhoto(Image image,string url)
    {
        var source=await ProductImages.GetAsync(url);
        if(source is not null) { image.Source=source;image.Visibility=Visibility.Visible; }
    }

    private Border Cart(bool editable)
    {
        var dock=new DockPanel();var header=new StackPanel();header.Children.Add(Text("Pesanan",25,true));header.Children.Add(Text(current.Number,13));
        DockPanel.SetDock(header,Dock.Top);dock.Children.Add(header);
        var bottom=new StackPanel();bottom.Children.Add(Text(current.TotalLabel,36,true));
        if(editable)
        {
            var label=new TextBox { Text=current.CustomerLabel,MaxLength=60,ToolTip="Nama panggilan (opsional)",Margin=new Thickness(0,8,0,8) };
            bottom.Children.Add(Text("Nama pelanggan (opsional)",14));bottom.Children.Add(label);
            var hold=ActionButton("Simpan Dulu",async()=>
            {
                await Save(OrderRules.Hold(current,label.Text));current=Order.New();RenderSelling();
            });hold.IsEnabled=current.Lines.Length>0;bottom.Children.Add(hold);
            var pay=ActionButton("Lanjut Bayar",()=> { tendered="";method=PaymentMethod.Cash;RenderPayment();return Task.CompletedTask; },true);
            pay.IsEnabled=current.Lines.Length>0;bottom.Children.Add(pay);
        }
        DockPanel.SetDock(bottom,Dock.Bottom);dock.Children.Add(bottom);
        var lines=new StackPanel();
        if(current.Lines.Length==0) lines.Children.Add(Text("Pilih makanan untuk mulai.",20));
        foreach(var line in current.Lines)
        {
            var panel=new StackPanel { Margin=new Thickness(0,8,0,12) };
            panel.Children.Add(Text(line.Name,21,true));panel.Children.Add(Text($"{line.Quantity} × {line.PriceLabel} = {line.SubtotalLabel}",17));
            if(editable)
            {
                var controls=new StackPanel { Orientation=Orientation.Horizontal };
                controls.Children.Add(ActionButton(line.Quantity==1?"Hapus":"−",async()=> { await Save(OrderRules.Reduce(current,line.ProductId));RenderSelling(); }));
                controls.Children.Add(ActionButton("+",async()=>
                {
                    var product=catalog.FirstOrDefault(p=>p.Id==line.ProductId)??new Product(line.ProductId,line.Name,line.Category,line.UnitPrice);
                    await Save(OrderRules.Add(current,product));RenderSelling();
                }));panel.Children.Add(controls);
            }
            lines.Children.Add(panel);
        }
        dock.Children.Add(Scroll(lines));return Surface(dock);
    }

    private void RenderPayment()
    {
        page="payment";var grid=TwoColumns();grid.Children.Add(Cart(false));
        var panel=new StackPanel();panel.Children.Add(Text("Pembayaran",28,true));
        var methods=new WrapPanel();
        methods.Children.Add(ActionButton("Tunai",()=> { method=PaymentMethod.Cash;RenderPayment();return Task.CompletedTask; },method==PaymentMethod.Cash));
        methods.Children.Add(ActionButton("QRIS",()=> { method=PaymentMethod.QrisManual;RenderPayment();return Task.CompletedTask; },method==PaymentMethod.QrisManual));panel.Children.Add(methods);
        if(method==PaymentMethod.Cash)
        {
            panel.Children.Add(Text("Uang diterima",18));tenderedInput=new TextBox { Text=tendered };
            tenderedInput.TextChanged+=(_,_)=> { tendered=tenderedInput.Text;UpdateChange(); };panel.Children.Add(tenderedInput);
            var quick=new WrapPanel();
            foreach(var value in new[]{current.Total,20000L,50000L,100000L})
            {
                var chosen=value;quick.Children.Add(ActionButton(value==current.Total?"Uang Pas":Money.Format(value),()=> { tenderedInput.Text=chosen.ToString(CultureInfo.InvariantCulture);return Task.CompletedTask; }));
            }
            panel.Children.Add(quick);var keys=new System.Windows.Controls.Primitives.UniformGrid { Columns=3 };
            foreach(var key in new[]{"1","2","3","4","5","6","7","8","9","C","0","⌫"})
            {
                var chosen=key;keys.Children.Add(ActionButton(key,()=>
                {
                    tenderedInput.Text=chosen=="C"?"":chosen=="⌫"?(tendered.Length>0?tendered[..^1]:""):tendered+chosen;
                    return Task.CompletedTask;
                }));
            }
            panel.Children.Add(keys);changeLabel=Text("",23,true);panel.Children.Add(changeLabel);UpdateChange();
        }
        else
        {
            panel.Children.Add(Text(current.TotalLabel,40,true));
            panel.Children.Add(Text("Minta pelanggan scan QR yang dipajang di meja.",24));
            panel.Children.Add(Text("Pastikan pembayaran diterima sebelum menyelesaikan pesanan.",18));
        }
        panel.Children.Add(ActionButton("Selesaikan Pesanan",Complete,true));
        panel.Children.Add(ActionButton("Ubah Pesanan",()=> { RenderSelling();return Task.CompletedTask; }));
        var right=Surface(Scroll(panel));Grid.SetColumn(right,1);grid.Children.Add(right);MainContent.Content=grid;
    }

    private void UpdateChange()
    {
        if(changeLabel is null) return;
        try
        {
            var difference=Money.ParseInput(tendered)-current.Total;
            changeLabel.Text=difference<0?$"Kurang {Money.Format(-difference)}":$"Kembalian {Money.Format(difference)}";
        }
        catch(ArgumentException ex) { changeLabel.Text=ex.Message; }
    }
    private async Task Complete()
    {
        lastSale=await store.CompleteAsync(current.Id,current.Version,method,method==PaymentMethod.Cash?Money.ParseInput(tendered):0);
        current=lastSale.Order;page="success";
        var panel=new StackPanel { MaxWidth=620,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center };
        panel.Children.Add(Text("Pesanan tersimpan",34,true));panel.Children.Add(Text(current.Number,18));
        panel.Children.Add(Text(method==PaymentMethod.Cash?$"Kembalian {Money.Format(lastSale.Payment.Change)}":$"QRIS {current.TotalLabel}",42,true));
        panel.Children.Add(ActionButton("Pesanan Baru",()=> { current=Order.New();RenderSelling();return Task.CompletedTask; },true));
        panel.Children.Add(ActionButton("Cetak Ulang",()=>Print(lastSale,true)));
        MainContent.Content=Surface(panel);await UpdateStatus();_=Print(lastSale,false);
    }
    private async Task Print(CompletedSale sale,bool copy)
    {
        try { await ReceiptPrinter.PrintAsync(sale,copy);await store.RecordPrintAsync(sale.Order.Id,copy,"submitted");StatusText.Text="Pesanan tersimpan · Struk dikirim ke printer"; }
        catch(Exception ex)
        {
            try { await store.RecordPrintAsync(sale.Order.Id,copy,"uncertain"); } catch { /* Preserve the completed sale even when print logging fails. */ }
            StatusText.Text=$"Pesanan tersimpan. Struk belum dipastikan tercetak: {ex.Message}";
        }
    }

    private async void ShowSelling(object sender,RoutedEventArgs e)=>await Run(()=>
    {
        if(current.Status is OrderStatus.Completed or OrderStatus.Cancelled)current=Order.New();RenderSelling();return Task.CompletedTask;
    });
    private async void ShowHeld(object sender,RoutedEventArgs e)=>await Run(()=>RenderOrders(true));
    private async void ShowHistory(object sender,RoutedEventArgs e)=>await Run(()=>RenderOrders(false));
    private async Task RenderOrders(bool held)
    {
        if(!ready)return;page=held?"held":"history";
        var panel=new StackPanel();panel.Children.Add(Text(held?"Pesanan Ditunda":"Riwayat Pesanan",28,true));
        panel.Children.Add(Text("Menampilkan hingga 1.000 pesanan terbaru.",14));
        var orders=await store.ListAsync(held?[OrderStatus.Held,OrderStatus.Draft]:[OrderStatus.Completed,OrderStatus.Cancelled]);
        foreach(var order in orders)
        {
            var row=new StackPanel();row.Children.Add(Text($"{order.Number} · {order.CustomerLabel}",22,true));
            row.Children.Add(Text($"{order.Summary} · {order.TotalLabel}",19));
            if(held)
            {
                row.Children.Add(ActionButton("Buka Pesanan",async()=>
                {
                    if(current.Id!=order.Id && current.Status==OrderStatus.Draft && current.Lines.Length>0) await Save(OrderRules.Hold(current,current.CustomerLabel));
                    current=await store.SaveAsync(OrderRules.Resume(order));RenderSelling();await UpdateStatus();
                },true));
                row.Children.Add(ActionButton("Batalkan",async()=>
                {
                    if(MessageBox.Show("Batalkan pesanan ini? Alasan: pelanggan batal.","Batalkan Pesanan",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
                    await store.SaveAsync(OrderRules.Cancel(order,"Pelanggan batal"));
                    if(current.Id==order.Id)current=Order.New();await RenderOrders(true);await UpdateStatus();
                }));
            }
            else if(order.Status==OrderStatus.Completed)
                row.Children.Add(ActionButton("Cetak Ulang",async()=> { var sale=await store.SaleAsync(order.Id);if(sale is not null)await Print(sale,true); }));
            panel.Children.Add(Surface(row));
        }
        if(orders.Length==0)panel.Children.Add(Text("Belum ada pesanan."));MainContent.Content=Scroll(panel);
    }
    private async void ShowCash(object sender,RoutedEventArgs e)=>await Run(async()=>
    {
        if(!ready)return;page="cash";var now=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).Date;
        var summary=await store.DailySalesAsync(now);
        var cash=summary.Cash;var qris=summary.Qris;
        var panel=new StackPanel();panel.Children.Add(Text("Ringkasan hari ini",30,true));
        panel.Children.Add(Text($"Tunai tercatat: {Money.Format(cash)}",28));panel.Children.Add(Text($"QRIS dicatat kasir: {Money.Format(qris)}",28));
        panel.Children.Add(Text("Ringkasan tanggal WIB. Belum termasuk modal, pengeluaran, refund, atau biaya QRIS.",18));MainContent.Content=Surface(panel);
    });

    private async Task ConnectAsync(CancellationToken token)
    {
        var sync=RemoteSync.FromEnvironment(store);
        if(sync is null)return;
        while(!token.IsCancellationRequested)
        {
            try
            {
                var incoming=await sync.FetchPaymentsAsync(token);
                foreach(var proof in incoming)
                    if(DateTimeOffset.UtcNow-proof.PaidAt is var age && age>=TimeSpan.Zero && age<=TimeSpan.FromSeconds(60))notifications.Enqueue(proof);
                ShowNextToast();

            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch(Exception) { if(!busy)StatusText.Text="Data lokal aman · Layanan online belum dapat diperiksa"; }
            try
            {
                await sync.SendOutboxAsync(token);
                await sync.FetchCatalogAsync(token);
                if(!busy)
                {
                    await UpdateStatus();
                    if(page=="sell" && current.Lines.Length==0)
                    {
                        var latest=(await store.CatalogAsync()).Products;
                        if(!catalog.SequenceEqual(latest)) { catalog=latest;RenderSelling(); }
                    }
                }
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch(Exception) { if(!busy)StatusText.Text="Data lokal aman · Pengiriman atau katalog menunggu koneksi"; }
            try { await Task.Delay(page=="payment"?3000:12000,token); } catch(OperationCanceledException){break;}
        }
    }
    private void ShowNextToast()
    {
        if(toastTimer.IsEnabled||notifications.Count==0)return;
        var proof=notifications.Dequeue();
        if(DateTimeOffset.UtcNow-proof.PaidAt>TimeSpan.FromSeconds(60)) { ShowNextToast();return; }
        ToastAmount.Text=Money.Format(proof.Amount);
        ToastTime.Text=$"Pukul {proof.PaidAt.ToOffset(TimeSpan.FromHours(7)):HH.mm}";
        Toast.Visibility=Visibility.Visible;System.Media.SystemSounds.Asterisk.Play();toastTimer.Start();
    }
}
