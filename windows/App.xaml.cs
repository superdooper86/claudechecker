using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace ClaudeCheckerWindows;

public partial class App : Application
{
    public static UsageViewModel ViewModel { get; } = new();
    public static UpdateManager  Updater   { get; } = new();

    private NotifyIcon?   _tray;
    private PopupWindow?  _popup;
    private DispatcherTimer? _timer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SetupTray();
        ScheduleTimer(ViewModel.RefreshInterval);

        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            await ViewModel.RefreshAsync();
            await Updater.CheckForUpdatesAsync();
        });
    }

    private void SetupTray()
    {
        _tray = new NotifyIcon
        {
            Text    = "ClaudeChecker",
            Visible = true,
            Icon    = LoadIcon(),
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                TogglePopup();
        };
        _tray.ContextMenuStrip = BuildContextMenu();

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(UsageViewModel.Limits) or nameof(UsageViewModel.ShowInTaskbar))
                UpdateTrayText();
        };
    }

    private static Icon LoadIcon()
    {
        try { return new Icon("Assets/icon.ico"); }
        catch { return SystemIcons.Application; }
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show ClaudeChecker", null, (_, _) => ShowPopup());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());
        menu.BackColor = System.Drawing.Color.FromArgb(40, 40, 40);
        menu.ForeColor = System.Drawing.Color.White;
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
            _popup.Closed += (_, _) => _popup = null;
        }

        PositionPopup();
        _popup.Show();
        _popup.Activate();
    }

    private void PositionPopup()
    {
        if (_popup == null) return;
        var workArea = SystemParameters.WorkArea;
        _popup.Left  = workArea.Right  - _popup.Width  - 8;
        _popup.Top   = workArea.Bottom - _popup.Height - 8;
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
