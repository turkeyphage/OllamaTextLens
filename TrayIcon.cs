using Forms = System.Windows.Forms;

namespace OllamaTextLens;

internal sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon icon;
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
        icon = new Forms.NotifyIcon {
            Text = "OllamaTextLens",
            Icon = System.Drawing.SystemIcons.Application,
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
        menu.Dispose();
    }
}
