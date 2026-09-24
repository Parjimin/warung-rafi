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
        button.Background=Color(active?"#205C49":"#EDF3ED");button.Foreground=Color(active?"#FFFFFF":"#205C49");
    }
    private static ScrollViewer Scroll(UIElement child,string? id=null)
    {
        var scroll=new ScrollViewer { Content=child,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,PanningMode=PanningMode.VerticalOnly };
        if(id is not null)Identify(scroll,id);return scroll;
    }
    private static Border Surface(UIElement child,int padding=18)=>new()
    { Child=child,Background=Brushes.White,BorderBrush=Color("#DEE6DF"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),Padding=new Thickness(padding) };
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
        Select(SellNav,next is "sell" or "payment" or "success");Select(HeldNav,next=="held");Select(HistoryNav,next=="history");Select(CashNav,next=="cash");
        if(changed)Reveal(view);
    }
    private static FrameworkElement Empty(string title,string description)
    {
        var stack=new StackPanel { VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,MaxWidth=300,Margin=new Thickness(16) };
        var mark=Text("+",32,true,"#205C49");mark.HorizontalAlignment=HorizontalAlignment.Center;
        stack.Children.Add(new Border { Child=mark,Background=Color("#EDF3ED"),CornerRadius=new CornerRadius(22),Width=56,Height=56,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,12) });
        var heading=Text(title,21,true);heading.TextAlignment=TextAlignment.Center;stack.Children.Add(heading);
        var subtitle=Text(description,16,false,"#65766E");subtitle.TextAlignment=TextAlignment.Center;subtitle.Margin=new Thickness(0,8,0,0);stack.Children.Add(subtitle);return stack;
    }
    // Repo-native illustrations: no image downloads in the critical click/render path.
    private static FrameworkElement Illustration(string category)
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
                Path("M35,43 C35,14 78,12 82,43 Q59,55 35,43 Z",cream);
                Path("M49,29 l5,-4 2,3 -5,4 Z M66,28 l4,-2 2,3 -4,2 Z M53,39 l4,-3 2,3 -4,3 Z",gold);
                Ellipse(82,29,18,13,dark);Ellipse(78,40,20,9,gold);
            }
            else {Ellipse(33,29,28,20,gold);Ellipse(63,27,30,22,dark);Ellipse(54,40,24,13,gold);}
        }
        return new Viewbox { Child=canvas,Stretch=Stretch.Uniform,Height=76,HorizontalAlignment=HorizontalAlignment.Center };
    }
}
