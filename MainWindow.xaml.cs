using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
namespace OllamaTextLens;
public partial class MainWindow : Window
{
    readonly Popup popup=new();
    readonly TrayIcon tray;
    readonly System.Windows.Threading.DispatcherTimer healthTimer=new() { Interval=TimeSpan.FromSeconds(5) };
    CancellationTokenSource? healthRequest;
    CancellationTokenSource? speechInstall;
    int speechDownloadProgress;
    Exception? speechInstallError;
    string connectionKey="OllamaChecking";
    string[]? runningModels;
    CancellationTokenSource? pending;
    bool capturing;
    HwndSource? source;
    readonly string settingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OllamaTextLens","settings.json");
    bool languagesReady;
    bool hotkeyRegistered;
    string uiLanguage="zh-TW";
    string targetLanguage="zh-TW";
    string statusKey="Ready";
    string statusDetail="";
    public MainWindow()
    {
        InitializeComponent();
        Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/window.ico"));
        // Fit content by default, but keep the entire window within the desktop work area.
        var workArea=SystemParameters.WorkArea;
        MinWidth=Math.Min(MinWidth,Math.Max(1,workArea.Width-24));
        MinHeight=Math.Min(MinHeight,Math.Max(1,workArea.Height-24));
        MaxWidth=Math.Max(MinWidth,workArea.Width-24);
        MaxHeight=Math.Max(MinHeight,workArea.Height-24);
        Width=Math.Min(Width,MaxWidth);
        try
        {
            var loadPath=File.Exists(settingsPath)?settingsPath:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalPdfTranslator","settings.json");
            if(File.Exists(loadPath))
            {
                var settings=JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(loadPath));
                if(settings!=null)
                {
                    Endpoint.Text=settings.Endpoint; Models.Text=settings.Model;
                    uiLanguage=Languages.Ui(settings.UiLanguage).Id;
                    targetLanguage=Languages.Target(settings.TargetLanguage).Id;
                }
            }
        }
        catch { statusKey="SettingsReadFailed"; }
        InterfaceLanguage.ItemsSource=Languages.Interface;
        TargetLanguage.ItemsSource=Languages.Targets;
        InterfaceLanguage.SelectedValue=uiLanguage;
        TargetLanguage.SelectedValue=targetLanguage;
        tray=new TrayIcon(ShowSettings,()=> { App.IsExiting=true; Application.Current.Shutdown(); });
        languagesReady=true;
        ApplyLanguage();
        healthTimer.Tick+=async(_,_)=> { RefreshSpeechAvailability(); await CheckConnection(); };
        Endpoint.TextChanged+=async(_,_)=> { if(languagesReady) { CancelHealthCheck(); if(IsVisible) await CheckConnection(); } };
        Models.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,new System.Windows.Controls.TextChangedEventHandler((_,_)=>RenderConnection()));
        IsVisibleChanged+=async(_,_)=> {
            if(IsVisible) { healthTimer.Start(); RefreshSpeechAvailability(); await CheckConnection(); }
            else { healthTimer.Stop(); CancelHealthCheck(); }
        };
        popup.Dismissed+=()=>pending?.Cancel();
        SourceInitialized+=(_,_)=> {
            source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); source.AddHook(Hook);
            hotkeyRegistered=Native.RegisterHotKey(source.Handle,1,0x4003,0x54);
            if(!hotkeyRegistered) SetStatus("HotkeyBusy");
        };
        Loaded+=async(_,_)=>await Refresh();
        Closing+=(_,e)=> {
            if(App.IsExiting) return;
            e.Cancel=true; Hide();
        };
        Closed+=(_,_)=> { speechInstall?.Cancel(); healthTimer.Stop(); CancelHealthCheck(); tray.Dispose(); pending?.Cancel(); if(source!=null) { Native.UnregisterHotKey(source.Handle,1); source.RemoveHook(Hook); } };
    }
    void ShowSettings()
    {
        Show();
        if(WindowState==WindowState.Minimized) WindowState=WindowState.Normal;
        Activate();
    }
    void ApplyLanguage()
    {
        string T(string key)=>I18n.Text(uiLanguage,key);
        HelpLabel.Text=T("Help"); InterfaceLabel.Text=T("Interface"); TargetLabel.Text=T("Target");
        EndpointLabel.Text=T("Endpoint"); ModelLabel.Text=T("Model"); PrivacyLabel.Text=T("Privacy");
        RefreshButton.Content=T("Refresh"); SaveButton.Content=T("Save"); TestButton.Content=T("Test");
        SpeechTestButton.Content=T("SpeechTest");
        SpeechSpanishTestButton.Content=T("SpeechTestSpanish");
        SpeechJapaneseTestButton.Content=T("SpeechTestJapanese");
        RefreshSpeechAvailability();
        Status.Text=T(statusKey)+(statusDetail.Length==0?"":" "+statusDetail);
        popup.SetLanguage(uiLanguage);
        tray.SetLanguage(uiLanguage);
        RenderConnection();
        Language=System.Windows.Markup.XmlLanguage.GetLanguage(uiLanguage);
    }
    void CancelHealthCheck()
    {
        healthRequest?.Cancel();
        healthRequest=null;
        runningModels=null;
        connectionKey="OllamaChecking";
        RenderConnection();
    }
    void RenderConnection()
    {
        ConnectionStatus.Text=I18n.Text(uiLanguage,connectionKey);
        ConnectionStatus.Foreground=connectionKey=="OllamaConnected"?System.Windows.Media.Brushes.ForestGreen:
            connectionKey=="OllamaChecking"?System.Windows.Media.Brushes.DarkGoldenrod:System.Windows.Media.Brushes.Firebrick;
        var loaded=runningModels==null?I18n.Text(uiLanguage,"OllamaUnknown"):
            runningModels.Length==0?I18n.Text(uiLanguage,"OllamaIdle"):string.Join(", ",runningModels);
        RunningModelStatus.Text=I18n.Text(uiLanguage,"OllamaRunningModels",loaded);
        SelectedModelStatus.Text=I18n.Text(uiLanguage,"OllamaSelectedModel",string.IsNullOrWhiteSpace(Models.Text)?I18n.Text(uiLanguage,"OllamaNotSelected"):Models.Text.Trim());
    }
    async Task CheckConnection()
    {
        if(healthRequest!=null || App.IsExiting || !IsVisible) return;
        var current=new CancellationTokenSource();
        healthRequest=current;
        var address=Endpoint.Text;
        try
        {
            var endpoint=OllamaClient.Validate(address);
            var loaded=await OllamaClient.RunningModels(endpoint,current.Token);
            if(healthRequest!=current || Endpoint.Text!=address) return;
            runningModels=loaded; connectionKey="OllamaConnected";
        }
        catch(OperationCanceledException) when(current.IsCancellationRequested) { return; }
        catch(AppError e) when(e.Key=="InvalidEndpoint") { if(healthRequest==current) { runningModels=null; connectionKey="InvalidEndpoint"; } }
        catch(Exception) { if(healthRequest==current) { runningModels=null; connectionKey="OllamaDisconnected"; } }
        finally
        {
            if(healthRequest==current) { healthRequest=null; RenderConnection(); }
            current.Dispose();
        }
    }
    void SetStatus(string key,string detail="") { statusKey=key; statusDetail=detail; Status.Text=I18n.Text(uiLanguage,key)+(detail.Length==0?"":" "+detail); }
    void Language_Changed(object sender,System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if(!languagesReady) return;
        uiLanguage=Languages.Ui(InterfaceLanguage.SelectedValue as string).Id;
        targetLanguage=Languages.Target(TargetLanguage.SelectedValue as string).Id;
        ApplyLanguage();
    }
    IntPtr Hook(IntPtr hwnd,int msg,IntPtr w,IntPtr l,ref bool handled)
    {
        if(msg==0x0312 && w.ToInt32()==1) { handled=true; _=Capture(); }
        return IntPtr.Zero;
    }
    async Task Capture()
    {
        if(capturing) return;
        capturing=true;
        pending?.Cancel();
        Native.GetCursorPos(out var point);
        var foreground=Native.GetForegroundWindow();
        try
        {
            // Do not activate the popup until copying has finished.
            for(int i=0;i<150 && new[]{0x11,0x12,0x54}.Any(k=>Native.GetAsyncKeyState(k)<0);i++) await Task.Delay(10);
            if(new[]{0x11,0x12,0x54}.Any(k=>Native.GetAsyncKeyState(k)<0)) throw new AppError("ReleaseKeys");
            if(Native.GetForegroundWindow()!=foreground) throw new AppError("WindowChanged");
            var before=Native.GetClipboardSequenceNumber();
            var original=Clipboard.GetDataObject();
            string? text=null;
            uint copied=before;
            try
            {
                Native.Copy();
                for(int i=0;i<60;i++)
                {
                    await Task.Delay(20);
                    copied=Native.GetClipboardSequenceNumber();
                    if(copied!=before) { if(Clipboard.ContainsText()) text=Clipboard.GetText(); break; }
                }
            }
            finally
            {
                // Do not overwrite a newer clipboard change made by another application.
                if(copied!=before && Native.GetClipboardSequenceNumber()==copied)
                {
                    if(original!=null) Clipboard.SetDataObject(original,true); else Clipboard.Clear();
                }
            }
            if(string.IsNullOrWhiteSpace(text)) throw new AppError("NoText");
            if(text.Length>12000) throw new AppError("TextTooLong");
            text=Regex.Replace(text,@"(?<=\p{L})-\r?\n(?=\p{Ll})","");
            text=Regex.Replace(text,@"(?<!\n)\r?\n(?!\r?\n)"," ").Trim();
            if(!App.IsExiting) _=Translate(text,point);
        }
        catch(Exception ex) { if(!App.IsExiting) { popup.Begin(point,"CaptureFailed",target:targetLanguage); popup.ShowError(ex); } }
        finally { capturing=false; }
    }
    async Task Translate(string text,Native.Point point)
    {
        pending?.Cancel();
        var current=new CancellationTokenSource(); pending=current;
        // Snapshot the target so changing settings cannot relabel an in-flight response.
        var requestTarget=targetLanguage;
        popup.Begin(point,"Preparing",text,requestTarget);
        try
        {
            var endpoint=OllamaClient.Validate(Endpoint.Text);
            var model=Models.Text.Trim();
            if(model.Length==0) throw new AppError("ChooseModel");
            var watch=System.Diagnostics.Stopwatch.StartNew();
            await OllamaClient.Translate(endpoint,model,text,requestTarget,result=> { if(pending==current) { popup.Status("Translating"); popup.Update(result); } },current.Token);
            if(pending==current) popup.Status("Done",watch.Elapsed.TotalSeconds,model);
        }
        catch(OperationCanceledException) { if(pending==current) popup.Status("Cancelled"); }
        catch(Exception ex) { if(pending==current) { popup.Status("TranslationFailed"); popup.ShowError(ex); } }
        finally { if(pending==current) pending=null; current.Dispose(); }
    }
    async Task Refresh()
    {
        CancelHealthCheck();
        _=CheckConnection();
        try { var names=await OllamaClient.Models(OllamaClient.Validate(Endpoint.Text)); var selected=Models.Text; Models.ItemsSource=names; Models.Text=names.Contains(selected)?selected:names.FirstOrDefault()??""; SetStatus(!hotkeyRegistered?"HotkeyBusy":names.Length==0?"NoModels":"ModelsLoaded"); }
        catch(Exception ex) { SetStatus("ModelsFailed",I18n.Error(uiLanguage,ex)); }
    }
    async void Refresh_Click(object sender,RoutedEventArgs e)=>await Refresh();
    void Save_Click(object sender,RoutedEventArgs e)
    {
        try { OllamaClient.Validate(Endpoint.Text); Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!); File.WriteAllText(settingsPath,JsonSerializer.Serialize(new UserSettings(Endpoint.Text,Models.Text,uiLanguage,targetLanguage))); SetStatus("Saved"); } catch(Exception ex) { SetStatus("SettingsFailed",I18n.Error(uiLanguage,ex)); }
    }
    async void Test_Click(object sender,RoutedEventArgs e) { Native.GetCursorPos(out var point); await Translate("The results suggest a strong correlation, but correlation does not imply causation.",point); }
    void SpeechTest_Click(object sender,RoutedEventArgs e)
    {
        RefreshSpeechAvailability();
        if(!SpeechTestButton.IsEnabled) return;
        if(sender==SpeechJapaneseTestButton && !SpeechJapaneseTestButton.IsEnabled) return;
        pending?.Cancel(); pending=null;
        Native.GetCursorPos(out var point);
        var sample=sender==SpeechJapaneseTestButton?
            "こんにちは。これは日本語の読み上げテストです。文章を選択すると、自然な声で原文を聞くことができます。途中で一時停止したり、読み上げの速さを変えたりできます。":sender==SpeechSpanishTestButton?
            "Hola, esta es una prueba de lectura en español. Selecciona un fragmento de texto y escucha su pronunciación natural, sin enviar el contenido a un servicio de pago.":
            "Welcome to OllamaTextLens. Select a passage and listen to the original English text, spoken naturally on your own computer. You can pause, continue, stop, or choose a different reading speed.";
        popup.Begin(point,"SpeechDemo",sample,targetLanguage);
    }
    void RefreshSpeechAvailability()
    {
        var state=SpeechModel.Inspect(SpeechModel.DirectoryPath);
        bool ready=state==SpeechModelState.Ready;
        SpeechTestButton.IsEnabled=ready && speechInstall==null;
        SpeechSpanishTestButton.IsEnabled=SpeechTestButton.IsEnabled;
        SpeechJapaneseTestButton.IsEnabled=SpeechTestButton.IsEnabled && JapaneseSpeech.IsAvailable;
        SpeechJapaneseTestButton.ToolTip=JapaneseSpeech.IsAvailable?null:I18n.Text(uiLanguage,"SpeechJapaneseMissing");
        InstallSpeechButton.IsEnabled=!ready || speechInstall!=null;
        InstallSpeechButton.Content=I18n.Text(uiLanguage,speechInstall!=null?"SpeechCancelDownload":"SpeechInstall");
        string key=ready?"SpeechModelInstalled":state==SpeechModelState.Missing?"SpeechModelNotInstalled":"SpeechModelIncomplete";
        SpeechModelStatus.Text=speechInstall!=null?I18n.Text(uiLanguage,"SpeechDownloading",speechDownloadProgress):
            speechInstallError!=null && !ready?I18n.Text(uiLanguage,"SpeechInstallFailed")+" "+I18n.Text(uiLanguage,"SpeechRetry"):
            I18n.Text(uiLanguage,key);
        SpeechModelStatus.Foreground=ready?System.Windows.Media.Brushes.ForestGreen:System.Windows.Media.Brushes.DarkGoldenrod;
        SpeechTestButton.ToolTip=ready?null:I18n.Text(uiLanguage,"SpeechModelMissing");
    }
    async void InstallSpeech_Click(object sender,RoutedEventArgs e)
    {
        if(speechInstall!=null) { speechInstall.Cancel(); return; }
        var request=new CancellationTokenSource(); speechInstall=request;
        speechInstallError=null; speechDownloadProgress=0; RefreshSpeechAvailability();
        try
        {
            var progress=new Progress<int>(percent=> {
                if(speechInstall!=request || App.IsExiting) return;
                speechDownloadProgress=percent; RefreshSpeechAvailability();
            });
            await SpeechModel.EnsureAsync(progress,request.Token);
        }
        catch(OperationCanceledException) when(request.IsCancellationRequested) { }
        catch(Exception error) { speechInstallError=error; }
        finally { speechInstall=null; request.Dispose(); if(!App.IsExiting) RefreshSpeechAvailability(); }
    }
}
