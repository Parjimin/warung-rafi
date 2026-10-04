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
        var grid=Columns(Star,new GridLength(22),new GridLength(344));
        grid.ColumnDefinitions[2].MinWidth=300;
        grid.SizeChanged+=(_,_)=>grid.ColumnDefinitions[2].Width=cartExpanded?Star:new GridLength(Math.Clamp(grid.ActualWidth*0.28,300,360));
        var left=Rows(Auto,Auto,Star);
        var heading=Columns(Star,new GridLength(190));heading.Margin=new Thickness(0,0,0,14);
        var title=new StackPanel();
        var eyebrow=Text("DAPUR WARUNG RAFI",10,true,"#898272");eyebrow.Margin=new Thickness(0,0,0,3);title.Children.Add(eyebrow);
        title.Children.Add(Text("Menu pilihan, siap disajikan.",26,true,"#282722"));
        productCount=Text("",13,false,"#817E72");productCount.Margin=new Thickness(0,4,0,0);title.Children.Add(productCount);Place(heading,title);
        var find=Identify(new TextBox { Text=search,ToolTip="Cari nama menu di semua kategori",MaxLength=60,VerticalAlignment=VerticalAlignment.Center,FontSize=13,MinHeight=44 },"MenuSearch","Cari menu");
        var searchBox=InputWithHint(find,"Cari menu…  F2");searchBox.VerticalAlignment=VerticalAlignment.Center;Place(heading,searchBox,0,1);Place(left,heading);
        find.TextChanged+=(_,_)=>{search=find.Text;RefreshProducts();};
        var tabs=new System.Windows.Controls.Primitives.UniformGrid { Columns=4,Margin=new Thickness(0,0,0,12) };
        foreach(var name in new[]{"Nasi","Lauk","Sundukan","Minuman"})
        {
            var chosen=name;var tab=ActionButton(name,()=>{category=chosen;search="";RenderSelling();return Task.CompletedTask;},id:"Category-"+name);
            tab.Tag=name;var selected=category==name&&search.Length==0;SelectTab(tab,selected);
            tab.Style=Component("CategoryButton");
            var tabBody=new DockPanel { LastChildFill=true };
            var tabName=Text(name,14,true,selected?"#FFFFFF":"#282722");tabName.Margin=new Thickness(0,0,10,0);tabBody.Children.Add(tabName);
            var count=Text($"{catalog.Count(p=>p.Category==name)} menu",10,false,selected?"#FCB711":"#817E72");count.HorizontalAlignment=HorizontalAlignment.Left;tabBody.Children.Add(count);
            tab.Content=tabBody;tab.FontSize=16;tab.Padding=new Thickness(12,8,12,8);tab.Margin=new Thickness(0,0,6,0);tabs.Children.Add(tab);
        }
        Place(left,tabs,1);
        productCards=new WrapPanel();productScroll=Scroll(productCards,"MenuViewport");Place(left,productScroll,2);
        productScroll.SizeChanged+=(_,_)=>SizeCards();
        productScroll.ScrollChanged+=(_,e)=>{if(e.ViewportWidthChange!=0||e.ViewportHeightChange!=0)SizeCards();};
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
        var height=available>=620?Math.Clamp(Math.Floor(productScroll.ViewportHeight/8)-6,56,88):76;
        foreach(FrameworkElement card in productCards.Children){card.Width=width;if(card is Button)card.Height=height;}
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
            var body=Rows(Star,Auto);body.Margin=new Thickness(10,6,10,6);
            var name=Text(product.Name,13,true,"#282722");name.Margin=new Thickness(0,0,12,2);name.MaxHeight=32;name.TextTrimming=TextTrimming.CharacterEllipsis;Place(body,name);
            var price=Columns(Star,Auto);
            Place(price,Text(product.Available?Money.Format(product.Price):"Habis",13,false,product.Available?"#82704C":"#986326"));
            Place(price,Text(product.Available?"+":"—",16,false,"#988557"),0,1);Place(body,price,1);
            var badge=Text("",10,true,"#FFFFFF");
            var badgeBox=new Border { Child=badge,Background=Token("RailBrush"),CornerRadius=new CornerRadius(4),Padding=new Thickness(4,1,4,1),Visibility=Visibility.Collapsed,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,4,4,0) };
            productBadges[product.Id]=badge;
            var button=ActionButton("",async()=>{await Save(OrderRules.Add(current,product));RefreshCart(product.Id);RefreshBadges();},id:"Product-"+product.Id);
            var cardLayers=new Grid { ClipToBounds=true };cardLayers.Children.Add(body);cardLayers.Children.Add(badgeBox);
            button.Style=Component("ProductCard");button.Content=cardLayers;button.Width=170;button.Height=64;button.ToolTip=product.Name;button.Padding=new Thickness(0);button.Background=Token("SurfaceBrush");button.Foreground=Token("Ink");button.BorderBrush=Token("Line");
            button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Stretch;button.Margin=new Thickness(0,0,8,6);button.IsEnabled=product.Available;button.Tag=product.Id;productButtons[product.Id]=button;
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
                card.BorderBrush=Color(quantity>0?"#E2AD2F":"#E3DED2");
                card.BorderThickness=new Thickness(1);
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
        var header=new StackPanel { Margin=new Thickness(18,10,18,8) };
        var heading=Columns(Star,Auto);var title=new StackPanel();title.Children.Add(Text("PESANAN AKTIF / "+current.Number[^8..],10,true,"#AA842D"));title.Children.Add(Text(editable?"Pesanan baru":"Rincian pesanan",20,true,"#282722"));
        Place(heading,title);
        if(editable)
        {
            var expand=ActionButton(cartExpanded?"Kembali ke menu":"Perbesar",()=>{cartExpanded=!cartExpanded;RenderSelling();return Task.CompletedTask;},id:"ExpandCart");
            expand.FontSize=11;expand.Padding=new Thickness(10,8,10,8);expand.Background=Brushes.White;expand.BorderBrush=Color("#E8DFCD");Place(heading,expand,0,1);
        }
        header.Children.Add(heading);
        if(editable)
        {
            var nameRow=Columns(Auto,Star);nameRow.Margin=new Thickness(0,8,0,0);

            var name=Identify(new TextBox { Text=customerName,MaxLength=60,MinHeight=44,FontSize=13,Padding=new Thickness(10,6,10,6),ToolTip="Nama pelanggan (opsional), tersimpan otomatis" },"CustomerName","Nama pelanggan, opsional");
            name.TextChanged+=(_,_)=>{customerName=name.Text;nameDirty=customerName!=current.CustomerLabel;nameTimer.Stop();if(nameDirty)nameTimer.Start();};Place(nameRow,InputWithHint(name,"Nama pelanggan (opsional)"),0,1);header.Children.Add(nameRow);
        }
        else if(!string.IsNullOrWhiteSpace(customerName))header.Children.Add(Text(customerName,16,false,"#817E72"));
        Place(grid,new Border {Child=header,Background=Color("#FFFAEE"),BorderBrush=Token("Forest"),BorderThickness=new Thickness(0,3,0,0),CornerRadius=new CornerRadius(16,16,0,0)});
        var lines=new StackPanel();
        foreach(var line in current.Lines)
        {
            var row=Rows(Auto,Auto);row.MinHeight=66;row.Margin=new Thickness(0,1,0,1);
            var top=Columns(Star,Auto);var itemName=Text(line.Name,13,true);itemName.Margin=new Thickness(0,0,12,0);Place(top,itemName);
            Place(top,Text(line.SubtotalLabel,13,true,"#282722"),0,1);Place(row,top);
            var detail=Columns(Star,Auto);Place(detail,Text($"{line.PriceLabel} / pcs",12,false,"#817E72"));
            if(editable)
            {
                var controls=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };
                var minus=ActionButton("−",async()=>{await Save(OrderRules.Reduce(current,line.ProductId));RefreshCart();RefreshBadges();},id:"Qty-Minus-"+line.ProductId);
                minus.Style=Component("StepperButton");minus.Width=44;minus.MinHeight=44;minus.Padding=new Thickness(0);minus.FontSize=22;minus.Background=Brushes.Transparent;minus.ToolTip=line.Quantity==1?"Hapus item":"Kurangi satu";
                System.Windows.Automation.AutomationProperties.SetName(minus,$"Kurangi {line.Name}");controls.Children.Add(minus);
                var quantity=Text(line.Quantity.ToString(),14,true);quantity.MinWidth=24;quantity.TextAlignment=TextAlignment.Center;controls.Children.Add(quantity);
                var plus=ActionButton("+",async()=>
                {
                    var product=catalog.FirstOrDefault(p=>p.Id==line.ProductId)??new Product(line.ProductId,line.Name,line.Category,line.UnitPrice);
                    await Save(OrderRules.Add(current,product));RefreshCart(line.ProductId);RefreshBadges();
                },id:"Qty-Plus-"+line.ProductId);
                plus.Style=Component("StepperButton");plus.Width=44;plus.MinHeight=44;plus.Padding=new Thickness(0);plus.FontSize=22;plus.Background=Brushes.Transparent;
                System.Windows.Automation.AutomationProperties.SetName(plus,$"Tambah {line.Name}");controls.Children.Add(plus);
                Place(detail,new Border { Child=controls,Background=Color("#F6F3EC"),CornerRadius=new CornerRadius(10),Margin=new Thickness(0,2,0,0) },0,1);
            }
            Place(row,detail,1);
            var container=Identify(new Border { Child=row,BorderBrush=Color("#E7E1D5"),BorderThickness=new Thickness(0,0,0,1) },"Line-"+line.ProductId);
            lines.Children.Add(container);
        }
        var viewport=Scroll(current.Lines.Length==0?Empty("Mulai pesanan pertama","Pilih menu di sebelah kiri. Semua tersimpan di laptop."):lines,editable?"CartViewport":"ReviewViewport");
        viewport.Margin=new Thickness(18,0,18,0);if(editable)cartScroll=viewport;Place(grid,viewport,1);
        var bottom=new StackPanel { Margin=new Thickness(0,4,0,0) };
        var total=Columns(Star,Auto);total.Margin=new Thickness(0,0,0,8);Place(total,Text("TOTAL BAYAR",10,true,"#C6BFA9"));Place(total,Identify(Text(current.TotalLabel,30,true,"#FCB711"),"CartTotal"),0,1);
        bottom.Children.Add(new Border { Child=total,BorderBrush=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0) });
        if(editable)
        {
            var buttons=Columns(new GridLength(104),new GridLength(8),Star);
            var hold=ActionButton("Simpan dulu",async()=>{await Save(OrderRules.Hold(current,customerName));SetCurrent(Order.New());RenderSelling();},id:"HoldOrder");
            hold.IsEnabled=current.Lines.Length>0;hold.Padding=new Thickness(10);hold.MinHeight=52;hold.Background=Color("#3B3C34");hold.Foreground=Color("#E1DDCF");hold.BorderBrush=Color("#555548");hold.FontSize=14;Place(buttons,hold);
            var pay=ActionButton("Bayar  →",()=>{tendered="";method=PaymentMethod.Cash;RenderPayment();return Task.CompletedTask;},true,"PayOrder");
            pay.IsEnabled=current.Lines.Length>0;pay.Padding=new Thickness(10);pay.MinHeight=52;Place(buttons,pay,0,2);bottom.Children.Add(buttons);
        }
        var settlement=new Border {Child=bottom,Background=Token("RailBrush"),Padding=new Thickness(18,10,18,10),CornerRadius=new CornerRadius(0,0,16,16)};
        Place(grid,settlement,2);
        grid.SizeChanged+=(_,_)=>
        {
            var compact=grid.ActualHeight<480;
            header.Margin=compact?new Thickness(18,8,18,6):new Thickness(18,10,18,8);
            bottom.Margin=new Thickness(0,compact?0:4,0,0);
            total.Margin=new Thickness(0,0,0,compact?4:8);
            ((TextBlock)total.Children[1]).FontSize=compact?26:30;
            settlement.Padding=compact?new Thickness(18,6,18,6):new Thickness(18,10,18,10);
        };
        var slip=Surface(grid,0);slip.Style=Component("OrderSlip");slip.BorderThickness=new Thickness(0);slip.CornerRadius=new CornerRadius(16);
        if(!editable)
        {
            // Match the prototype's dark payment summary without touching order logic.
            slip.Background=Token("RailBrush");((Border)grid.Children[grid.Children.Count-1]).Background=Token("RailBrush");
            ((Border)grid.Children[0]).Background=Token("RailBrush");foreach(var text in LogicalTexts(grid))text.Foreground=Brushes.White;LogicalTexts(grid).First(t=>t.Text==current.TotalLabel).Foreground=Token("Forest");
        }
        return slip;
    }
}
