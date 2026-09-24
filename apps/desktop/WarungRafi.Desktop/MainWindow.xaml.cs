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

    public MainWindow() : this(new LocalStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WarungRafi","warung-rafi.db")),false) { }
    internal MainWindow(LocalStore storage, bool isolatedPreview)
    {
        store=storage;this.isolatedPreview=isolatedPreview;
        InitializeComponent();
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
        var count=await store.PendingCountAsync();
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
                await sync.SendOutboxAsync(token);await sync.FetchCatalogAsync(token);
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
            catch(Exception) { if(!busy)StatusText.Text="Data lokal aman · Pengiriman atau katalog menunggu koneksi"; }
            try { await Task.Delay(page=="payment"?3000:12000,token); } catch(OperationCanceledException){break;}
        }
    }
    private void ShowNextToast()
    {
        if(toastTimer.IsEnabled||notifications.Count==0)return;
        var proof=notifications.Dequeue();
        if(DateTimeOffset.UtcNow-proof.PaidAt>TimeSpan.FromSeconds(60)){ShowNextToast();return;}
        ToastAmount.Text=Money.Format(proof.Amount);ToastTime.Text=$"Pukul {proof.PaidAt.ToOffset(TimeSpan.FromHours(7)):HH.mm}";
        Toast.Visibility=Visibility.Visible;Reveal(Toast);
        if(!isolatedPreview)System.Media.SystemSounds.Asterisk.Play();toastTimer.Start();
    }
}
