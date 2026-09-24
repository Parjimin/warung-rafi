using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WarungRafi.Core;

namespace WarungRafi.Desktop;

public partial class MainWindow
{
    private async Task RenderOrders(bool held)
    {
        if(!ready)return;
        var orders=await store.ListAsync(held?[OrderStatus.Held,OrderStatus.Draft]:[OrderStatus.Completed,OrderStatus.Cancelled]);
        var grid=Rows(Auto,Star);var heading=Columns(Star,new GridLength(260));heading.Margin=new Thickness(0,0,0,16);
        var title=new StackPanel();title.Children.Add(Text(held?"Pesanan ditunda":"Riwayat pesanan",27,true));
        title.Children.Add(Text(held?"Buka kembali pesanan untuk menambah menu atau membayar.":"Lihat rincian dan cetak kembali struk penjualan.",15,false,"#65766E"));Place(heading,title);
        var filter=Identify(new TextBox { ToolTip="Cari nama pelanggan atau nomor pesanan" },"OrderSearch","Cari nama atau nomor pesanan");
        var searchPanel=new StackPanel();searchPanel.Children.Add(Text("Cari nama / nomor pesanan",13,false,"#65766E"));searchPanel.Children.Add(filter);Place(heading,searchPanel,0,1);Place(grid,heading);
        var rows=new StackPanel();var scroll=Scroll(rows,"OrdersViewport");Place(grid,scroll,1);
        void Filter()
        {
            rows.Children.Clear();
            var matching=orders.Where(x=>string.IsNullOrWhiteSpace(filter.Text)||$"{x.Number} {x.CustomerLabel}".Contains(filter.Text.Trim(),StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach(var order in matching)
            {
                var row=Columns(Star,new GridLength(150),new GridLength(180));
                var description=new StackPanel { VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,20,0) };
                description.Children.Add(Text(string.IsNullOrWhiteSpace(order.CustomerLabel)?"Pelanggan tanpa nama":order.CustomerLabel,21,true));
                var date=order.UpdatedAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd MMM · HH.mm",CultureInfo.GetCultureInfo("id-ID"));
                var status=order.Status switch{OrderStatus.Completed=>"Selesai",OrderStatus.Cancelled=>"Dibatalkan",OrderStatus.Held=>"Ditunda",_=>"Draf"};
                var meta=Text($"{date} WIB · {order.Number[^8..]} · {status}",14,false,"#65766E");meta.Margin=new Thickness(0,6,0,6);description.Children.Add(meta);
                description.Children.Add(Text(order.Summary,16));Place(row,description);
                Place(row,Text(order.TotalLabel,22,true,"#205C49"),0,1);
                var actionsPanel=new StackPanel { VerticalAlignment=VerticalAlignment.Center };
                if(held)
                {
                    actionsPanel.Children.Add(ActionButton("Buka pesanan",async()=>
                    {
                        if(current.Id!=order.Id&&current.Status==OrderStatus.Draft&&current.Lines.Length>0)await Save(OrderRules.Hold(current,customerName));
                        var latest=await store.OrderAsync(order.Id)??throw new InvalidOperationException("Pesanan tidak ditemukan.");
                        SetCurrent(await store.SaveAsync(OrderRules.Resume(latest)));cartExpanded=false;RenderSelling();await UpdateStatus();
                    },true,"Resume-"+order.Id));
                    var cancel=ActionButton("Batalkan",async()=>
                    {
                        if(MessageBox.Show(this,"Batalkan pesanan ini? Alasan: pelanggan batal.","Batalkan pesanan",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
                        var latest=await store.OrderAsync(order.Id)??throw new InvalidOperationException("Pesanan tidak ditemukan.");
                        await store.SaveAsync(OrderRules.Cancel(latest,"Pelanggan batal"));if(current.Id==order.Id)SetCurrent(Order.New());await RenderOrders(true);await UpdateStatus();
                    });cancel.FontSize=15;cancel.Margin=new Thickness(0,6,0,0);actionsPanel.Children.Add(cancel);
                }
                else actionsPanel.Children.Add(ActionButton("Lihat rincian",()=>{RenderOrderDetail(order);return Task.CompletedTask;},id:"Detail-"+order.Id));
                Place(row,actionsPanel,0,2);var surface=Surface(row);surface.Margin=new Thickness(0,0,0,10);rows.Children.Add(surface);
            }
            if(matching.Length==0)rows.Children.Add(Empty("Belum ada pesanan",orders.Length==0?"Pesanan akan muncul di sini setelah disimpan.":"Coba nama atau nomor lainnya."));
            if(orders.Length==1000)rows.Children.Add(Text("Menampilkan 1.000 pesanan terbaru. Pencarian berlaku untuk daftar ini.",14,false,"#65766E"));
        }
        filter.TextChanged+=(_,_)=>Filter();Filter();Present(held?"held":"history",grid);
    }
    private void RenderOrderDetail(Order order)
    {
        var panel=Rows(Auto,Star,Auto);
        var header=Columns(Star,Auto);var title=new StackPanel();title.Children.Add(Text("Rincian pesanan",26,true));title.Children.Add(Text($"{order.Number} · {order.CustomerLabel}",16,false,"#65766E"));Place(header,title);
        Place(header,ActionButton("← Riwayat",()=>RenderOrders(false)),0,1);header.Margin=new Thickness(0,0,0,14);Place(panel,header);
        var lines=new StackPanel();foreach(var line in order.Lines)
        {
            var row=Columns(Star,Auto);row.Margin=new Thickness(0,12,0,12);var name=new StackPanel();name.Children.Add(Text(line.Name,20,true));name.Children.Add(Text($"{line.Quantity} × {line.PriceLabel}",16,false,"#65766E"));Place(row,name);Place(row,Text(line.SubtotalLabel,22,true),0,1);lines.Children.Add(row);
        }
        Place(panel,Scroll(lines),1);var footer=Columns(Star,Auto);Place(footer,Text($"Total  {order.TotalLabel}",30,true));
        if(order.Status==OrderStatus.Completed)Place(footer,ActionButton("Cetak ulang struk",async()=>{var sale=await store.SaleAsync(order.Id);if(sale is not null)await Print(sale,true);},true),0,1);
        else Place(footer,Text($"Dibatalkan · {order.CancellationReason}",18,false,"#87522F"),0,1);
        Place(panel,footer,2);Present("history",Surface(panel));
    }
    private async Task RenderCash()
    {
        if(!ready)return;
        var now=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));var summary=await store.DailySalesAsync(now.Date);
        var panel=new StackPanel();panel.Children.Add(Text("Kas hari ini",28,true));
        var date=Text(now.ToString("dddd, d MMMM yyyy",CultureInfo.GetCultureInfo("id-ID"))+" · WIB",16,false,"#65766E");date.Margin=new Thickness(0,6,0,22);panel.Children.Add(date);
        var cards=new System.Windows.Controls.Primitives.UniformGrid { Columns=3 };
        foreach(var (label,value,note) in new[]{("Penjualan tercatat",Money.Format(checked(summary.Cash+summary.Qris)),$"{summary.Count} pesanan selesai"),("Tunai",Money.Format(summary.Cash),"Diterima melalui uang tunai"),("QRIS",Money.Format(summary.Qris),"Dicatat oleh kasir")})
        {
            var content=new StackPanel();content.Children.Add(Text(label,18,false,"#65766E"));var amount=Text(value,30,true,"#205C49");amount.Margin=new Thickness(0,14,0,14);content.Children.Add(amount);content.Children.Add(Text(note,15,false,"#65766E"));
            var card=Surface(content,22);card.Margin=new Thickness(0,0,12,0);cards.Children.Add(card);
        }
        panel.Children.Add(cards);
        var explanation=new StackPanel { Margin=new Thickness(0,24,0,0) };explanation.Children.Add(Text("Tentang ringkasan ini",20,true));
        var detail=Text("Angka di atas adalah penjualan yang dicatat hari ini, bukan saldo laci atau saldo rekening. Modal, pengeluaran, refund, dan biaya QRIS belum dihitung.",17,false,"#65766E");detail.Margin=new Thickness(0,8,0,0);explanation.Children.Add(detail);panel.Children.Add(explanation);Present("cash",Scroll(panel));
    }
}
