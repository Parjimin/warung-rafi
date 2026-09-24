using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WarungRafi.Core;

namespace WarungRafi.Desktop;

public partial class MainWindow
{
    private ContentControl? cartHost;
    private ScrollViewer? cartScroll,productScroll;
    private WrapPanel? productCards;
    private TextBlock? productCount;
    private readonly Dictionary<string,TextBlock> productBadges=new();

    private void RenderSelling()
    {
        if(!ready)return;
        var grid=Columns(new GridLength(1.6,GridUnitType.Star),new GridLength(18),new GridLength(1,GridUnitType.Star));
        grid.ColumnDefinitions[2].MinWidth=370;
        var left=Rows(Auto,Auto,Star);
        var heading=Columns(Star,new GridLength(220));heading.Margin=new Thickness(0,0,0,14);
        var title=new StackPanel();title.Children.Add(Text("Mau pesan apa?",26,true));
        productCount=Text("",14,false,"#65766E");productCount.Margin=new Thickness(0,4,0,0);title.Children.Add(productCount);Place(heading,title);
        var find=Identify(new TextBox { Text=search,ToolTip="Cari nama menu di semua kategori",MaxLength=60,VerticalAlignment=VerticalAlignment.Center },"MenuSearch","Cari menu");
        var searchBox=Rows(Auto,Auto);searchBox.Children.Add(Text("Cari menu",12,false,"#65766E"));Place(searchBox,find,1);Place(heading,searchBox,0,1);Place(left,heading);
        find.TextChanged+=(_,_)=> { search=find.Text;RefreshProducts(); };
        var tabs=new System.Windows.Controls.Primitives.UniformGrid { Columns=4,Margin=new Thickness(0,0,0,12) };
        foreach(var name in new[]{"Nasi","Lauk","Sundukan","Minuman"})
        {
            var chosen=name;var tab=ActionButton(name,()=>{category=chosen;search="";RenderSelling();return Task.CompletedTask;},category==name&&search.Length==0,"Category-"+name);
            tab.FontSize=17;tab.Padding=new Thickness(8,9,8,9);tab.Margin=new Thickness(0,0,6,0);tabs.Children.Add(tab);
        }
        Place(left,tabs,1);
        productCards=new WrapPanel();productScroll=Scroll(productCards,"MenuViewport");Place(left,productScroll,2);
        productScroll.SizeChanged+=(_,_)=>SizeCards();
        Place(grid,left);
        cartHost=new ContentControl { HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch };
        Place(grid,cartHost,0,2);
        if(cartExpanded){left.Visibility=Visibility.Collapsed;grid.ColumnDefinitions[0].Width=new GridLength(0);grid.ColumnDefinitions[1].Width=new GridLength(0);}
        RefreshProducts();RefreshCart();Present("sell",grid);
    }
    private void SizeCards()
    {
        if(productCards is null||productScroll is null)return;
        var available=Math.Max(180,productScroll.ViewportWidth>0?productScroll.ViewportWidth:productScroll.ActualWidth-18);
        var columns=Math.Max(1,(int)(available/190));var width=Math.Floor(available/columns)-10;
        foreach(FrameworkElement card in productCards.Children)card.Width=Math.Max(160,width);
    }
    private void RefreshProducts()
    {
        if(productCards is null)return;
        productCards.Children.Clear();productBadges.Clear();
        var products=catalog.Where(x=>string.IsNullOrWhiteSpace(search)?x.Category==category:x.Name.Contains(search.Trim(),StringComparison.OrdinalIgnoreCase)).ToArray();
        if(productCount is not null)productCount.Text=string.IsNullOrWhiteSpace(search)?$"{category} · {products.Length} pilihan tersedia":$"Hasil pencarian · {products.Length} menu";
        foreach(var product in products)
        {
            var body=Rows(new GridLength(90),Star,Auto);
            var art=new Grid { Background=Color(product.Category=="Minuman"?"#E9EFEC":"#F3F0E4"),ClipToBounds=true };
            art.Children.Add(Illustration(product.Category));
            if(product.ImageUrl is not null)
            {
                var photo=new Image { Stretch=Stretch.UniformToFill,Visibility=Visibility.Hidden };
                art.Children.Add(photo);_=LoadPhoto(photo,product.ImageUrl);
            }
            var badge=Text("",12,true,"#FFFFFF");
            var badgeBox=new Border { Child=badge,Background=Color("#205C49"),CornerRadius=new CornerRadius(7),Padding=new Thickness(8,4,8,4),HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(8),Visibility=Visibility.Collapsed };
            art.Children.Add(badgeBox);productBadges[product.Id]=badge;Place(body,art);
            var name=Text(product.Name,19,true);name.Margin=new Thickness(12,10,12,6);Place(body,name,1);
            var price=Columns(Star,Auto);price.Margin=new Thickness(12,0,12,12);
            Place(price,Text(product.Available?Money.Format(product.Price):"Sedang habis",18,true,product.Available?"#205C49":"#857467"));
            Place(price,Text(product.Available?"+":"—",25,false,"#205C49"),0,1);Place(body,price,2);
            var button=ActionButton("",async()=>{await Save(OrderRules.Add(current,product));RefreshCart(product.Id);RefreshBadges();},id:"Product-"+product.Id);
            button.Content=body;button.Width=200;button.Height=204;button.Padding=new Thickness(0);button.Background=Brushes.White;button.Foreground=Color("#233E35");button.BorderBrush=Color("#DEE6DF");
            button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Margin=new Thickness(0,0,10,10);button.IsEnabled=product.Available;
            System.Windows.Automation.AutomationProperties.SetName(button,$"Tambah {product.Name}, {Money.Format(product.Price)}");productCards.Children.Add(button);
        }
        if(products.Length==0)productCards.Children.Add(Empty("Menu belum ditemukan","Coba nama lain atau kosongkan pencarian."));
        RefreshBadges();SizeCards();
    }
    private void RefreshBadges()
    {
        foreach(var (id,badge) in productBadges)
        {
            var quantity=current.Lines.FirstOrDefault(x=>x.ProductId==id)?.Quantity??0;
            badge.Text=$"{quantity} dipilih";((Border)badge.Parent).Visibility=quantity>0?Visibility.Visible:Visibility.Collapsed;
        }
    }
    private static async Task LoadPhoto(Image image,string url)
    {
        var source=await ProductImages.GetAsync(url);if(source is not null){image.Source=source;image.Visibility=Visibility.Visible;}
    }
    private void RefreshCart(string? changedId=null)
    {
        if(cartHost is null)return;
        var offset=cartScroll?.VerticalOffset??0;
        var focusId=Keyboard.FocusedElement is DependencyObject focus?System.Windows.Automation.AutomationProperties.GetAutomationId(focus):"";
        cartHost.Content=Cart(true);
        var scroll=cartScroll;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>
        {
            if(scroll!=cartScroll||scroll is null)return;
            scroll.ScrollToVerticalOffset(offset);
            if(changedId is not null)
            {
                var row=Descendants<FrameworkElement>(scroll).FirstOrDefault(x=>System.Windows.Automation.AutomationProperties.GetAutomationId(x)=="Line-"+changedId);
                row?.BringIntoView();if(row is not null)Reveal(row);
            }
            if(focusId.StartsWith("Qty-",StringComparison.Ordinal))
                Descendants<Button>(scroll).FirstOrDefault(x=>System.Windows.Automation.AutomationProperties.GetAutomationId(x)==focusId)?.Focus();
        }));
    }
    internal static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);if(child is T found)yield return found;
            foreach(var nested in Descendants<T>(child))yield return nested;
        }
    }
    private Border Cart(bool editable)
    {
        var grid=Rows(Auto,Star,Auto);
        var header=new StackPanel { Margin=new Thickness(0,0,0,10) };
        var heading=Columns(Star,Auto);var title=new StackPanel();title.Children.Add(Text(editable?"Pesanan":"Rincian pesanan",24,true));
        title.Children.Add(Text($"{current.Lines.Sum(x=>x.Quantity)} item · {current.Number[^8..]}",13,false,"#65766E"));Place(heading,title);
        if(editable)
        {
            var expand=ActionButton(cartExpanded?"Kembali ke menu":"Perbesar",()=>{cartExpanded=!cartExpanded;RenderSelling();return Task.CompletedTask;},id:"ExpandCart");
            expand.FontSize=14;expand.Padding=new Thickness(10,8,10,8);Place(heading,expand,0,1);
        }
        header.Children.Add(heading);
        if(editable)
        {
            var nameRow=Columns(Auto,Star);nameRow.Margin=new Thickness(0,10,0,0);
            var label=Text("Nama",14,false,"#65766E");label.Margin=new Thickness(0,0,10,0);Place(nameRow,label);
            var name=Identify(new TextBox { Text=customerName,MaxLength=60,MinHeight=38,FontSize=16,Padding=new Thickness(10,6,10,6),ToolTip="Nama pelanggan (opsional), tersimpan otomatis" },"CustomerName","Nama pelanggan, opsional");
            name.TextChanged+=(_,_)=>{customerName=name.Text;nameDirty=customerName!=current.CustomerLabel;nameTimer.Stop();if(nameDirty)nameTimer.Start();};Place(nameRow,name,0,1);header.Children.Add(nameRow);
        }
        else if(!string.IsNullOrWhiteSpace(customerName))header.Children.Add(Text(customerName,16,false,"#65766E"));
        Place(grid,header);
        var lines=new StackPanel();
        foreach(var line in current.Lines)
        {
            var row=Columns(Star,Auto);row.MinHeight=68;row.Margin=new Thickness(0,0,0,2);
            var info=new StackPanel { VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,8,8,8) };
            info.Children.Add(Text(line.Name,18,true));
            var detail=Text(editable?line.SubtotalLabel:$"{line.Quantity} × {line.PriceLabel}   ·   {line.SubtotalLabel}",15,false,"#65766E");detail.ToolTip=$"{line.Quantity} × {line.PriceLabel}";detail.Margin=new Thickness(0,5,0,0);info.Children.Add(detail);Place(row,info);
            if(editable)
            {
                var controls=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };
                var minus=ActionButton("−",async()=>{await Save(OrderRules.Reduce(current,line.ProductId));RefreshCart();RefreshBadges();},id:"Qty-Minus-"+line.ProductId);
                minus.Width=44;minus.MinHeight=44;minus.Padding=new Thickness(0);minus.FontSize=24;minus.ToolTip=line.Quantity==1?"Hapus item":"Kurangi satu";
                System.Windows.Automation.AutomationProperties.SetName(minus,$"Kurangi {line.Name}");controls.Children.Add(minus);
                var quantity=Text(line.Quantity.ToString(),18,true);quantity.MinWidth=32;quantity.TextAlignment=TextAlignment.Center;controls.Children.Add(quantity);
                var plus=ActionButton("+",async()=>
                {
                    var product=catalog.FirstOrDefault(p=>p.Id==line.ProductId)??new Product(line.ProductId,line.Name,line.Category,line.UnitPrice);
                    await Save(OrderRules.Add(current,product));RefreshCart(line.ProductId);RefreshBadges();
                },id:"Qty-Plus-"+line.ProductId);
                plus.Width=44;plus.MinHeight=44;plus.Padding=new Thickness(0);plus.FontSize=24;
                System.Windows.Automation.AutomationProperties.SetName(plus,$"Tambah {line.Name}");controls.Children.Add(plus);Place(row,controls,0,1);
            }
            var container=Identify(new Border { Child=row,BorderBrush=Color("#E8EDE6"),BorderThickness=new Thickness(0,0,0,1) },"Line-"+line.ProductId);
            lines.Children.Add(container);
        }
        var viewport=Scroll(current.Lines.Length==0?Empty("Belum ada pesanan","Klik makanan atau minuman untuk menambahkannya di sini."):lines,editable?"CartViewport":"ReviewViewport");
        if(editable)cartScroll=viewport;Place(grid,viewport,1);
        var bottom=new StackPanel { Margin=new Thickness(0,8,0,0) };
        var total=Columns(Star,Auto);total.Margin=new Thickness(0,0,0,8);Place(total,Text("Total",17,false,"#65766E"));Place(total,Identify(Text(current.TotalLabel,28,true),"CartTotal"),0,1);bottom.Children.Add(total);
        if(editable)
        {
            var buttons=Columns(Star,new GridLength(10),new GridLength(1.2,GridUnitType.Star));
            var hold=ActionButton("Simpan dulu",async()=>{await Save(OrderRules.Hold(current,customerName));SetCurrent(Order.New());RenderSelling();},id:"HoldOrder");
            hold.IsEnabled=current.Lines.Length>0;hold.Padding=new Thickness(10);Place(buttons,hold);
            var pay=ActionButton("Bayar  →",()=>{tendered="";method=PaymentMethod.Cash;RenderPayment();return Task.CompletedTask;},true,"PayOrder");
            pay.IsEnabled=current.Lines.Length>0;pay.Padding=new Thickness(10);Place(buttons,pay,0,2);bottom.Children.Add(buttons);
        }
        Place(grid,bottom,2);return Surface(grid,16);
    }
}
