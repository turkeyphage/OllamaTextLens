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
        Closed+=(_,_)=> { tray.Dispose(); pending?.Cancel(); if(source!=null) { Native.UnregisterHotKey(source.Handle,1); source.RemoveHook(Hook); } };
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
        Status.Text=T(statusKey)+(statusDetail.Length==0?"":" "+statusDetail);
        popup.SetLanguage(uiLanguage);
        tray.SetLanguage(uiLanguage);
        Language=System.Windows.Markup.XmlLanguage.GetLanguage(uiLanguage);
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
        try { var names=await OllamaClient.Models(OllamaClient.Validate(Endpoint.Text)); var selected=Models.Text; Models.ItemsSource=names; Models.Text=names.Contains(selected)?selected:names.FirstOrDefault()??""; SetStatus(!hotkeyRegistered?"HotkeyBusy":names.Length==0?"NoModels":"ModelsLoaded"); }
        catch(Exception ex) { SetStatus("ModelsFailed",I18n.Error(uiLanguage,ex)); }
    }
    async void Refresh_Click(object sender,RoutedEventArgs e)=>await Refresh();
    void Save_Click(object sender,RoutedEventArgs e)
    {
        try { OllamaClient.Validate(Endpoint.Text); Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!); File.WriteAllText(settingsPath,JsonSerializer.Serialize(new UserSettings(Endpoint.Text,Models.Text,uiLanguage,targetLanguage))); SetStatus("Saved"); } catch(Exception ex) { SetStatus("SettingsFailed",I18n.Error(uiLanguage,ex)); }
    }
    async void Test_Click(object sender,RoutedEventArgs e) { Native.GetCursorPos(out var point); await Translate("The results suggest a strong correlation, but correlation does not imply causation.",point); }
}
