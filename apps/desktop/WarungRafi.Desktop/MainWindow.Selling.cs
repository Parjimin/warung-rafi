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
    private readonly Dictionary<string,Button> productButtons=new();

    private void RenderSelling()
    {
        if(!ready)return;
        var grid=Columns(Star,new GridLength(20),new GridLength(350));
        grid.ColumnDefinitions[2].MinWidth=310;
        grid.SizeChanged+=(_,_)=>grid.ColumnDefinitions[2].Width=cartExpanded?Star:new GridLength(Math.Clamp(grid.ActualWidth*0.30,310,370));
        var left=Rows(Auto,Auto,Star);
        var heading=Columns(Star,new GridLength(190));heading.Margin=new Thickness(0,0,0,18);
        var title=new StackPanel();
        var eyebrow=Text("PILIH MENU · TAMBAH KE PESANAN",10,true,"#778A80");eyebrow.Margin=new Thickness(0,0,0,3);title.Children.Add(eyebrow);
        title.Children.Add(Text("Hari ini, mau apa?",28,true,"#1F342D"));
        productCount=Text("",13,false,"#68766A");productCount.Margin=new Thickness(0,4,0,0);title.Children.Add(productCount);Place(heading,title);
        var find=Identify(new TextBox { Text=search,ToolTip="Cari nama menu di semua kategori",MaxLength=60,VerticalAlignment=VerticalAlignment.Center,FontSize=16,MinHeight=44 },"MenuSearch","Cari menu");
        var searchBox=InputWithHint(find,"Cari menu…  F2");searchBox.VerticalAlignment=VerticalAlignment.Center;Place(heading,searchBox,0,1);Place(left,heading);
        find.TextChanged+=(_,_)=>{search=find.Text;RefreshProducts();};
        var tabs=new System.Windows.Controls.Primitives.UniformGrid { Columns=4,Margin=new Thickness(0,0,0,16) };
        foreach(var name in new[]{"Nasi","Lauk","Sundukan","Minuman"})
        {
            var chosen=name;var tab=ActionButton(name,()=>{category=chosen;search="";RenderSelling();return Task.CompletedTask;},id:"Category-"+name);
            tab.Tag=name;var selected=category==name&&search.Length==0;SelectTab(tab,selected);
            tab.Style=Component("CategoryButton");
            var tabBody=new StackPanel { HorizontalAlignment=HorizontalAlignment.Left };
            tabBody.Children.Add(Text(name,15,true,selected?"#FFFFFF":"#294B40"));
            var count=Text($"{catalog.Count(p=>p.Category==name)} menu",10,false,selected?"#C7D7CE":"#72867B");count.HorizontalAlignment=HorizontalAlignment.Left;tabBody.Children.Add(count);
            tab.Content=tabBody;tab.FontSize=16;tab.Padding=new Thickness(12,8,12,8);tab.Margin=new Thickness(0,0,6,0);tabs.Children.Add(tab);
        }
        Place(left,tabs,1);
        productCards=new WrapPanel();productScroll=Scroll(productCards,"MenuViewport");Place(left,productScroll,2);
        productScroll.SizeChanged+=(_,_)=>SizeCards();
        productScroll.ScrollChanged+=(_,e)=>{if(e.ViewportWidthChange!=0)SizeCards();};
        var catalogPanel=new Border {Child=left};Identify(catalogPanel,"CatalogPanel");Place(grid,catalogPanel);
        cartHost=new ContentControl { HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch };
        Place(grid,cartHost,0,2);
        if(cartExpanded){catalogPanel.Visibility=Visibility.Collapsed;grid.ColumnDefinitions[0].Width=new GridLength(0);grid.ColumnDefinitions[1].Width=new GridLength(0);grid.ColumnDefinitions[2].Width=Star;}
        RefreshProducts();RefreshCart();Present("sell",grid);
    }
    private void SizeCards()
    {
        if(productCards is null||productScroll is null)return;
        // Four compact columns at normal laptop widths; three on narrow windows.
        var available=Math.Max(1,productScroll.ViewportWidth>0?productScroll.ViewportWidth:productScroll.ActualWidth-18);
        var columns=available>=620?4:available>=390?3:2;
        productCards.Width=available;
        var width=Math.Max(1,Math.Floor(available/columns)-8);
        foreach(FrameworkElement card in productCards.Children)card.Width=width;
    }
    private void RefreshProducts()
    {
        if(productCards is null)return;
        productCards.Children.Clear();productBadges.Clear();productButtons.Clear();
        var products=catalog.Where(x=>string.IsNullOrWhiteSpace(search)?x.Category==category:x.Name.Contains(search.Trim(),StringComparison.OrdinalIgnoreCase)).ToArray();
        if(productCount is not null)productCount.Text=string.IsNullOrWhiteSpace(search)?$"{category} · {products.Length} menu":$"{products.Length} menu ditemukan";
        foreach(var product in products)
        {
            var palette=MenuPalette(product.Category);
            var body=Rows(Star,Auto);body.Margin=new Thickness(12,10,12,10);
            var name=Text(product.Name,15,true,"#20322C");name.Margin=new Thickness(0,0,12,4);name.MaxHeight=40;name.TextTrimming=TextTrimming.CharacterEllipsis;Place(body,name);
            var price=Columns(Star,Auto);
            Place(price,Text(product.Available?Money.Format(product.Price):"Habis",17,true,product.Available?"#214F40":"#986326"));
            Place(price,Text(product.Available?"+":"—",20,false,"#82938A"),0,1);Place(body,price,1);
            var badge=Text("",10,true,"#FFFFFF");
            var badgeBox=new Border { Child=badge,Background=Token("Forest"),CornerRadius=new CornerRadius(4),Padding=new Thickness(4,1,4,1),Visibility=Visibility.Collapsed,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,4,4,0) };
            productBadges[product.Id]=badge;
            var button=ActionButton("",async()=>{await Save(OrderRules.Add(current,product));RefreshCart(product.Id);RefreshBadges();},id:"Product-"+product.Id);
            var cardLayers=new Grid { ClipToBounds=true };cardLayers.Children.Add(body);cardLayers.Children.Add(badgeBox);
            button.Style=Component("ProductCard");button.Content=cardLayers;button.Width=170;button.Height=94;button.ToolTip=product.Name;button.Padding=new Thickness(0);button.Background=Token("SurfaceBrush");button.Foreground=Token("Ink");button.BorderBrush=Token("Line");
            button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Stretch;button.Margin=new Thickness(0,0,8,8);button.IsEnabled=product.Available;button.Tag=product.Id;productButtons[product.Id]=button;
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
            badge.Text=quantity.ToString();((Border)badge.Parent).Visibility=quantity>0?Visibility.Visible:Visibility.Collapsed;
            if(productButtons.TryGetValue(id,out var card))
            {
                var palette=MenuPalette(catalog.FirstOrDefault(p=>p.Id==id)?.Category??"");
                card.BorderBrush=Color(quantity>0?palette.Accent:palette.Line);
                card.BorderThickness=new Thickness(quantity>0?2:1);
                card.Background=GlassBrush(quantity>0);
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
    private static IEnumerable<TextBlock> LogicalTexts(DependencyObject root)
    {
        if(root is TextBlock t)yield return t;
        foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach(var text in LogicalTexts(child))yield return text;
    }
    private Border Cart(bool editable)
    {
        var grid=Rows(Auto,Star,Auto);
        var header=new StackPanel { Margin=new Thickness(0,0,0,12) };
        var heading=Columns(Star,Auto);var title=new StackPanel();title.Children.Add(Text("PESANAN AKTIF / "+current.Number[^8..],10,true,"#718078"));title.Children.Add(Text(editable?"Pesanan pelanggan":"Rincian pesanan",22,true,"#20322C"));
        Place(heading,title);
        if(editable)
        {
            var expand=ActionButton(cartExpanded?"Kembali ke menu":"Perbesar",()=>{cartExpanded=!cartExpanded;RenderSelling();return Task.CompletedTask;},id:"ExpandCart");
            expand.FontSize=11;expand.Padding=new Thickness(10,8,10,8);expand.Background=Brushes.White;expand.BorderBrush=Color("#DEE6D9");Place(heading,expand,0,1);
        }
        header.Children.Add(heading);
        if(editable)
        {
            var nameRow=Columns(Auto,Star);nameRow.Margin=new Thickness(0,10,0,0);

            var name=Identify(new TextBox { Text=customerName,MaxLength=60,MinHeight=44,FontSize=14,Padding=new Thickness(10,6,10,6),ToolTip="Nama pelanggan (opsional), tersimpan otomatis" },"CustomerName","Nama pelanggan, opsional");
            name.TextChanged+=(_,_)=>{customerName=name.Text;nameDirty=customerName!=current.CustomerLabel;nameTimer.Stop();if(nameDirty)nameTimer.Start();};Place(nameRow,InputWithHint(name,"Nama pelanggan (opsional)"),0,1);header.Children.Add(nameRow);
        }
        else if(!string.IsNullOrWhiteSpace(customerName))header.Children.Add(Text(customerName,16,false,"#65766E"));
        Place(grid,header);
        var lines=new StackPanel();
        foreach(var line in current.Lines)
        {
            var row=Rows(Auto,Auto);row.MinHeight=70;row.Margin=new Thickness(0,3,0,3);
            var top=Columns(Star,Auto);var itemName=Text(line.Name,15,true);itemName.Margin=new Thickness(0,0,12,0);Place(top,itemName);
            Place(top,Text(line.SubtotalLabel,15,true,"#234F3F"),0,1);Place(row,top);
            var detail=Columns(Star,Auto);Place(detail,Text($"{line.PriceLabel} / pcs",12,false,"#68766A"));
            if(editable)
            {
                var controls=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };
                var minus=ActionButton("−",async()=>{await Save(OrderRules.Reduce(current,line.ProductId));RefreshCart();RefreshBadges();},id:"Qty-Minus-"+line.ProductId);
                minus.Style=Component("StepperButton");minus.Width=44;minus.MinHeight=44;minus.Padding=new Thickness(0);minus.FontSize=22;minus.Background=Brushes.Transparent;minus.ToolTip=line.Quantity==1?"Hapus item":"Kurangi satu";
                System.Windows.Automation.AutomationProperties.SetName(minus,$"Kurangi {line.Name}");controls.Children.Add(minus);
                var quantity=Text(line.Quantity.ToString(),17,true);quantity.MinWidth=24;quantity.TextAlignment=TextAlignment.Center;controls.Children.Add(quantity);
                var plus=ActionButton("+",async()=>
                {
                    var product=catalog.FirstOrDefault(p=>p.Id==line.ProductId)??new Product(line.ProductId,line.Name,line.Category,line.UnitPrice);
                    await Save(OrderRules.Add(current,product));RefreshCart(line.ProductId);RefreshBadges();
                },id:"Qty-Plus-"+line.ProductId);
                plus.Style=Component("StepperButton");plus.Width=44;plus.MinHeight=44;plus.Padding=new Thickness(0);plus.FontSize=22;plus.Background=Brushes.Transparent;
                System.Windows.Automation.AutomationProperties.SetName(plus,$"Tambah {line.Name}");controls.Children.Add(plus);
                Place(detail,new Border { Child=controls,Background=Color("#F0F3EC"),CornerRadius=new CornerRadius(10),Margin=new Thickness(0,2,0,0) },0,1);
            }
            Place(row,detail,1);
            var container=Identify(new Border { Child=row,BorderBrush=Color("#E7ECE3"),BorderThickness=new Thickness(0,0,0,1) },"Line-"+line.ProductId);
            lines.Children.Add(container);
        }
        var viewport=Scroll(current.Lines.Length==0?Empty("Mulai pesanan pertama","Pilih menu di sebelah kiri. Semua tersimpan di laptop."):lines,editable?"CartViewport":"ReviewViewport");
        if(editable)cartScroll=viewport;Place(grid,viewport,1);
        var bottom=new StackPanel { Margin=new Thickness(0,8,0,0) };
        var total=Columns(Star,Auto);total.Margin=new Thickness(0,0,0,8);Place(total,Text("TOTAL BAYAR",11,true,"#68766A"));Place(total,Identify(Text(current.TotalLabel,30,true,"#203F36"),"CartTotal"),0,1);
        bottom.Children.Add(new Border { Child=total,BorderBrush=Color("#BDD0C4"),BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(0) });
        if(editable)
        {
            var buttons=Columns(new GridLength(104),new GridLength(8),Star);
            var hold=ActionButton("Simpan dulu",async()=>{await Save(OrderRules.Hold(current,customerName));SetCurrent(Order.New());RenderSelling();},id:"HoldOrder");
            hold.IsEnabled=current.Lines.Length>0;hold.Padding=new Thickness(10);hold.MinHeight=52;hold.Background=Brushes.White;hold.BorderBrush=Color("#D8E1D3");hold.FontSize=14;Place(buttons,hold);
            var pay=ActionButton("Bayar  →",()=>{tendered="";method=PaymentMethod.Cash;RenderPayment();return Task.CompletedTask;},true,"PayOrder");
            pay.IsEnabled=current.Lines.Length>0;pay.Padding=new Thickness(10);pay.MinHeight=52;Place(buttons,pay,0,2);bottom.Children.Add(buttons);
        }
        Place(grid,new Border {Child=bottom,Background=Token("ElevatedBrush"),Padding=new Thickness(0,8,0,0)},2);
        var slip=Surface(grid,16);slip.Style=Component("OrderSlip");
        if(!editable)
        {
            // Match the prototype's dark payment summary without touching order logic.
            slip.Background=Token("RailBrush");((Border)grid.Children[grid.Children.Count-1]).Background=Token("RailBrush");
            foreach(var text in LogicalTexts(grid))text.Foreground=Brushes.White;
        }
        return slip;
    }
}
