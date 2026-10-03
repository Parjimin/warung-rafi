using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace WarungRafi.Desktop;

// Wheel easing is independent of native WPF touch panning and inertia.
internal sealed class SmoothScrollViewer : ScrollViewer
{
    private static readonly DependencyProperty AnimatedOffsetProperty=DependencyProperty.Register(
        "AnimatedOffset",typeof(double),typeof(SmoothScrollViewer),new PropertyMetadata(0d,
        (d,e)=>((SmoothScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));
    private double target;
    private bool animating;
    internal SmoothScrollViewer()
    {
        CanContentScroll=false;
        PanningMode=PanningMode.VerticalOnly;
        PanningRatio=1;
        PanningDeceleration=0.0015;
        Unloaded+=(_,_)=>StopMotion();
    }
    internal void StopMotion()
    {
        var offset=VerticalOffset;
        BeginAnimation(AnimatedOffsetProperty,null);
        SetValue(AnimatedOffsetProperty,offset);
        target=offset;animating=false;
    }
    protected override void OnPreviewTouchDown(TouchEventArgs e)
    { StopMotion();base.OnPreviewTouchDown(e); }
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    { StopMotion();base.OnPreviewMouseDown(e); }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    { StopMotion();base.OnPreviewKeyDown(e); }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if(e.Handled)return;
        var lines=SystemParameters.WheelScrollLines;
        if(lines==0)return;
        var distance=lines<0?ViewportHeight:lines*22d;
        var next=Math.Clamp((animating?target:VerticalOffset)-e.Delta/120d*distance,0,ScrollableHeight);
        if(Math.Abs(next-VerticalOffset)<0.5&&!animating)return;
        e.Handled=true;
        if(!SystemParameters.ClientAreaAnimation){StopMotion();ScrollToVerticalOffset(next);return;}
        var from=VerticalOffset;target=next;animating=true;
        var animation=new DoubleAnimation(from,target,TimeSpan.FromMilliseconds(230)) {
            EasingFunction=new CubicEase { EasingMode=EasingMode.EaseOut },FillBehavior=FillBehavior.HoldEnd
        };
        animation.Completed+=(_,_)=>{animating=false;};
        BeginAnimation(AnimatedOffsetProperty,animation,HandoffBehavior.SnapshotAndReplace);
    }
}
