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
    readonly TextBlock heading=new() { FontWeight=FontWeights.Bold, FontSize=18, TextWrapping=TextWrapping.Wrap, VerticalAlignment=VerticalAlignment.Center };
    readonly Button close=new() { HorizontalAlignment=HorizontalAlignment.Right };
    readonly TextBlock originalLabel=new(), translationLabel=new(), explanationLabel=new();
    string uiLanguage="zh-TW", targetLanguage="zh-TW", statusKey="Preparing";
    object[] statusArgs=[];
    Exception? shownError;
    internal event Action? Dismissed;
    internal Popup()
    {
        Title="翻譯與白話解釋"; Width=500; Height=560; MinWidth=320; MinHeight=220; Topmost=true; ShowInTaskbar=false; ShowActivated=false;
        FontFamily=new FontFamily("Segoe UI, Microsoft JhengHei UI"); Background=new SolidColorBrush(Color.FromRgb(250,252,255));
        var grid=new Grid { Margin=new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        var header=new DockPanel();
        close.Click+=(_,_)=>Dismiss(); DockPanel.SetDock(close,Dock.Right); header.Children.Add(close);
        header.Children.Add(heading);
        var sections=new StackPanel { Margin=new Thickness(0,8,10,0) };
        void AddSection(TextBlock label,TextBox text,bool first=false)
        {
            var content=new StackPanel();
            label.FontWeight=FontWeights.SemiBold; label.FontSize=15; label.Foreground=Brushes.SlateGray; label.TextWrapping=TextWrapping.Wrap;
            content.Children.Add(label);
            content.Children.Add(text);
            sections.Children.Add(new Border { Child=content, Margin=new Thickness(0,first?0:24,0,0), Padding=new Thickness(0,first?0:20,0,0), BorderThickness=new Thickness(0,first?0:1,0,0), BorderBrush=new SolidColorBrush(Color.FromRgb(222,228,237)) });
        }
        AddSection(originalLabel,original,true);
        AddSection(translationLabel,translation);
        AddSection(explanationLabel,explanation);
        scroll.Content=sections;
        grid.Children.Add(header); Grid.SetRow(state,1); grid.Children.Add(state); Grid.SetRow(scroll,2); grid.Children.Add(scroll); Content=grid;
        PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape) Dismiss(); };
        Closing+=(_,e)=> { if(!App.IsExiting) { e.Cancel=true; Dismiss(); } };
        SetLanguage(uiLanguage);
    }
    void Dismiss() { Hide(); Dismissed?.Invoke(); }
    internal void SetLanguage(string language)
    {
        uiLanguage=Languages.Ui(language).Id;
        Language=System.Windows.Markup.XmlLanguage.GetLanguage(uiLanguage);
        Title=heading.Text=I18n.Text(uiLanguage,"PopupTitle"); close.Content=I18n.Text(uiLanguage,"Close");
        originalLabel.Text=I18n.Text(uiLanguage,"Original");
        translationLabel.Text=I18n.Text(uiLanguage,"Translation",Languages.Target(targetLanguage).Name);
        explanationLabel.Text=I18n.Text(uiLanguage,"Explanation");
        Status(statusKey,statusArgs);
        if(shownError!=null) translation.Text=I18n.Error(uiLanguage,shownError);
    }
    internal void Begin(Native.Point point,string source,string originalText="",string target="zh-TW")
    {
        original.Text=originalText; translation.Text=""; explanation.Text=""; shownError=null;
        targetLanguage=Languages.Target(target).Id; Status(source); SetLanguage(uiLanguage);
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
        (translation.Text,explanation.Text)=TranslationSections.Parse(text);
    }
    internal void ShowError(Exception error) { shownError=error; translation.Text=I18n.Error(uiLanguage,error); explanation.Text=""; }
    internal void Status(string key,params object[] args) { statusKey=key; statusArgs=args; state.Text=I18n.Text(uiLanguage,key,args); }
}
