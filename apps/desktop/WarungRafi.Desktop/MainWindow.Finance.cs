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
    private Grid FinancePage(string title,string subtitle,Func<Task> back,string backLabel="← Kas hari ini")
    {
        var pageRoot=Rows(Auto,Star);var header=Columns(Star,Auto);header.Margin=new Thickness(0,0,0,16);
        var text=new StackPanel();text.Children.Add(Text(title,28,true));var note=Text(subtitle,16,false,"#65766E");note.Margin=new Thickness(0,6,16,0);text.Children.Add(note);Place(header,text);
        Place(header,ActionButton(backLabel,back),0,1);Place(pageRoot,header);return pageRoot;
    }
    private static void CashLine(StackPanel panel,string name,long amount,bool emphasis=false)
    {
        var row=Columns(Star,new GridLength(16),Auto);row.Margin=new Thickness(0,8,0,8);Place(row,Text(name,emphasis?20:17,emphasis,"#65766E"));Place(row,Text(Money.Format(amount),emphasis?26:20,true),0,2);panel.Children.Add(row);
    }
    private async Task RenderCash()
    {
        if(!ready)return;
        var position=await store.ActiveCashAsync();
        var root=FinancePage("Kas hari ini",position is null?"Buka kas sebelum menerima pembayaran.":"Sesi dibuka "+CashTime(position.Session.OpenedAt),RenderCashHistory,"Riwayat kas");
        if(position is null)
        {
            var empty=new StackPanel { MaxWidth=540,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
            empty.Children.Add(Text("Kas belum dibuka",30,true));var note=Text("Hitung uang modal di laci. Masukkan 0 jika belum ada modal; pesanan yang ditunda tetap tersimpan.",19,false,"#65766E");note.Margin=new Thickness(0,14,0,24);empty.Children.Add(note);
            empty.Children.Add(ActionButton("Buka kas",()=>{RenderOpenCash(false);return Task.CompletedTask;},true,"OpenCash"));Place(root,Surface(empty),1);
        }
        else
        {
            var columns=Columns(new GridLength(1.15,GridUnitType.Star),new GridLength(18),Star);
            var left=Rows(Star,Auto);var balance=new StackPanel();balance.Children.Add(Text("Uang di laci menurut catatan",20,false,"#65766E"));
            var expected=Identify(Text(Money.Format(position.Expected),42,true,"#205C49"),"ExpectedCash");expected.Margin=new Thickness(0,10,0,20);balance.Children.Add(expected);
            CashLine(balance,"Modal awal",position.Session.OpeningCash);CashLine(balance,"Penjualan tunai",position.CashSales);CashLine(balance,"Kas masuk lainnya",position.CashIn);CashLine(balance,"Kas keluar",-position.CashOut);CashLine(balance,"Pengembalian tunai",-position.CashRefunds);
            var actions=Columns(Star,new GridLength(10),Star);actions.Margin=new Thickness(0,16,0,0);
            Place(actions,ActionButton("+ Kas masuk",()=>{RenderCashMovement(position,true);return Task.CompletedTask;},id:"CashIn"));
            Place(actions,ActionButton("− Kas keluar",()=>{RenderCashMovement(position,false);return Task.CompletedTask;},id:"CashOut"),0,2);
            Place(left,Scroll(balance,"CashBalanceViewport"));Place(left,actions,1);Place(columns,Surface(left));
            var right=Rows(Star,Auto);var sales=new StackPanel();sales.Children.Add(Text("Penjualan tercatat",22,true));
            CashLine(sales,$"{position.SaleCount} pesanan dalam sesi ini",position.Gross,true);CashLine(sales,"Di antaranya QRIS",position.QrisSales);
            var detail=Text("QRIS dicatat kasir dan tidak menambah uang di laci. Angka ini belum menunjukkan pencairan atau biaya provider.",17,false,"#65766E");detail.Margin=new Thickness(0,12,0,16);sales.Children.Add(detail);
            CashLine(sales,"Pengembalian berhasil di sesi ini",position.SalesRefunds);
            sales.Children.Add(Text("Pengembalian dapat berasal dari penjualan pada sesi sebelumnya.",15,false,"#65766E"));
            Place(right,Scroll(sales));var close=ActionButton("Hitung & tutup kas",()=>{RenderCloseCash(position);return Task.CompletedTask;},true,"CloseCash");close.Margin=new Thickness(0,18,0,0);Place(right,close,1);
            Place(columns,Surface(right),0,2);Place(root,columns,1);
        }
        Present("cash",root);
    }
    private void RenderOpenCash(bool returnToPayment)
    {
        var root=FinancePage("Buka kas","Cukup sekali di awal berjualan. Sesi tetap terbuka jika aplikasi ditutup.",()=>{if(returnToPayment){RenderSelling();return Task.CompletedTask;}return RenderCash();},returnToPayment?"← Pesanan":"← Kas hari ini");
        var form=new StackPanel { MaxWidth=620,HorizontalAlignment=HorizontalAlignment.Center };
        form.Children.Add(Text("Berapa uang modal di laci?",24,true));var input=FinanceInput(form,"Modal awal (rupiah)","OpeningCash","",true);
        var zero=ActionButton("Mulai tanpa modal · Rp0",()=>{input.Text="0";return Task.CompletedTask;},id:"ZeroOpeningCash");zero.Margin=new Thickness(0,12,0,16);form.Children.Add(zero);
        var id=Guid.NewGuid().ToString("N");form.Children.Add(ActionButton(returnToPayment?"Buka kas & lanjut bayar":"Buka kas",async()=>
        {
            if(string.IsNullOrWhiteSpace(input.Text))throw new ArgumentException("Isi modal awal, atau pilih mulai tanpa modal.");
            await store.OpenCashAsync(id,Money.ParseInput(input.Text));await UpdateStatus();if(returnToPayment)RenderPayment();else await RenderCash();
        },true,"ConfirmOpenCash"));Place(root,Surface(Scroll(form)),1);Present("cash",root);
    }
    private void RenderCashMovement(CashPosition position,bool incoming)
    {
        var root=FinancePage(incoming?"Catat kas masuk":"Catat kas keluar","Untuk uang di luar penjualan dan pengembalian pelanggan.",RenderCash);
        var form=new StackPanel { MaxWidth=640,HorizontalAlignment=HorizontalAlignment.Center };CashLine(form,"Kas menurut catatan",position.Expected,true);
        var amount=FinanceInput(form,"Nominal (rupiah)","MovementAmount","",true);var reason=FinanceInput(form,incoming?"Keterangan · contoh: tambahan modal":"Keterangan · contoh: membeli es batu","MovementReason");
        var id=Guid.NewGuid().ToString("N");var save=ActionButton(incoming?"Simpan kas masuk":"Simpan kas keluar",async()=>{await store.RecordCashMovementAsync(id,position.Session.Id,incoming,Money.ParseInput(amount.Text),reason.Text);await UpdateStatus();await RenderCash();},true,"SaveCashMovement");save.Margin=new Thickness(0,22,0,0);form.Children.Add(save);
        Place(root,Surface(Scroll(form)),1);Present("cash",root);
    }
    private void RenderCloseCash(CashPosition position)
    {
        var root=FinancePage("Hitung uang di laci","Pesanan ditunda tetap tersimpan setelah kas ditutup.",RenderCash);
        var form=new StackPanel { MaxWidth=680,HorizontalAlignment=HorizontalAlignment.Center };CashLine(form,"Kas menurut catatan",position.Expected,true);
        var counted=FinanceInput(form,"Uang yang benar-benar dihitung (rupiah)","CountedCash","",true);
        var difference=Identify(Text("Masukkan hasil hitungan uang di laci.",20,true),"CashDifference");difference.Margin=new Thickness(0,12,0,6);form.Children.Add(difference);
        counted.TextChanged+=(_,_)=>{try{difference.Text=string.IsNullOrWhiteSpace(counted.Text)?"Masukkan hasil hitungan uang di laci.":"Selisih  "+Money.Format(Money.ParseInput(counted.Text)-position.Expected);}catch(ArgumentException){difference.Text="Periksa nominal hitungan.";}};
        var note=FinanceInput(form,"Catatan · wajib jika ada selisih","ClosingNote");
        var formSurface=Surface(Scroll(form));
        var review=ActionButton("Periksa & lanjutkan",()=>
        {
            if(string.IsNullOrWhiteSpace(counted.Text))throw new ArgumentException("Isi hasil hitungan kas terlebih dahulu.");
            var amount=Money.ParseInput(counted.Text);FinanceRules.Amount(amount,true);if(amount!=position.Expected)FinanceRules.Note(note.Text);
            var confirmation=new StackPanel { MaxWidth=640,HorizontalAlignment=HorizontalAlignment.Center };
            confirmation.Children.Add(Text("Tutup sesi kas ini?",27,true));CashLine(confirmation,"Menurut catatan",position.Expected);CashLine(confirmation,"Uang yang dihitung",amount);CashLine(confirmation,"Selisih",amount-position.Expected,true);
            confirmation.Children.Add(Text(note.Text,18));var explanation=Text("Hitungan akhir disimpan permanen. Periksa kembali sebelum menutup kas.",17,false,"#65766E");explanation.Margin=new Thickness(0,14,0,20);confirmation.Children.Add(explanation);
            confirmation.Children.Add(ActionButton("Ya, tutup kas",async()=>{await store.CloseCashAsync(position.Session.Id,amount,note.Text);await UpdateStatus();await RenderCashHistory();},true,"ConfirmCloseCash"));
            var revise=ActionButton("Kembali ke hitungan",()=>{root.Children.RemoveAt(root.Children.Count-1);Place(root,formSurface,1);return Task.CompletedTask;},id:"ReviseCloseCash");revise.Margin=new Thickness(0,10,0,0);confirmation.Children.Add(revise);
            // Replace the content row, keeping navigation and available height intact.
            root.Children.RemoveAt(root.Children.Count-1);Place(root,Surface(Scroll(confirmation)),1);return Task.CompletedTask;
        },true,"ReviewCloseCash");review.Margin=new Thickness(0,22,0,0);form.Children.Add(review);
        Place(root,formSurface,1);Present("cash",root);
    }
    private async Task RenderCashHistory()
    {
        var history=await store.CashHistoryAsync();var root=FinancePage("Riwayat kas","100 sesi terbaru · Sesi dapat melewati tengah malam.",RenderCash);
        var list=new StackPanel();foreach(var position in history)
        {
            var session=position.Session;var row=Columns(Star,Auto);var info=new StackPanel();info.Children.Add(Text(CashTime(session.OpenedAt),21,true));
            info.Children.Add(Text(session.ClosedAt is null?"Masih terbuka":"Ditutup "+CashTime(session.ClosedAt.Value),15,false,"#65766E"));
            info.Children.Add(Text(session.ClosedAt is null?"Kas tercatat "+Money.Format(position.Expected):$"Dihitung {Money.Format(session.CountedCash!.Value)} · Selisih {Money.Format(session.Difference!.Value)}",18));
            if(session.ClosingNote.Length>0)info.Children.Add(Text(session.ClosingNote,16,false,"#65766E"));Place(row,info);
            Place(row,ActionButton("Rincian",()=>RenderCashEntries(position),id:"CashDetail-"+session.Id),0,1);var card=Surface(row);card.Margin=new Thickness(0,0,0,10);list.Children.Add(card);
        }
        if(history.Length==0)list.Children.Add(Empty("Belum ada sesi kas","Mulai dengan membuka kas dan mengisi modal awal."));
        Place(root,Scroll(list),1);Present("cash",root);
    }
    private async Task RenderCashEntries(CashPosition position)
    {
        var root=FinancePage("Rincian sesi kas","Dibuka "+CashTime(position.Session.OpenedAt)+" · 500 catatan terbaru",RenderCashHistory,"← Riwayat kas");var list=new StackPanel();
        foreach(var entry in await store.CashEntriesAsync(position.Session.Id))
        {
            var row=Columns(Star,Auto);var info=new StackPanel();var kind=entry.Kind switch {"session_opened"=>"Modal awal","session_closed"=>"Tutup kas","sale"=>entry.CashDelta>0?"Penjualan tunai":"Penjualan QRIS","cash_in"=>"Kas masuk","cash_out"=>"Kas keluar","refund_completed"=>"Pengembalian berhasil",_=>entry.Kind};
            info.Children.Add(Text(kind,21,true));info.Children.Add(Text(CashTime(entry.OccurredAt)+" · "+entry.Reason,16,false,"#65766E"));Place(row,info);
            var amount=new StackPanel();amount.Children.Add(Text(Money.Format(entry.Amount),23,true));amount.Children.Add(Text("Perubahan laci "+Money.Format(entry.CashDelta),14,false,"#65766E"));Place(row,amount,0,1);var card=Surface(row);card.Margin=new Thickness(0,0,0,8);list.Children.Add(card);
        }
        Place(root,Scroll(list),1);Present("cash",root);
    }
    private async Task RenderRefunds(Order order)
    {
        var refunds=await store.RefundsAsync(order.Id);var sale=await store.SaleAsync(order.Id)??throw new InvalidOperationException("Pesanan belum dibayar.");
        var reserved=refunds.Where(r=>r.State!=RefundState.Failed).Sum(r=>r.Amount);var remaining=order.Total-reserved;
        var root=FinancePage("Pengembalian pelanggan",order.Number+" · "+order.TotalLabel,()=>{RenderOrderDetail(order);return Task.CompletedTask;},"← Rincian pesanan");
        var columns=Columns(Star,new GridLength(18),Star);var form=new StackPanel();form.Children.Add(Text("Ajukan pengembalian",23,true));CashLine(form,"Masih dapat diajukan",remaining);
        var amount=FinanceInput(form,"Nominal (rupiah)","RefundAmount","",true);var reason=FinanceInput(form,"Alasan pengembalian","RefundReason");
        form.Children.Add(Text("Dikembalikan melalui",17));var channel=Identify(new ComboBox { FontSize=20,MinHeight=48,Margin=new Thickness(0,8,0,12),ItemsSource=new[]{"Uang tunai dari laci","Transfer / provider"},SelectedIndex=sale.Payment.Method==PaymentMethod.Cash?0:1 },"RefundChannel");form.Children.Add(channel);
        form.Children.Add(Text("Permintaan belum mengurangi kas. Pengelola perlu mencatat hasilnya setelah uang benar-benar dikembalikan.",16,false,"#65766E"));
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
        var root=FinancePage("Catat hasil pengembalian",Money.Format(refund.Amount)+" · "+(refund.Channel==RefundChannel.Cash?"Uang tunai dari laci":"Transfer / provider"),()=>RenderRefunds(order),"← Pengembalian");
        var form=new StackPanel { MaxWidth=680,HorizontalAlignment=HorizontalAlignment.Center };form.Children.Add(Text("Khusus pengelola",24,true));form.Children.Add(Text("Aplikasi mencatat hasil pengembalian. Transfer atau refund melalui provider perlu dilakukan terlebih dahulu.",18,false,"#65766E"));
        var reference=FinanceInput(form,refund.Channel==RefundChannel.Cash?"Keterangan penyerahan / alasan tidak terlaksana":"Referensi transfer / provider atau alasan tidak terlaksana","RefundReference");
        var label=Text("PIN pengelola",17);label.Margin=new Thickness(0,14,0,6);form.Children.Add(label);var pin=Identify(new PasswordBox { MaxLength=12,FontSize=24,MinHeight=48,Padding=new Thickness(12) },"ManagerPin","PIN pengelola");form.Children.Add(pin);
        var confirmed=Identify(new CheckBox { Content="Uang sudah dikembalikan kepada pelanggan",FontSize=18,MinHeight=48,Margin=new Thickness(0,12,0,8) },"RefundReturned");form.Children.Add(confirmed);
        async Task Resolve(bool completed){var secret=pin.Password;pin.Clear();await store.ResolveRefundAsync(refund.Id,completed,secret,reference.Text);await UpdateStatus();await RenderRefunds(order);}
        var success=ActionButton("Simpan sebagai berhasil",()=>Resolve(true),true,"CompleteRefund");success.IsEnabled=false;confirmed.Checked+=(_,_)=>success.IsEnabled=true;confirmed.Unchecked+=(_,_)=>success.IsEnabled=false;form.Children.Add(success);
        var failed=ActionButton("Simpan: tidak terlaksana",()=>Resolve(false),id:"FailRefund");failed.Margin=new Thickness(0,10,0,0);form.Children.Add(failed);
        Place(root,Surface(Scroll(form)),1);Present("history",root);
    }
}
