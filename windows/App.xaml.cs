using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ClaudeCheckerWindows;

public partial class App : Application
{
    public static UsageViewModel ViewModel { get; } = new();
    public static UpdateManager  Updater   { get; } = new();

    private Forms.NotifyIcon? _tray;
    private PopupWindow?      _popup;
    private DispatcherTimer?  _timer;

    protected override void OnStartup(StartupEventArgs e)
    {
        var logPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClaudeChecker", "startup_crash.txt");

        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            System.IO.File.WriteAllText(logPath, ex.ExceptionObject?.ToString() ?? "unknown");

        DispatcherUnhandledException += (_, ex) =>
        {
            System.IO.File.WriteAllText(logPath, ex.Exception?.ToString() ?? "unknown");
            ex.Handled = false;
        };

        base.OnStartup(e);
        ThemeManager.Initialize();
        SetupTray();
        ScheduleTimer(ViewModel.RefreshInterval);

        _ = Task.Run(async () =>
        {
            await ViewModel.LoadFromCacheAsync();
            await Task.Delay(1000);
            await ViewModel.RefreshAsync();
            await Updater.CheckForUpdatesAsync();
        });
    }

    private void SetupTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Text    = "ClaudeChecker",
            Visible = true,
            Icon    = LoadIcon(),
        };

        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                Dispatcher.InvokeAsync(TogglePopup);
        };

        _tray.ContextMenuStrip = BuildContextMenu();

        ViewModel.PropertyChanged += (_, _) => UpdateTrayText();
        Updater.PropertyChanged   += (_, _) => UpdateTrayText();
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try { return new System.Drawing.Icon("Assets/icon.ico"); }
        catch { return System.Drawing.SystemIcons.Application; }
    }

    private Forms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show ClaudeChecker", null, (_, _) => Dispatcher.InvokeAsync(ShowPopup));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Dispatcher.InvokeAsync(Quit));
        return menu;
    }

    private void TogglePopup()
    {
        if (_popup == null || !_popup.IsVisible)
            ShowPopup();
        else
            _popup.Hide();
    }

    private void ShowPopup()
    {
        if (_popup == null)
        {
            _popup = new PopupWindow();
        }
        _popup.Show();
        _popup.Activate();
    }

    public void ScheduleTimer(int seconds)
    {
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _timer.Tick += async (_, _) =>
        {
            await ViewModel.RefreshAsync();
            await Updater.CheckForUpdatesAsync();
        };
        _timer.Start();
    }

    private void UpdateTrayText()
    {
        if (_tray == null) return;
        var limits = ViewModel.Limits;
        if (ViewModel.ShowInTaskbar && limits.Count >= 2)
        {
            var fh = limits.Find(l => l.Window == WindowKind.FiveHour);
            var sd = limits.Find(l => l.Window == WindowKind.SevenDay);
            if (fh != null && sd != null && fh.IsLive)
            {
                _tray.Text = $"ClaudeChecker  {(int)fh.UsedPercent}%  {(int)sd.UsedPercent}%";
                return;
            }
        }
        _tray.Text = "ClaudeChecker";
    }

    private void Quit()
    {
        _tray?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
