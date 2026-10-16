using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.InteropServices;
namespace OllamaTextLens;
internal sealed class Popup : Window
{
    static TextBox TextArea() => new() { IsReadOnly=true, TextWrapping=TextWrapping.Wrap, BorderThickness=new Thickness(0), Background=Brushes.Transparent, FontSize=15, Padding=new Thickness(0), Margin=new Thickness(0,8,0,0) };
    readonly TextBox original=TextArea();
    readonly TextBox translation=TextArea();
    readonly TextBox explanation=TextArea();
    readonly ScrollViewer scroll=new() { VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
    readonly TextBlock state=new() { Foreground=Brushes.SlateGray, Margin=new Thickness(0,8,0,8) };
    internal event Action? Dismissed;
    internal Popup()
    {
        Title="翻譯與白話解釋"; Width=500; Height=560; MinWidth=320; MinHeight=220; Topmost=true; ShowInTaskbar=false; ShowActivated=false;
        FontFamily=new FontFamily("Microsoft JhengHei UI"); Background=new SolidColorBrush(Color.FromRgb(250,252,255));
        var grid=new Grid { Margin=new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        var header=new DockPanel();
        var close=new Button { Content="關閉 · Esc", HorizontalAlignment=HorizontalAlignment.Right }; close.Click+=(_,_)=>Dismiss(); DockPanel.SetDock(close,Dock.Right); header.Children.Add(close);
        header.Children.Add(new TextBlock { Text="翻譯與白話解釋", FontWeight=FontWeights.Bold,FontSize=18,VerticalAlignment=VerticalAlignment.Center });
        var sections=new StackPanel { Margin=new Thickness(0,8,10,0) };
        void AddSection(string title,TextBox text,bool first=false)
        {
            var content=new StackPanel();
            content.Children.Add(new TextBlock { Text=title, FontWeight=FontWeights.SemiBold,FontSize=15,Foreground=Brushes.SlateGray });
            content.Children.Add(text);
            sections.Children.Add(new Border { Child=content, Margin=new Thickness(0,first?0:24,0,0), Padding=new Thickness(0,first?0:20,0,0), BorderThickness=new Thickness(0,first?0:1,0,0), BorderBrush=new SolidColorBrush(Color.FromRgb(222,228,237)) });
        }
        AddSection("【原文】",original,true);
        AddSection("【繁體中文翻譯】",translation);
        AddSection("【白話解釋】",explanation);
        scroll.Content=sections;
        grid.Children.Add(header); Grid.SetRow(state,1); grid.Children.Add(state); Grid.SetRow(scroll,2); grid.Children.Add(scroll); Content=grid;
        PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape) Dismiss(); };
        Closing+=(_,e)=> { e.Cancel=true; Dismiss(); };
    }
    void Dismiss() { Hide(); Dismissed?.Invoke(); }
    internal void Begin(Native.Point point,string source,string originalText="")
    {
        original.Text=originalText; translation.Text=""; explanation.Text=""; state.Text=source;
        scroll.ScrollToTop();
        Show();
        var transform=PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var pos=transform.Transform(new System.Windows.Point(point.X,point.Y));
        var info=new Native.MonitorInfo { Size=Marshal.SizeOf<Native.MonitorInfo>() };
        if (Native.GetMonitorInfo(Native.MonitorFromPoint(point,2),ref info))
        {
            var a=transform.Transform(new System.Windows.Point(info.Work.Left,info.Work.Top)); var b=transform.Transform(new System.Windows.Point(info.Work.Right,info.Work.Bottom));
            Width=Math.Min(500,b.X-a.X); Height=Math.Min(560,b.Y-a.Y);
            Left=Math.Clamp(pos.X+16,a.X,Math.Max(a.X,b.X-Width)); Top=Math.Clamp(pos.Y+20,a.Y,Math.Max(a.Y,b.Y-Height));
        }
        else { Left=pos.X+16; Top=pos.Y+20; }
    }
    internal void Update(string text)
    {
        const string translationHeader="【繁體中文翻譯】", explanationHeader="【白話解釋】";
        int start=text.IndexOf(translationHeader,StringComparison.Ordinal);
        if(start>=0) text=text[(start+translationHeader.Length)..];
        int split=text.IndexOf(explanationHeader,StringComparison.Ordinal);
        string Clean(string value) => value.Trim().Trim('*','#').Trim();
        translation.Text=Clean(split<0?text:text[..split]);
        explanation.Text=split<0?"":Clean(text[(split+explanationHeader.Length)..]);
    }
    internal void Status(string text) => state.Text=text;
}
