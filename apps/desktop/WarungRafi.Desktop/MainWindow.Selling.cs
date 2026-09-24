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
        var grid=Columns(Star,new GridLength(18),new GridLength(420));
        grid.ColumnDefinitions[2].MinWidth=380;
        grid.SizeChanged+=(_,_)=>grid.ColumnDefinitions[2].Width=cartExpanded?Star:new GridLength(Math.Clamp(grid.ActualWidth*0.35,380,440));
        var left=Rows(Auto,Auto,Star);
        var heading=Columns(Star,new GridLength(200));heading.Margin=new Thickness(0,0,0,18);
        var title=new StackPanel();title.Children.Add(Text("Pilih menu",25,true));
        productCount=Text("",13,false,"#68766A");productCount.Margin=new Thickness(0,4,0,0);title.Children.Add(productCount);Place(heading,title);
        var find=Identify(new TextBox { Text=search,ToolTip="Cari nama menu di semua kategori",MaxLength=60,VerticalAlignment=VerticalAlignment.Center,FontSize=16,MinHeight=44 },"MenuSearch","Cari menu");
        var searchBox=InputWithHint(find,"Cari menu…");searchBox.VerticalAlignment=VerticalAlignment.Center;Place(heading,searchBox,0,1);Place(left,heading);
        find.TextChanged+=(_,_)=>{search=find.Text;RefreshProducts();};
        var tabs=new System.Windows.Controls.Primitives.UniformGrid { Columns=4,Margin=new Thickness(0,0,0,16) };
        foreach(var name in new[]{"Nasi","Lauk","Sundukan","Minuman"})
        {
            var chosen=name;var tab=ActionButton(name,()=>{category=chosen;search="";RenderSelling();return Task.CompletedTask;},id:"Category-"+name);
            SelectTab(tab,category==name&&search.Length==0);tab.FontSize=16;tab.Padding=new Thickness(6,8,6,8);tab.Margin=new Thickness(0,0,6,0);tabs.Children.Add(tab);
        }
        Place(left,tabs,1);
        productCards=new WrapPanel();productScroll=Scroll(productCards,"MenuViewport");Place(left,productScroll,2);
        productScroll.SizeChanged+=(_,_)=>SizeCards();
        productScroll.ScrollChanged+=(_,e)=>{if(e.ViewportWidthChange!=0)SizeCards();};
        var catalogPanel=Surface(left,20);Identify(catalogPanel,"CatalogPanel");Place(grid,catalogPanel);
        cartHost=new ContentControl { HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch };
        Place(grid,cartHost,0,2);
        if(cartExpanded){catalogPanel.Visibility=Visibility.Collapsed;grid.ColumnDefinitions[0].Width=new GridLength(0);grid.ColumnDefinitions[1].Width=new GridLength(0);grid.ColumnDefinitions[2].Width=Star;}
        RefreshProducts();RefreshCart();Present("sell",grid);
    }
    private void SizeCards()
    {
        if(productCards is null||productScroll is null)return;
        var available=Math.Max(240,productScroll.ViewportWidth>0?productScroll.ViewportWidth:productScroll.ActualWidth-18);
        var columns=Math.Clamp((int)(available/300),1,4);
        var count=productCards.Children.Count;
        // Four menu choices form a balanced 2x2 grid instead of a stranded 3+1 row.
        if(count>columns&&count%columns==1&&columns>2)columns--;
        var width=Math.Floor(available/columns)-12;
        foreach(FrameworkElement card in productCards.Children)card.Width=Math.Max(228,width);
    }
    private void RefreshProducts()
    {
        if(productCards is null)return;
        productCards.Children.Clear();productBadges.Clear();
        var products=catalog.Where(x=>string.IsNullOrWhiteSpace(search)?x.Category==category:x.Name.Contains(search.Trim(),StringComparison.OrdinalIgnoreCase)).ToArray();
        if(productCount is not null)productCount.Text=string.IsNullOrWhiteSpace(search)?$"{category} · {products.Length} menu":$"{products.Length} menu ditemukan";
        foreach(var product in products)
        {
            var body=Columns(new GridLength(112),Star);body.Margin=new Thickness(12);
            var art=new Grid { ClipToBounds=true };
            var drawing=Illustration(product.Category,product.Id);drawing.Height=92;art.Children.Add(drawing);
            var artwork=new Border { Child=art,CornerRadius=new CornerRadius(12),Background=Color(product.Category=="Minuman"?"#E5EDE5":"#EEECE0"),Margin=new Thickness(0,0,14,0) };
            if(product.ImageUrl is not null)
            {
                var photo=new Image { Stretch=Stretch.UniformToFill,Visibility=Visibility.Hidden };
                art.Children.Add(photo);_=LoadPhoto(photo,product.ImageUrl);
            }
            Place(body,artwork);
            var info=Rows(Auto,Star,Auto);info.Margin=new Thickness(0,3,2,3);
            var top=Columns(Star,Auto);Place(top,Text(product.Category.ToUpperInvariant(),10,true,"#748170"));
            var badge=Text("",11,true,"#FFFFFF");
            var badgeBox=new Border { Child=badge,Background=Color("#234F3F"),CornerRadius=new CornerRadius(6),Padding=new Thickness(6,3,6,3),Visibility=Visibility.Collapsed };
            Place(top,badgeBox,0,1);productBadges[product.Id]=badge;Place(info,top);
            var name=Text(product.Name,20,true);name.Margin=new Thickness(0,8,0,10);Place(info,name,1);
            var price=Columns(Star,Auto);
            Place(price,Text(product.Available?Money.Format(product.Price):"Habis",20,true,product.Available?"#234F3F":"#857467"));
            var plus=Text(product.Available?"+":"—",24,false,"#234F3F");plus.HorizontalAlignment=HorizontalAlignment.Center;
            Place(price,new Border { Child=plus,Width=34,Height=34,Background=Color("#E8EFE5"),CornerRadius=new CornerRadius(10) },0,1);Place(info,price,2);Place(body,info,0,1);
            var button=ActionButton("",async()=>{await Save(OrderRules.Add(current,product));RefreshCart(product.Id);RefreshBadges();},id:"Product-"+product.Id);
            button.Content=body;button.Width=342;button.Height=172;button.Padding=new Thickness(0);button.Background=Color("#FAFBF8");button.Foreground=Color("#243D33");button.BorderBrush=Color("#E1E7DC");
            button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Stretch;button.Margin=new Thickness(0,0,12,12);button.IsEnabled=product.Available;button.Tag=product.Id;
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
            badge.Text=$"{quantity} ×";((Border)badge.Parent).Visibility=quantity>0?Visibility.Visible:Visibility.Collapsed;
            if(productCards?.Children.OfType<Button>().FirstOrDefault(x=>x.Tag as string==id) is { } card)
            {
                card.BorderBrush=Color(quantity>0?"#9DB99D":"#E1E7DC");
                card.Background=Color(quantity>0?"#F0F5EC":"#FAFBF8");
            }
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
        title.Children.Add(Text($"{current.Lines.Sum(x=>x.Quantity)} item · #{current.Number[^8..]}",12,false,"#68766A"));Place(heading,title);
        if(editable)
        {
            var expand=ActionButton(cartExpanded?"Kembali ke menu":"Perbesar",()=>{cartExpanded=!cartExpanded;RenderSelling();return Task.CompletedTask;},id:"ExpandCart");
            expand.FontSize=13;expand.Padding=new Thickness(10,8,10,8);expand.Background=Brushes.White;expand.BorderBrush=Color("#DEE6D9");Place(heading,expand,0,1);
        }
        header.Children.Add(heading);
        if(editable)
        {
            var nameRow=Columns(Auto,Star);nameRow.Margin=new Thickness(0,10,0,0);
            var label=Text("Pelanggan",13,false,"#68766A");label.Margin=new Thickness(0,0,10,0);Place(nameRow,label);
            var name=Identify(new TextBox { Text=customerName,MaxLength=60,MinHeight=38,FontSize=16,Padding=new Thickness(10,6,10,6),ToolTip="Nama pelanggan (opsional), tersimpan otomatis" },"CustomerName","Nama pelanggan, opsional");
            name.TextChanged+=(_,_)=>{customerName=name.Text;nameDirty=customerName!=current.CustomerLabel;nameTimer.Stop();if(nameDirty)nameTimer.Start();};Place(nameRow,InputWithHint(name,"Nama (opsional)"),0,1);header.Children.Add(nameRow);
        }
        else if(!string.IsNullOrWhiteSpace(customerName))header.Children.Add(Text(customerName,16,false,"#65766E"));
        Place(grid,header);
        var lines=new StackPanel();
        foreach(var line in current.Lines)
        {
            var row=Rows(Auto,Auto);row.MinHeight=70;row.Margin=new Thickness(0,3,0,3);
            var top=Columns(Star,Auto);var itemName=Text(line.Name,18,true);itemName.Margin=new Thickness(0,0,12,0);Place(top,itemName);
            Place(top,Text(line.SubtotalLabel,18,true,"#234F3F"),0,1);Place(row,top);
            var detail=Columns(Star,Auto);Place(detail,Text($"{line.Quantity} × {line.PriceLabel}",14,false,"#68766A"));
            if(editable)
            {
                var controls=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };
                var minus=ActionButton("−",async()=>{await Save(OrderRules.Reduce(current,line.ProductId));RefreshCart();RefreshBadges();},id:"Qty-Minus-"+line.ProductId);
                minus.Width=44;minus.MinHeight=44;minus.Padding=new Thickness(0);minus.FontSize=22;minus.Background=Brushes.Transparent;minus.ToolTip=line.Quantity==1?"Hapus item":"Kurangi satu";
                System.Windows.Automation.AutomationProperties.SetName(minus,$"Kurangi {line.Name}");controls.Children.Add(minus);
                var quantity=Text(line.Quantity.ToString(),17,true);quantity.MinWidth=24;quantity.TextAlignment=TextAlignment.Center;controls.Children.Add(quantity);
                var plus=ActionButton("+",async()=>
                {
                    var product=catalog.FirstOrDefault(p=>p.Id==line.ProductId)??new Product(line.ProductId,line.Name,line.Category,line.UnitPrice);
                    await Save(OrderRules.Add(current,product));RefreshCart(line.ProductId);RefreshBadges();
                },id:"Qty-Plus-"+line.ProductId);
                plus.Width=44;plus.MinHeight=44;plus.Padding=new Thickness(0);plus.FontSize=22;plus.Background=Brushes.Transparent;
                System.Windows.Automation.AutomationProperties.SetName(plus,$"Tambah {line.Name}");controls.Children.Add(plus);
                Place(detail,new Border { Child=controls,Background=Color("#F0F3EC"),CornerRadius=new CornerRadius(10),Margin=new Thickness(0,2,0,0) },0,1);
            }
            Place(row,detail,1);
            var container=Identify(new Border { Child=row,BorderBrush=Color("#E7ECE3"),BorderThickness=new Thickness(0,0,0,1) },"Line-"+line.ProductId);
            lines.Children.Add(container);
        }
        var viewport=Scroll(current.Lines.Length==0?Empty("Belum ada pesanan","Klik makanan atau minuman untuk menambahkannya di sini."):lines,editable?"CartViewport":"ReviewViewport");
        if(editable)cartScroll=viewport;Place(grid,viewport,1);
        var bottom=new StackPanel { Margin=new Thickness(0,8,0,0) };
        var total=Columns(Star,Auto);total.Margin=new Thickness(0,0,0,8);Place(total,Text("Total pesanan",15,false,"#68766A"));Place(total,Identify(Text(current.TotalLabel,30,true,"#234F3F"),"CartTotal"),0,1);bottom.Children.Add(total);
        if(editable)
        {
            var buttons=Columns(Star,new GridLength(10),new GridLength(1.2,GridUnitType.Star));
            var hold=ActionButton("Simpan dulu",async()=>{await Save(OrderRules.Hold(current,customerName));SetCurrent(Order.New());RenderSelling();},id:"HoldOrder");
            hold.IsEnabled=current.Lines.Length>0;hold.Padding=new Thickness(10);hold.MinHeight=52;hold.Background=Brushes.White;hold.BorderBrush=Color("#D8E1D3");hold.FontSize=16;Place(buttons,hold);
            var pay=ActionButton("Bayar  →",()=>{tendered="";method=PaymentMethod.Cash;RenderPayment();return Task.CompletedTask;},true,"PayOrder");
            pay.IsEnabled=current.Lines.Length>0;pay.Padding=new Thickness(10);pay.MinHeight=52;Place(buttons,pay,0,2);bottom.Children.Add(buttons);
        }
        Place(grid,bottom,2);return Surface(grid,16);
    }
}
