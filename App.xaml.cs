using System.Windows;
using System.IO;
using System.Text.Json;
namespace OllamaTextLens;
public partial class App : Application
{
    SingleInstance? instance;
    internal static bool IsExiting { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        string language="zh-TW";
        try
        {
            var path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OllamaTextLens","settings.json");
            if(File.Exists(path)) language=Languages.Ui(JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path))?.UiLanguage).Id;
        }
        catch { /* The main window handles unreadable settings. */ }
        try
        {
            instance=new SingleInstance();
            if(!instance.TryAcquire(TimeSpan.FromSeconds(10), SingleInstance.CloseLegacyInstances))
            {
                MessageBox.Show(I18n.Text(language,"RestartFailed"),"OllamaTextLens",MessageBoxButton.OK,MessageBoxImage.Information);
                IsExiting=true; Shutdown(); return;
            }
            instance.ListenForReplacement(() => Dispatcher.BeginInvoke(new Action(() => { IsExiting=true; Shutdown(); })));
        }
        catch(Exception)
        {
            MessageBox.Show(I18n.Text(language,"RestartFailed"),"OllamaTextLens",MessageBoxButton.OK,MessageBoxImage.Information);
            IsExiting=true; Shutdown(); return;
        }
        base.OnStartup(e);
        // The settings window constructs a hidden popup first; explicitly choose the lifetime owner.
        var settingsWindow=new MainWindow();
        MainWindow=settingsWindow;
        settingsWindow.Show();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        IsExiting=true;
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsExiting=true;
        // Window shutdown unregisters the hotkey before the next process acquires the lock.
        instance?.Dispose(); instance=null;
        base.OnExit(e);
    }
}
