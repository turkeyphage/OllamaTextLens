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
    readonly SpeechService speech=new(new KokoroVoice(),rate=>new SpeechOutput(rate));
    readonly System.Windows.Threading.DispatcherTimer modelTimer=new() { Interval=TimeSpan.FromSeconds(2) };
    readonly Button speak=new(), pauseSpeech=new(), stopSpeech=new();
    readonly ComboBox speechRate=new() { Width=78,Padding=new Thickness(5,3,5,3),Margin=new Thickness(4,0,0,0) };
    readonly ComboBox speechVoice=new() { Width=178,DisplayMemberPath="Name",SelectedValuePath="Id",Padding=new Thickness(5,3,5,3),Margin=new Thickness(4,0,10,0) };
    readonly TextBlock speechVoiceLabel=new() { VerticalAlignment=VerticalAlignment.Center };
    readonly TextBlock speechRateLabel=new() { VerticalAlignment=VerticalAlignment.Center };
    readonly TextBlock speechState=new() { FontSize=12,Foreground=Brushes.SlateGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0) };
    string uiLanguage="zh-TW", targetLanguage="zh-TW", statusKey="Preparing";
    object[] statusArgs=[];
    Exception? shownError;
    internal event Action? Dismissed;
    internal Popup()
    {
        Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/window.ico"));
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
            if(first)
            {
                var controls=new WrapPanel { VerticalAlignment=VerticalAlignment.Center };
                label.Margin=new Thickness(0,0,10,6); label.VerticalAlignment=VerticalAlignment.Center;
                controls.Children.Add(label);
                foreach(var button in new[]{speak,pauseSpeech,stopSpeech})
                { button.Padding=new Thickness(8,4,8,4); button.Margin=new Thickness(0,0,6,6); controls.Children.Add(button); }
                var voiceControls=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,6) };
                voiceControls.Children.Add(speechVoiceLabel); voiceControls.Children.Add(speechVoice);
                controls.Children.Add(voiceControls);
                var speedControls=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,6) };
                speedControls.Children.Add(speechRateLabel); speedControls.Children.Add(speechRate);
                controls.Children.Add(speedControls); content.Children.Add(controls); content.Children.Add(speechState);
            }
            else content.Children.Add(label);
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
        Closed+=(_,_)=> { modelTimer.Stop(); speech.Dispose(); };
        modelTimer.Tick+=(_,_)=>RenderSpeech();
        IsVisibleChanged+=(_,_)=> { if(IsVisible) { modelTimer.Start(); RenderSpeech(); } else modelTimer.Stop(); };
        speechRate.ItemsSource=new[]{"0.75×","1.0×","1.25×","1.5×","2.0×"}; speechRate.SelectedIndex=1;
        speak.Click+=async(_,_)=>await speech.PlayAsync(original.Text,new[]{0.75f,1f,1.25f,1.5f,2f}[speechRate.SelectedIndex],speechVoice.SelectedValue is int voice?voice:3);
        pauseSpeech.Click+=(_,_)=>speech.TogglePause(); stopSpeech.Click+=(_,_)=>speech.Stop();
        speech.Changed+=()=> {
            if(App.IsExiting || Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(RenderSpeech));
        };
        SetLanguage(uiLanguage);
    }
    void Dismiss() { speech.Stop(); Hide(); Dismissed?.Invoke(); }
    void RenderSpeech()
    {
        var value=speech.Status;
        bool modelReady=SpeechModel.IsReady(SpeechModel.DirectoryPath);
        bool active=value.Phase is SpeechPhase.Downloading or SpeechPhase.Preparing or SpeechPhase.Playing or SpeechPhase.Paused;
        if(!modelReady && active) { speech.Stop(); value=speech.Status; active=false; }
        speak.Content=I18n.Text(uiLanguage,"SpeechPlay"); speak.IsEnabled=modelReady && !active && !string.IsNullOrWhiteSpace(original.Text);
        speak.ToolTip=modelReady?null:I18n.Text(uiLanguage,"SpeechModelMissing");
        pauseSpeech.Content=I18n.Text(uiLanguage,value.Phase==SpeechPhase.Paused?"SpeechResume":"SpeechPause");
        pauseSpeech.IsEnabled=value.Phase is SpeechPhase.Playing or SpeechPhase.Paused;
        stopSpeech.Content=I18n.Text(uiLanguage,"SpeechStop"); stopSpeech.IsEnabled=active;
        speechRateLabel.Text=I18n.Text(uiLanguage,"SpeechRate"); speechRate.IsEnabled=modelReady && !active;
        speechVoiceLabel.Text=I18n.Text(uiLanguage,"SpeechVoice"); speechVoice.IsEnabled=modelReady && !active;
        speechVoice.ToolTip=I18n.Text(uiLanguage,"SpeechVoiceHint");
        speechRate.ToolTip=I18n.Text(uiLanguage,"SpeechRateHint");
        speechState.Text=!modelReady?I18n.Text(uiLanguage,"SpeechModelMissing"):
            value.Phase==SpeechPhase.Downloading?I18n.Text(uiLanguage,"SpeechDownloading",value.Progress):
            value.Phase==SpeechPhase.Failed?I18n.Text(uiLanguage,"SpeechFailed")+" "+(value.Error is AppError?I18n.Error(uiLanguage,value.Error):I18n.Text(uiLanguage,"SpeechRetry")):
            I18n.Text(uiLanguage,"Speech"+value.Phase);
    }
    internal void SetLanguage(string language)
    {
        uiLanguage=Languages.Ui(language).Id;
        var selectedVoice=speechVoice.SelectedValue is int voice?voice:3;
        speechVoice.ItemsSource=new[]{new SpeechVoiceChoice(3,I18n.Text(uiLanguage,"SpeechFemaleVoice")),new SpeechVoiceChoice(16,I18n.Text(uiLanguage,"SpeechMaleVoice"))};
        speechVoice.SelectedValue=selectedVoice;
        Language=System.Windows.Markup.XmlLanguage.GetLanguage(uiLanguage);
        Title=heading.Text=I18n.Text(uiLanguage,"PopupTitle"); close.Content=I18n.Text(uiLanguage,"Close");
        originalLabel.Text=I18n.Text(uiLanguage,"Original");
        translationLabel.Text=I18n.Text(uiLanguage,"Translation",Languages.Target(targetLanguage).Name);
        explanationLabel.Text=I18n.Text(uiLanguage,"Explanation");
        Status(statusKey,statusArgs);
        if(shownError!=null) translation.Text=I18n.Error(uiLanguage,shownError);
        RenderSpeech();
    }
    internal void Begin(Native.Point point,string source,string originalText="",string target="zh-TW")
    {
        speech.Stop();
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
    sealed record SpeechVoiceChoice(int Id,string Name);
}
