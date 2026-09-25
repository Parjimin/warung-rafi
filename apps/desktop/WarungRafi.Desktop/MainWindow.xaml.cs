using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WarungRafi.Core;
using WarungRafi.Storage;

namespace WarungRafi.Desktop;

public partial class MainWindow : Window
{
    private readonly LocalStore store;
    private readonly bool isolatedPreview;
    private readonly bool simulatePayments;
    private readonly Action playPaymentSound;
    private readonly SemaphoreSlim actions = new(1,1);
    private readonly CancellationTokenSource closing = new();
    private Order current=Order.New();
    private Product[] catalog=DummyCatalog.Products;
    private string category="Nasi", page="sell", customerName="", search="";
    private bool ready, busy, nameDirty, cartExpanded;
    private PaymentMethod method=PaymentMethod.Cash;
    private TextBox? tenderedInput;
    private TextBlock? changeLabel;
    private Button? completeButton;
    private string tendered="";
    private readonly Queue<ProviderPayment> notifications=new();
    private readonly DispatcherTimer toastTimer=new() { Interval=TimeSpan.FromSeconds(6) };
    private readonly DispatcherTimer nameTimer=new() { Interval=TimeSpan.FromMilliseconds(450) };
    private CompletedSale? lastSale;
    private Task? imagePrefetch;

