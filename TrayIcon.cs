using Forms = System.Windows.Forms;

namespace OllamaTextLens;

internal sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon icon;
    readonly System.Drawing.Icon appIcon;
    readonly Forms.ToolStripMenuItem open = new();
    readonly Forms.ToolStripMenuItem exit = new();
    readonly Forms.ContextMenuStrip menu = new();

    internal TrayIcon(Action showSettings, Action exitApplication)
    {
        open.Click += (_, _) => showSettings();
        exit.Click += (_, _) => exitApplication();
        menu.Items.Add(open);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exit);
        using(var stream=System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/tray.ico")).Stream)
        using(var loaded=new System.Drawing.Icon(stream,Forms.SystemInformation.SmallIconSize))
            appIcon=(System.Drawing.Icon)loaded.Clone();
        icon = new Forms.NotifyIcon {
            Text = "OllamaTextLens",
            Icon = appIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        icon.DoubleClick += (_, _) => showSettings();
    }

    internal void SetLanguage(string language)
    {
        open.Text = I18n.Text(language, "TrayOpen");
        exit.Text = I18n.Text(language, "TrayExit");
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        appIcon.Dispose();
        menu.Dispose();
    }
}
