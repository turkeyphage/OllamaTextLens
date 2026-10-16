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
    CancellationTokenSource? pending;
    bool capturing;
    HwndSource? source;
    readonly string settingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OllamaTextLens","settings.json");
    record Settings(string Endpoint,string Model);
    public MainWindow()
    {
        InitializeComponent();
        try
        {
            var loadPath=File.Exists(settingsPath)?settingsPath:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LocalPdfTranslator","settings.json");
            if(File.Exists(loadPath))
            {
                var settings=JsonSerializer.Deserialize<Settings>(File.ReadAllText(loadPath));
                if(settings!=null) { Endpoint.Text=settings.Endpoint; Models.Text=settings.Model; }
            }
        }
        catch { Status.Text="無法讀取設定，請重新儲存。"; }
        popup.Dismissed+=()=>pending?.Cancel();
        SourceInitialized+=(_,_)=> {
            source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); source.AddHook(Hook);
            Status.Text=Native.RegisterHotKey(source.Handle,1,0x4003,0x54)?"已就緒：Ctrl + Alt + T。首次載入模型可能需要較久。":"快捷鍵已被其他程式使用，請關閉衝突程式後重開本程式。";
        };
        Loaded+=async(_,_)=>await Refresh();
        Closed+=(_,_)=> { pending?.Cancel(); if(source!=null) { Native.UnregisterHotKey(source.Handle,1); source.RemoveHook(Hook); } };
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
            if(new[]{0x11,0x12,0x54}.Any(k=>Native.GetAsyncKeyState(k)<0)) throw new InvalidOperationException("請放開快捷鍵後再試一次。");
            if(Native.GetForegroundWindow()!=foreground) throw new InvalidOperationException("作用中的視窗已變更，請重新反白並重試。");
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
            if(string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("未取得選取文字。請在 PDF 中反白可複製的文字後再試；掃描圖片需先 OCR。");
            if(text.Length>12000) throw new InvalidOperationException("選取內容超過 12,000 字元，請縮小至幾個段落。");
            text=Regex.Replace(text,@"(?<=\p{L})-\r?\n(?=\p{Ll})","");
            text=Regex.Replace(text,@"(?<!\n)\r?\n(?!\r?\n)"," ").Trim();
            _=Translate(text,point);
        }
        catch(Exception ex) { popup.Begin(point,"取詞失敗"); popup.Update(ex.Message); }
        finally { capturing=false; }
    }
    async Task Translate(string text,Native.Point point)
    {
        pending?.Cancel();
        var current=new CancellationTokenSource(); pending=current;
        popup.Begin(point,"正在準備本機模型…",text);
        try
        {
            var endpoint=OllamaClient.Validate(Endpoint.Text);
            var model=Models.Text.Trim();
            if(model.Length==0) throw new InvalidOperationException("請先在主視窗選擇 Ollama 模型。");
            var watch=System.Diagnostics.Stopwatch.StartNew();
            await OllamaClient.Translate(endpoint,model,text,result=> { if(pending==current) { popup.Status("翻譯中…"); popup.Update(result); } },current.Token);
            if(pending==current) popup.Status($"完成 · {watch.Elapsed.TotalSeconds:F1} 秒 · {model}");
        }
        catch(OperationCanceledException) { if(pending==current) popup.Status("已停止或請求逾時，可再次按快捷鍵重試。"); }
        catch(Exception ex) { if(pending==current) { popup.Status("翻譯失敗"); popup.Update(ex is System.Net.Http.HttpRequestException?"無法連線至本機 Ollama。請確認 Ollama 正在執行，並按「重新讀取模型」。":ex.Message); } }
        finally { if(pending==current) pending=null; current.Dispose(); }
    }
    async Task Refresh()
    {
        try { var names=await OllamaClient.Models(OllamaClient.Validate(Endpoint.Text)); var selected=Models.Text; Models.ItemsSource=names; Models.Text=names.Contains(selected)?selected:names.FirstOrDefault()??""; Status.Text=names.Length==0?"Ollama 尚無模型，請先使用 ollama pull 下載聊天模型。":"模型已載入。Ctrl + Alt + T 可翻譯反白文字。"; }
        catch(Exception ex) { Status.Text="讀取模型失敗："+ex.Message; }
    }
    async void Refresh_Click(object sender,RoutedEventArgs e)=>await Refresh();
    void Save_Click(object sender,RoutedEventArgs e)
    {
        try { OllamaClient.Validate(Endpoint.Text); Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!); File.WriteAllText(settingsPath,JsonSerializer.Serialize(new Settings(Endpoint.Text,Models.Text))); Status.Text="設定已儲存。"; } catch(Exception ex) { Status.Text=ex.Message; }
    }
    async void Test_Click(object sender,RoutedEventArgs e) { Native.GetCursorPos(out var point); await Translate("The results suggest a strong correlation, but correlation does not imply causation.",point); }
}