    public MainWindow() : this(new LocalStore(App.DataPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),false)),false) { }
    internal MainWindow(LocalStore storage, bool isolatedPreview, bool simulatePayments=false, Action? paymentSound=null)
    {
        if(simulatePayments&&!isolatedPreview)throw new ArgumentException("Simulasi harus terisolasi.");
        store=storage;this.isolatedPreview=isolatedPreview;this.simulatePayments=simulatePayments;
        playPaymentSound=paymentSound??(()=> { if(!isolatedPreview||simulatePayments)System.Media.SystemSounds.Asterisk.Play(); });
        InitializeComponent();
        if(simulatePayments)
        {
            Title="Warung Rafi — SIMULASI QRIS";
            PreviewBadge.Text="SIMULASI · BUKAN PEMBAYARAN NYATA";
            DemoQris.Visibility=Visibility.Visible;
            ToastTitle.Text="SIMULASI · QRIS diterima";
        }
        RootLayout.SizeChanged+=(_,_)=>HeaderDate.Visibility=RootLayout.ActualWidth<1080?Visibility.Collapsed:Visibility.Visible;
        DateLabel.Text=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).ToString("ddd, d MMM yyyy",CultureInfo.GetCultureInfo("id-ID"));
        toastTimer.Tick+=(_,_)=> { Toast.Visibility=Visibility.Collapsed;toastTimer.Stop();ShowNextToast(); };
        nameTimer.Tick+=async(_,_)=> { nameTimer.Stop();if(busy)nameTimer.Start();else await Run(()=>Task.CompletedTask); };
    }
    private async void OnLoaded(object sender,RoutedEventArgs e)
    {
        if(ready)return;
        await Run(async()=>
        {
            await store.InitializeAsync();catalog=(await store.CatalogAsync()).Products;
            var drafts=await store.ListAsync(OrderStatus.Draft);
            if(drafts.Length>0)SetCurrent(drafts[0]);
            ready=true;RenderSelling();await UpdateStatus();
        });
        if(ready&&!isolatedPreview)_=ConnectAsync(closing.Token);
    }
    private async Task Run(Func<Task> action)
    {
        if(!await actions.WaitAsync(0))return;
        busy=true;Navigation.IsEnabled=false;
        try
        {
            if(ready&&nameDirty&&current.Status is OrderStatus.Draft or OrderStatus.Held)await Save(current);
            await action();
        }
        catch(Exception ex) { StatusText.Text=$"Belum berhasil: {ex.Message}";StatusText.ToolTip=StatusText.Text; }
        finally { busy=false;Navigation.IsEnabled=true;actions.Release(); }
    }
    private async void OnClosing(object? sender,CancelEventArgs e)
    {
        if(busy){e.Cancel=true;StatusText.Text="Tunggu penyimpanan selesai sebelum menutup.";return;}
        if(nameDirty)
        {
            e.Cancel=true;await Run(()=>Task.CompletedTask);
            if(!nameDirty)Close();return;
        }
        closing.Cancel();toastTimer.Stop();nameTimer.Stop();
    }
    private void SetCurrent(Order order)
    {
        current=order;customerName=order.CustomerLabel;nameDirty=false;nameTimer.Stop();
    }
    private async Task Save(Order next)
    {
        var snapshot=customerName;
        current=await store.SaveAsync(next with { CustomerLabel=snapshot });
        nameDirty=customerName!=snapshot;
        await UpdateStatus();
    }
    private async Task UpdateStatus()
    {
        var count=await store.PendingCountAsync()+await store.PendingFinanceCountAsync();
        StatusText.Text=count==0?"Tersimpan di laptop · Semua perubahan sudah dikirim":$"Tersimpan di laptop · {count} perubahan menunggu dikirim";
        StatusText.ToolTip=StatusText.Text;
        HeldNav.Content=$"Ditunda ({await store.CountAsync(OrderStatus.Held)})";
    }
    private async Task Print(CompletedSale sale,bool copy)
    {
        if(isolatedPreview)return;
        try { await ReceiptPrinter.PrintAsync(sale,copy);await store.RecordPrintAsync(sale.Order.Id,copy,"submitted");StatusText.Text="Pesanan tersimpan · Struk dikirim ke printer"; }
        catch(Exception ex)
        {
            try { await store.RecordPrintAsync(sale.Order.Id,copy,"uncertain"); } catch { }
            StatusText.Text=$"Pesanan tersimpan. Struk belum dipastikan tercetak: {ex.Message}";
        }
        StatusText.ToolTip=StatusText.Text;
    }
    private async void ShowSelling(object sender,RoutedEventArgs e)=>await Run(()=>
    {
        if(current.Status is OrderStatus.Completed or OrderStatus.Cancelled)SetCurrent(Order.New());
        RenderSelling();return Task.CompletedTask;
    });
    private async void ShowHeld(object sender,RoutedEventArgs e)=>await Run(()=>RenderOrders(true));
    private async void ShowHistory(object sender,RoutedEventArgs e)=>await Run(()=>RenderOrders(false));
    private async void ShowCash(object sender,RoutedEventArgs e)=>await Run(RenderCash);

    private async Task ConnectAsync(CancellationToken token)
    {
        var sync=RemoteSync.FromEnvironment(store);if(sync is null)return;
        await Task.WhenAll(PollPaymentsAsync(sync,token),SyncDataAsync(sync,token));
    }
    private async Task PollPaymentsAsync(RemoteSync sync,CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            try { QueuePaymentAlerts(await sync.FetchPaymentsAsync(token)); }
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch(Exception) { if(!busy)StatusText.Text="Data lokal aman · QRIS online belum dapat diperiksa"; }
            try { await Task.Delay(page=="payment"?3000:12000,token); } catch(OperationCanceledException){break;}
        }
    }
    private async Task SyncDataAsync(RemoteSync sync,CancellationToken token)
    {
        while(!token.IsCancellationRequested)
        {
            try
            {
                await sync.SendOutboxAsync(token);
                await sync.SendFinanceAsync(token);
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch(Exception) { if(!busy)StatusText.Text="Data lokal aman · Pengiriman menunggu koneksi atau pemeriksaan pengelola"; }
            try
            {
                await sync.FetchCatalogAsync(token);
                var snapshot=await store.CatalogAsync();
                if(imagePrefetch is null||imagePrefetch.IsCompleted)
                    imagePrefetch=ProductImages.PrefetchAsync(snapshot.Products.Where(x=>x.ImageUrl is not null).Select(x=>x.ImageUrl!),token);
                if(!busy)
                {
                    await UpdateStatus();
                    if(page=="sell" && current.Lines.Length==0)
                    {
                        var latest=(await store.CatalogAsync()).Products;
                        if(!catalog.SequenceEqual(latest)){catalog=latest;RefreshProducts();}
                    }
                }
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch(Exception) { if(!busy)StatusText.Text="Data lokal aman · Katalog menunggu koneksi"; }
            try { await sync.ReportStatusAsync(token); }
            catch(OperationCanceledException) when(token.IsCancellationRequested){break;}
            catch(Exception) { /* Monitoring failure must not interrupt local transactions. */ }
            try { await Task.Delay(12000,token); } catch(OperationCanceledException){break;}
        }
    }
    internal async Task ReceivePaymentAlertsAsync(ProviderPayment[] incoming)
    {
        QueuePaymentAlerts(await store.ReceivePaymentsAsync(incoming));
    }
    private void QueuePaymentAlerts(ProviderPayment[] incoming)
    {
        if(closing.IsCancellationRequested)return;
        foreach(var proof in incoming)
            if(PaymentAlertPolicy.IsFresh(proof,DateTimeOffset.UtcNow))notifications.Enqueue(proof);
        ShowNextToast();
    }
    private async void SimulateQris(object sender,RoutedEventArgs e)
    {
        if(!simulatePayments)return;
        await Run(async()=>
        {
            var amount=current.Lines.Length==0?22500:current.Total;
            await ReceivePaymentAlertsAsync([new(await store.CursorAsync()+1,"demo-"+Guid.NewGuid().ToString("N"),amount,DateTimeOffset.UtcNow)]);
        });
    }
    private void ShowNextToast()
    {
        if(toastTimer.IsEnabled)return;
        while(notifications.TryDequeue(out var proof))
        {
            if(!PaymentAlertPolicy.IsFresh(proof,DateTimeOffset.UtcNow))continue;
            ToastAmount.Text=Money.Format(proof.Amount);ToastTime.Text=$"Pukul {proof.PaidAt.ToOffset(TimeSpan.FromHours(7)):HH.mm}";
            Toast.Visibility=Visibility.Visible;Reveal(Toast);toastTimer.Start();
            try { playPaymentSound(); } catch { /* Audio failure must not interrupt the cashier or toast expiry. */ }
            break;
        }
    }
}
