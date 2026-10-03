using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WarungRafi.Core;

namespace WarungRafi.Desktop;

public partial class MainWindow
{
    private void RenderPayment()
    {
        tenderedInput=null;changeLabel=null;completeButton=null;
        var grid=Columns(Star,new GridLength(18),new GridLength(1.4,GridUnitType.Star));
        grid.ColumnDefinitions[0].MinWidth=310;grid.ColumnDefinitions[2].MinWidth=470;
        Place(grid,Cart(false));
        var panel=Rows(Auto,Star,Auto);
        var header=new StackPanel();var heading=Columns(Star,Auto);
        Place(heading,Text("Pembayaran",26,true));
        var back=ActionButton("← Ubah pesanan",()=>{RenderSelling();return Task.CompletedTask;},id:"EditOrder");back.FontSize=15;back.Background=System.Windows.Media.Brushes.White;back.BorderBrush=Color("#DBE3D7");Place(heading,back,0,1);header.Children.Add(heading);
        var methods=Columns(Star,new GridLength(10),Star);methods.Margin=new Thickness(0,12,0,14);
        var cashTab=ActionButton("Tunai",()=>{method=PaymentMethod.Cash;RenderPayment();return Task.CompletedTask;},id:"MethodCash");SelectTab(cashTab,method==PaymentMethod.Cash);Place(methods,cashTab);
        var qrisTab=ActionButton("QRIS",()=>{method=PaymentMethod.QrisManual;RenderPayment();return Task.CompletedTask;},id:"MethodQris");SelectTab(qrisTab,method==PaymentMethod.QrisManual);Place(methods,qrisTab,0,2);header.Children.Add(methods);Place(panel,header);
        if(method==PaymentMethod.Cash)
        {
            var body=Columns(Star,new GridLength(14),Star);
            var entry=new StackPanel();entry.Children.Add(Text("Uang diterima",17,false,"#65766E"));
            tenderedInput=Identify(new TextBox { Text=tendered,FontSize=30,FontWeight=FontWeights.SemiBold,MaxLength=10,BorderThickness=new Thickness(0),Background=System.Windows.Media.Brushes.Transparent,InputScope=new InputScope { Names={new InputScopeName(InputScopeNameValue.Number)} } },"Tendered","Uang tunai diterima");
            tenderedInput.TextChanged+=(_,_)=>{tendered=tenderedInput.Text;UpdateChange();};
            var amountEntry=Columns(Auto,Star);var prefix=Text("Rp",20,false,"#748170");prefix.Margin=new Thickness(14,0,0,0);Place(amountEntry,prefix);Place(amountEntry,InputWithHint(tenderedInput,"0"),0,1);
            entry.Children.Add(new Border { Child=amountEntry,CornerRadius=new CornerRadius(12),Background=Color("#F7F9F4"),BorderBrush=Color("#DDE5D7"),BorderThickness=new Thickness(1),Margin=new Thickness(0,8,0,12) });
            var quick=new System.Windows.Controls.Primitives.UniformGrid { Columns=2 };
            foreach(var value in new[]{current.Total,20000L,50000L,100000L}.Where(x=>x>=current.Total).Distinct())
            {
                var chosen=value;var button=ActionButton(value==current.Total?"Uang pas":Money.Format(value),()=>{tenderedInput.Text=chosen.ToString(CultureInfo.InvariantCulture);return Task.CompletedTask;},id:"Cash-"+value);
                button.FontSize=15;button.Background=System.Windows.Media.Brushes.White;button.BorderBrush=Color("#DBE3D7");button.Padding=new Thickness(5,8,5,8);button.Margin=new Thickness(0,0,6,6);quick.Children.Add(button);
            }
            entry.Children.Add(quick);Place(body,entry);
            var keys=new System.Windows.Controls.Primitives.UniformGrid { Columns=3,Rows=4,VerticalAlignment=VerticalAlignment.Top };
            foreach(var key in new[]{"1","2","3","4","5","6","7","8","9","C","0","⌫"})
            {
                var chosen=key;var button=ActionButton(key,()=>
                {
                    tenderedInput.Text=chosen=="C"?"":chosen=="⌫"?(tendered.Length>0?tendered[..^1]:""):tendered.Length<10?tendered+chosen:tendered;
                    return Task.CompletedTask;
                },id:"Key-"+key);
                button.MinHeight=48;button.Height=48;button.Padding=new Thickness(6);button.FontSize=22;button.Background=Color("#F7F9F4");button.BorderBrush=Color("#E2E7DE");button.Margin=new Thickness(0,0,6,6);keys.Children.Add(button);
            }
            Place(body,keys,0,2);Place(panel,Scroll(body,"PaymentBody"),1);
        }
        else
        {
            var instructions=new StackPanel { VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,12,8,12) };
            instructions.Children.Add(Text("Scan QRIS di meja",24,true));
            var note=Text("Minta pelanggan memindai QR yang dipajang, lalu mengisi nominal berikut.",18,false,"#65766E");note.Margin=new Thickness(0,12,0,12);instructions.Children.Add(note);
            instructions.Children.Add(Text(current.TotalLabel,38,true,"#205C49"));
            var reminder=Text("Pastikan pembayaran diterima sebelum menekan Selesaikan pesanan.",17);reminder.Margin=new Thickness(0,18,0,0);instructions.Children.Add(reminder);
            Place(panel,Scroll(instructions,"PaymentBody"),1);
        }
        var footer=new StackPanel { Margin=new Thickness(0,12,0,0) };
        changeLabel=Identify(Text("",22,true),"ChangeLabel");
        footer.Children.Add(new Border { Child=changeLabel,Background=Color("#EDF3ED"),CornerRadius=new CornerRadius(10),Padding=new Thickness(12),Margin=new Thickness(0,0,0,10) });
        completeButton=ActionButton("Selesaikan pesanan",Complete,true,"CompleteOrder");completeButton.MinHeight=52;footer.Children.Add(completeButton);
        Place(panel,footer,2);Place(grid,Surface(panel),0,2);UpdateChange();Present("payment",grid);
    }
    private void UpdateChange()
    {
        if(changeLabel is null||completeButton is null)return;
        if(method==PaymentMethod.QrisManual){changeLabel.Text="Pembayaran melalui QRIS";changeLabel.Foreground=Color("#205C49");completeButton.IsEnabled=true;return;}
        try
        {
            var difference=Money.ParseInput(tendered)-current.Total;
            changeLabel.Text=string.IsNullOrWhiteSpace(tendered)?"Masukkan uang yang diterima":difference<0?$"Masih kurang {Money.Format(-difference)}":$"Kembalian {Money.Format(difference)}";
            changeLabel.Foreground=Color(difference<0?"#87522F":"#205C49");completeButton.IsEnabled=difference>=0;
        }
        catch(ArgumentException ex){changeLabel.Text=ex.Message;changeLabel.Foreground=Color("#A33931");completeButton.IsEnabled=false;}
    }
    private async Task Complete()
    {
        lastSale=await store.CompleteAsync(current.Id,current.Version,method,method==PaymentMethod.Cash?Money.ParseInput(tendered):0);
        SetCurrent(lastSale.Order);
        var panel=new StackPanel { MaxWidth=600,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(20) };
        var tick=Text("✓",36,true,"#205C49");tick.HorizontalAlignment=HorizontalAlignment.Center;
        panel.Children.Add(new Border { Child=tick,Width=70,Height=70,Background=Color("#EDF3ED"),CornerRadius=new CornerRadius(35),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,18) });
        var title=Text("Pesanan tersimpan",30,true);title.TextAlignment=TextAlignment.Center;panel.Children.Add(title);
        var number=Text(current.Number,15,false,"#65766E");number.TextAlignment=TextAlignment.Center;number.Margin=new Thickness(0,8,0,22);panel.Children.Add(number);
        var label=Text(method==PaymentMethod.Cash?"Kembalian pelanggan":"QRIS dicatat kasir",18,false,"#65766E");label.TextAlignment=TextAlignment.Center;panel.Children.Add(label);
        var amount=Identify(Text(method==PaymentMethod.Cash?Money.Format(lastSale.Payment.Change):current.TotalLabel,44,true,"#205C49"),"SuccessAmount");amount.TextAlignment=TextAlignment.Center;amount.Margin=new Thickness(0,6,0,24);panel.Children.Add(amount);
        panel.Children.Add(ActionButton("+  Pesanan baru",()=>{SetCurrent(Order.New());cartExpanded=false;RenderSelling();return Task.CompletedTask;},true,"NewOrder"));
        var print=ActionButton("Cetak ulang struk",()=>Print(lastSale,true),id:"Reprint");print.Margin=new Thickness(0,10,0,0);panel.Children.Add(print);
        var root=new Grid();root.Children.Add(panel);Present("success",Surface(root));await UpdateStatus();_=Print(lastSale,false);
    }
}
