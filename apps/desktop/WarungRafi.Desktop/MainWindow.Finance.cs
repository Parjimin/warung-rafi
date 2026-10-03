using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WarungRafi.Core;

namespace WarungRafi.Desktop;

public partial class MainWindow
{
    private static string CashTime(DateTimeOffset value)=>value.ToOffset(TimeSpan.FromHours(7)).ToString("d MMM · HH.mm",CultureInfo.GetCultureInfo("id-ID"))+" WIB";
    private static TextBox FinanceInput(StackPanel panel,string label,string id,string value="",bool amount=false)
    {
        var heading=Text(label,16,false,"#65766E");heading.Margin=new Thickness(0,12,0,6);panel.Children.Add(heading);
        var input=Identify(new TextBox { Text=value,MaxLength=amount?10:200,FontSize=amount?28:19,MinHeight=48 },id,label);panel.Children.Add(input);return input;
    }
    private Grid FinancePage(string title,string subtitle,Func<Task> back,string backLabel="← Riwayat")
    {
        var pageRoot=Rows(Auto,Star);var header=Columns(Star,Auto);header.Margin=new Thickness(0,0,0,16);
        var text=new StackPanel();text.Children.Add(Text(title,28,true));var note=Text(subtitle,16,false,"#65766E");note.Margin=new Thickness(0,6,16,0);text.Children.Add(note);Place(header,text);
        Place(header,ActionButton(backLabel,back),0,1);Place(pageRoot,header);return pageRoot;
    }
    private static void CashLine(StackPanel panel,string name,long amount,bool emphasis=false)
    {
        var row=Columns(Star,new GridLength(16),Auto);row.Margin=new Thickness(0,8,0,8);Place(row,Text(name,emphasis?20:17,emphasis,"#65766E"));Place(row,Text(Money.Format(amount),emphasis?26:20,true),0,2);panel.Children.Add(row);
    }
    private async Task RenderRefunds(Order order)
    {
        var refunds=await store.RefundsAsync(order.Id);var sale=await store.SaleAsync(order.Id)??throw new InvalidOperationException("Pesanan belum dibayar.");
        var reserved=refunds.Where(r=>r.State!=RefundState.Failed).Sum(r=>r.Amount);var remaining=order.Total-reserved;
        var root=FinancePage("Pengembalian pelanggan",order.Number+" · "+order.TotalLabel,()=>{RenderOrderDetail(order);return Task.CompletedTask;},"← Rincian pesanan");
        var columns=Columns(Star,new GridLength(18),Star);var form=new StackPanel();form.Children.Add(Text("Ajukan pengembalian",23,true));CashLine(form,"Masih dapat diajukan",remaining);
        var amount=FinanceInput(form,"Nominal (rupiah)","RefundAmount","",true);var reason=FinanceInput(form,"Alasan pengembalian","RefundReason");
        form.Children.Add(Text("Dikembalikan melalui",17));var channel=Identify(new ComboBox { FontSize=20,MinHeight=48,Margin=new Thickness(0,8,0,12),ItemsSource=new[]{"Uang tunai","Transfer / provider"},SelectedIndex=sale.Payment.Method==PaymentMethod.Cash?0:1 },"RefundChannel");form.Children.Add(channel);
        form.Children.Add(Text("Permintaan belum mengurangi penjualan. Pengelola perlu mencatat hasilnya setelah uang benar-benar dikembalikan.",16,false,"#65766E"));
        var id=Guid.NewGuid().ToString("N");var request=ActionButton("Simpan permintaan",async()=>{await store.RequestRefundAsync(id,order.Id,Money.ParseInput(amount.Text),(RefundChannel)channel.SelectedIndex,reason.Text);await UpdateStatus();await RenderRefunds(order);},true,"RequestRefund");request.IsEnabled=remaining>0;request.Margin=new Thickness(0,18,0,0);form.Children.Add(request);Place(columns,Surface(Scroll(form)));
        var list=new StackPanel();list.Children.Add(Text("Riwayat pengembalian",23,true));foreach(var refund in refunds.Reverse())
        {
            var item=new StackPanel { Margin=new Thickness(0,18,0,8) };item.Children.Add(Text(Money.Format(refund.Amount)+" · "+(refund.Channel==RefundChannel.Cash?"Tunai":"Transfer / provider"),21,true));
            item.Children.Add(Text(refund.Reason,17));item.Children.Add(Text(refund.State switch {RefundState.Requested=>"Menunggu pengelola",RefundState.Completed=>"Berhasil · "+refund.Reference,_=>"Tidak terlaksana · "+refund.FailureReason},16,false,"#65766E"));
            if(refund.State==RefundState.Requested){var resolve=ActionButton("Catat hasil",()=>{RenderResolveRefund(order,refund);return Task.CompletedTask;},id:"Resolve-"+refund.Id);resolve.Margin=new Thickness(0,10,0,0);item.Children.Add(resolve);}list.Children.Add(item);
        }
        if(refunds.Length==0)list.Children.Add(Text("Belum ada permintaan untuk pesanan ini.",17,false,"#65766E"));Place(columns,Surface(Scroll(list)),0,2);Place(root,columns,1);Present("history",root);
    }
    private void RenderResolveRefund(Order order,Refund refund)
    {
        var root=FinancePage("Catat hasil pengembalian",Money.Format(refund.Amount)+" · "+(refund.Channel==RefundChannel.Cash?"Uang tunai":"Transfer / provider"),()=>RenderRefunds(order),"← Pengembalian");
        var form=new StackPanel { MaxWidth=680,HorizontalAlignment=HorizontalAlignment.Center };form.Children.Add(Text("Khusus pengelola",24,true));form.Children.Add(Text("Kembalikan uang terlebih dahulu, lalu catat hasilnya. Refund berhasil otomatis mengurangi rekap spreadsheet; jumlah pcs menu tetap.",18,false,"#65766E"));
        var reference=FinanceInput(form,refund.Channel==RefundChannel.Cash?"Keterangan penyerahan / alasan tidak terlaksana":"Referensi transfer / provider atau alasan tidak terlaksana","RefundReference");
        var label=Text("PIN pengelola",17);label.Margin=new Thickness(0,14,0,6);form.Children.Add(label);var pin=Identify(new PasswordBox { MaxLength=12,FontSize=24,MinHeight=48,Padding=new Thickness(12) },"ManagerPin","PIN pengelola");form.Children.Add(pin);
        var confirmed=Identify(new CheckBox { Content="Uang sudah dikembalikan kepada pelanggan",FontSize=18,MinHeight=48,Margin=new Thickness(0,12,0,8) },"RefundReturned");form.Children.Add(confirmed);
        async Task Resolve(bool completed){var secret=pin.Password;pin.Clear();await store.ResolveRefundAsync(refund.Id,completed,secret,reference.Text);await UpdateStatus();await RenderRefunds(order);}
        var success=ActionButton("Simpan sebagai berhasil",()=>Resolve(true),true,"CompleteRefund");success.IsEnabled=false;confirmed.Checked+=(_,_)=>success.IsEnabled=true;confirmed.Unchecked+=(_,_)=>success.IsEnabled=false;form.Children.Add(success);
        var failed=ActionButton("Simpan: tidak terlaksana",()=>Resolve(false),id:"FailRefund");failed.Margin=new Thickness(0,10,0,0);form.Children.Add(failed);
        Place(root,Surface(Scroll(form)),1);Present("history",root);
    }
}
