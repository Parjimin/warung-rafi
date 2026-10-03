using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace WarungRafi.Desktop;

public partial class MainWindow
{
    private static Brush Color(string value)=>new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(value));
    private static TextBlock Text(string value,double size=18,bool bold=false,string? color=null)
    {
        var text=new TextBlock { Text=value,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center };
        if(color is not null)text.Foreground=Color(color);return text;
    }
    private static T Identify<T>(T element,string id,string? name=null) where T:DependencyObject
    { AutomationProperties.SetAutomationId(element,id);if(name is not null)AutomationProperties.SetName(element,name);return element; }
    private Button ActionButton(string label,Func<Task> action,bool primary=false,string? id=null)
    {
        var button=new Button { Content=label };Select(button,primary);
        if(id is not null)Identify(button,id,label);
        button.Click+=async(_,_)=>await Run(action);return button;
    }
    private static void Select(Button button,bool active)
    {
        button.Background=Color(active?"#203F36":"#EAF0EC");button.Foreground=Color(active?"#FFFFFF":"#234F3F");
    }
    private static void SelectNavigation(Button button,bool active)
    {
        button.Background=active?GlassBrush():Brushes.Transparent;
        button.Foreground=Color(active?"#203F36":"#D0DED8");
        button.BorderBrush=active?Color("#DFFFFFFF"):Brushes.Transparent;
    }
    private static void SelectTab(Button button,bool active)
    {
        var palette=MenuPalette(button.Tag as string ?? "Nasi");
        button.Background=Color(active?palette.Accent:palette.Tint);
        button.Foreground=Color(active?"#FFFFFF":palette.Accent);
        button.BorderBrush=Color(active?palette.Accent:palette.Line);
        button.FontWeight=FontWeights.SemiBold;
    }
    private static (string Accent,string Tint,string Line) MenuPalette(string category)
        => ("#294B40","#D9FFFFFF","#AFFFFFFF");
    private static Brush GlassBrush(bool selected=false)=>new LinearGradientBrush(
        (System.Windows.Media.Color)ColorConverter.ConvertFromString(selected?"#F5FFFFFF":"#EFFFFFFF"),
        (System.Windows.Media.Color)ColorConverter.ConvertFromString(selected?"#E3E7F0E9":"#BFEFF4F1"),
        new Point(0,0),new Point(1,1));
    private static FrameworkElement MenuSymbol(string category,string stroke)
    {
        var geometry=category switch
        {
            "Minuman" => "M5,4 L19,4 L17,20 L7,20 Z M14,4 L16,0 M19,7 C26,7 24,14 18,14",
            "Sundukan" => "M5,1 L5,23 M12,1 L12,23 M19,1 L19,23 M2,5 L8,5 M2,10 L8,10 M2,15 L8,15 M9,5 L15,5 M9,10 L15,10 M9,15 L15,15 M16,5 L22,5 M16,10 L22,10 M16,15 L22,15",
            _ => "M2,11 L22,11 C21,23 3,23 2,11 Z M7,23 L17,23 M7,7 C4,4 10,3 7,0 M14,7 C11,4 17,3 14,0"
        };
        return new System.Windows.Shapes.Path { Data=Geometry.Parse(geometry),Stroke=Color(stroke),StrokeThickness=1.5,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Stretch=Stretch.Uniform,Width=18,Height=18,Margin=new Thickness(0,0,7,0) };
    }
    private static Grid InputWithHint(TextBox input,string hint)
    {
        var grid=new Grid();grid.Children.Add(input);
        var watermark=Text(hint,input.FontSize,false,"#849083");watermark.IsHitTestVisible=false;
        watermark.Margin=new Thickness(13,0,10,0);watermark.TextWrapping=TextWrapping.NoWrap;
        grid.Children.Add(watermark);
        void Update()=>watermark.Visibility=string.IsNullOrEmpty(input.Text)?Visibility.Visible:Visibility.Collapsed;
        input.TextChanged+=(_,_)=>Update();Update();return grid;
    }
    private static ScrollViewer Scroll(UIElement child,string? id=null)
    {
        var scroll=new ScrollViewer { Content=child,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,PanningMode=PanningMode.VerticalOnly };
        if(id is not null)Identify(scroll,id);return scroll;
    }
    private static Border Surface(UIElement child,int padding=18)=>new()
    { Child=child,Background=GlassBrush(),BorderBrush=Color("#EFFFFFFF"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(22),Padding=new Thickness(padding),
      Effect=new System.Windows.Media.Effects.DropShadowEffect { Color=System.Windows.Media.Color.FromRgb(37,62,51),BlurRadius=18,ShadowDepth=4,Opacity=0.09 } };
    private static Grid Rows(params GridLength[] heights)
    {
        var grid=new Grid();foreach(var height in heights)grid.RowDefinitions.Add(new RowDefinition{Height=height});return grid;
    }
    private static void Place(Grid grid,UIElement element,int row=0,int column=0)
    { Grid.SetRow(element,row);Grid.SetColumn(element,column);grid.Children.Add(element); }
    private static Grid Columns(params GridLength[] widths)
    {
        var grid=new Grid();foreach(var width in widths)grid.ColumnDefinitions.Add(new ColumnDefinition{Width=width});return grid;
    }
    private static readonly GridLength Auto=GridLength.Auto;
    private static readonly GridLength Star=new(1,GridUnitType.Star);
    private static void Reveal(UIElement element)
    {
        if(!SystemParameters.ClientAreaAnimation)return;
        element.BeginAnimation(OpacityProperty,new DoubleAnimation(0.55,1,TimeSpan.FromMilliseconds(130)) { FillBehavior=FillBehavior.Stop });
    }
    private void Present(string next,FrameworkElement view)
    {
        var changed=page!=next||MainContent.Content is null;page=next;MainContent.Content=view;
        SelectNavigation(SellNav,next is "sell" or "payment" or "success");SelectNavigation(HeldNav,next=="held");SelectNavigation(HistoryNav,next=="history");SelectNavigation(CashNav,next=="cash");
        if(changed)Reveal(view);
    }
    private static FrameworkElement Empty(string title,string description)
    {
        var stack=new StackPanel { VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,MaxWidth=300,Margin=new Thickness(16) };
        var mark=Text("+",32,true,"#234F3F");mark.HorizontalAlignment=HorizontalAlignment.Center;
        stack.Children.Add(new Border { Child=mark,Background=Color("#F0F3EC"),CornerRadius=new CornerRadius(22),Width=56,Height=56,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,12) });
        var heading=Text(title,21,true);heading.TextAlignment=TextAlignment.Center;stack.Children.Add(heading);
        var subtitle=Text(description,16,false,"#65766E");subtitle.TextAlignment=TextAlignment.Center;subtitle.Margin=new Thickness(0,8,0,0);stack.Children.Add(subtitle);return stack;
    }
    // Repo-native illustrations: no image downloads in the critical click/render path.
    private static FrameworkElement Illustration(string category,string? productId=null)
    {
        var canvas=new Canvas { Width=130,Height=76 };
        var cream=Color("#FFFDF7");var dark=Color("#47765D");var gold=Color("#C28C50");
        void Ellipse(double x,double y,double w,double h,Brush fill)
        {var shape=new Ellipse { Width=w,Height=h,Fill=fill };Canvas.SetLeft(shape,x);Canvas.SetTop(shape,y);canvas.Children.Add(shape);}
        void Path(string geometry,Brush fill)
        {canvas.Children.Add(new System.Windows.Shapes.Path { Data=Geometry.Parse(geometry),Fill=fill });}
        if(category=="Minuman")
        {
            Path("M43,14 L88,14 L82,66 Q66,76 49,66 Z",dark);Ellipse(43,8,45,13,cream);Ellipse(48,12,35,7,gold);
            canvas.Children.Add(new System.Windows.Shapes.Path { Data=Geometry.Parse("M89,23 C115,22 109,53 85,49"),Stroke=dark,StrokeThickness=7 });
            Path("M58,0 L61,0 L65,9 L62,9 Z M75,0 L78,0 L80,7 L77,7 Z",Color("#9CB59B"));
        }
        else if(category=="Sundukan")
        {
            for(var i=0;i<3;i++)
            {
                var x=36+i*26;Path($"M{x},4 L{x+3},4 L{x+3},74 L{x},74 Z",gold);
                for(var j=0;j<3;j++)Ellipse(x-7,10+j*17,18,14,i==1?gold:dark);
            }
        }
        else
        {
            Ellipse(20,54,90,14,Color("#CFDCCD"));Ellipse(13,13,104,53,cream);Ellipse(22,20,86,38,Color("#E4EADD"));
            if(category=="Nasi")
            {
                Path("M30,43 C30,12 72,10 76,43 Q53,56 30,43 Z",cream);
                Path("M40,30 l5,-4 2,3 -5,4 Z M57,27 l4,-2 2,3 -4,2 Z M48,41 l4,-3 2,3 -4,3 Z",Color("#D7C6A4"));
                if(productId=="NAS-002")
                {
                    Path("M69,32 Q86,12 105,34 Q85,51 69,32 Z M70,32 L59,21 L60,42 Z",Color("#9A603D"));
                    Path("M82,25 L77,38 M89,24 L83,41 M95,26 L90,39",gold);
                    Ellipse(97,29,3,3,dark);
                }
                else if(productId=="NAS-003")
                { Ellipse(68,22,34,29,Brushes.White);Ellipse(77,29,16,15,Color("#E5AE45")); }
                else if(productId=="NAS-004")
                {
                    Ellipse(72,23,30,26,Color("#AD6A35"));Path("M78,39 L66,54 L61,50 L70,35 Z",gold);
                    Ellipse(59,47,10,8,cream);Ellipse(76,27,8,4,Color("#D5A45C"));
                }
                if(productId!="NAS-001"){Ellipse(77,48,22,5,dark);Ellipse(85,49,12,5,Color("#698B59"));}
            }
            else {Ellipse(33,29,28,20,gold);Ellipse(63,27,30,22,dark);Ellipse(54,40,24,13,gold);}
        }
        return new Viewbox { Child=canvas,Stretch=Stretch.Uniform,Height=76,HorizontalAlignment=HorizontalAlignment.Center };
    }
}
