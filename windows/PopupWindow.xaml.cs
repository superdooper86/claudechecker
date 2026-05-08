using ClaudeCheckerWindows.Controls;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClaudeCheckerWindows;

public partial class PopupWindow : Window
{
    private static readonly UsageViewModel VM      = App.ViewModel;
    private static readonly UpdateManager  Updater = App.Updater;
    private readonly DispatcherTimer _clockTimer;
    private bool _dialogOpen;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void ApplyTitleBarTheme()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = ThemeManager.IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
    }

    public PopupWindow()
    {
        InitializeComponent();

        VM.PropertyChanged      += (_, e) => Dispatcher.InvokeAsync(() => OnVmChanged(e.PropertyName));
        Updater.PropertyChanged += (_, e) => Dispatcher.InvokeAsync(() => OnUpdaterChanged(e.PropertyName));

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _clockTimer.Tick += (_, _) => UpdateFooter();
        _clockTimer.Start();

        InitSettings();
        RebuildCards();
        UpdateBannerState();
        UpdateFooter();

        Closing += (_, e) => { e.Cancel = true; Hide(); };
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
        ThemeManager.ThemeChanged += () => { ApplyTitleBarTheme(); RebuildCards(); };
    }

    // ── Card rendering ───────────────────────────────────────────────

    private void RebuildCards()
    {
        CardsPanel.Children.Clear();
        foreach (var limit in VM.Limits)
            CardsPanel.Children.Add(BuildCard(limit));
    }

    private static UIElement BuildCard(AgentLimit limit)
    {
        var accent    = new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16));
        var text      = (SolidColorBrush)Application.Current.Resources["TextBrush"];
        var secondary = (SolidColorBrush)Application.Current.Resources["SecondaryBrush"];
        var border    = (SolidColorBrush)Application.Current.Resources["BorderBrush"];

        // Gauge
        var gauge = new GaugeControl { Width = 76, Height = 76, Percent = limit.UsedPercent, Accent = accent };

        // Live badge
        var liveBadge = new Border
        {
            Background   = new SolidColorBrush(Color.FromArgb(30, 0, 160, 0)),
            CornerRadius = new CornerRadius(4),
            Padding      = new Thickness(6, 2, 6, 2),
            Margin       = new Thickness(0, 0, 6, 0),
            Visibility   = limit.IsLive ? Visibility.Visible : Visibility.Collapsed,
            Child        = new TextBlock { Text = "Live", FontSize = 10, FontWeight = FontWeights.Medium,
                               Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 0)) }
        };

        // Usage badge
        var usageBadge = new Border
        {
            Background   = new SolidColorBrush(Color.FromArgb(18, 0, 0, 0)),
            CornerRadius = new CornerRadius(4),
            Padding      = new Thickness(6, 2, 6, 2),
            Child        = new TextBlock { Text = $"Usg: {limit.UsageLabel}", FontSize = 10,
                               FontWeight = FontWeights.Medium, Foreground = secondary }
        };

        var badgeStack = new StackPanel { Orientation = Orientation.Horizontal };
        badgeStack.Children.Add(liveBadge);
        badgeStack.Children.Add(usageBadge);

        var nameStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        nameStack.Children.Add(new TextBlock { Text = "✦", FontSize = 11, Foreground = accent, VerticalAlignment = VerticalAlignment.Center });
        nameStack.Children.Add(new TextBlock { Text = "Claude", FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        nameStack.Children.Add(new TextBlock { Text = $"·  {VM.PlanLabel}", FontSize = 11,
            Foreground = secondary, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });

        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(nameStack,  0);
        Grid.SetColumn(badgeStack, 1);
        headerRow.Children.Add(nameStack);
        headerRow.Children.Add(badgeStack);

        var progress = new ProgressBar
        {
            Value = limit.UsedPercent, Maximum = 100, Height = 4,
            Margin = new Thickness(0, 4, 0, 4),
            Foreground = accent,
            Background = new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)),
            BorderThickness = new Thickness(0),
        };

        var timeStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        timeStack.Children.Add(new TextBlock { Text = "⏱ ", FontSize = 10, Foreground = secondary, VerticalAlignment = VerticalAlignment.Center });
        timeStack.Children.Add(new TextBlock { Text = limit.TimeRemaining, FontSize = 12,
            FontWeight = FontWeights.Medium, Foreground = text, VerticalAlignment = VerticalAlignment.Center });

        var resetText = new TextBlock
        {
            Text = $"Resets {limit.ResetDate:d MMM yyyy} at {limit.ResetDate:HH:mm}",
            FontSize = 11, Foreground = secondary, VerticalAlignment = VerticalAlignment.Center
        };

        var timeRow = new Grid();
        timeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(timeStack, 0);
        Grid.SetColumn(resetText, 1);
        timeRow.Children.Add(timeStack);
        timeRow.Children.Add(resetText);

        var sparkline = new SparklineControl
        {
            Height = 28, Margin = new Thickness(0, 8, 0, 0), LineColor = Color.FromRgb(0xF9, 0x73, 0x16),
            Data = limit.BurnHistory.Count > 0 ? limit.BurnHistory : null,
        };

        var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(headerRow);
        infoPanel.Children.Add(progress);
        infoPanel.Children.Add(timeRow);
        infoPanel.Children.Add(sparkline);

        var cardGrid = new Grid { Margin = new Thickness(16, 12, 16, 12) };
        cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16, GridUnitType.Pixel) });
        cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(gauge,     0);
        Grid.SetColumn(infoPanel, 2);
        cardGrid.Children.Add(gauge);
        cardGrid.Children.Add(infoPanel);

        var card = new Border
        {
            Background      = (SolidColorBrush)Application.Current.Resources["CardBrush"],
            CornerRadius    = new CornerRadius(6),
            BorderBrush     = border,
            BorderThickness = new Thickness(1),
            Margin          = new Thickness(12, 0, 12, 0),
            Child           = cardGrid,
        };

        var sectionLabel = new TextBlock
        {
            Text   = limit.WindowLabel,
            Style  = (Style)Application.Current.Resources["HeaderText"],
            Margin = new Thickness(16, 12, 16, 6),
        };

        var section = new StackPanel();
        section.Children.Add(sectionLabel);
        section.Children.Add(card);
        return section;
    }

    // ── State updates ────────────────────────────────────────────────

    private void OnVmChanged(string? prop)
    {
        if (prop is nameof(UsageViewModel.Limits) or nameof(UsageViewModel.IsLoading)
                 or nameof(UsageViewModel.LastUpdated) or nameof(UsageViewModel.ErrorMessage))
        {
            RebuildCards();
            UpdateFooter();
        }
        if (prop is nameof(UsageViewModel.IsSignedIn) or nameof(UsageViewModel.UserEmail))
        {
            InitSettings();
        }
    }

    private void OnUpdaterChanged(string? prop)
    {
        if (prop is nameof(UpdateManager.UpdateAvailable) or nameof(UpdateManager.LatestVersion))
            UpdateBannerState();
        if (prop is nameof(UpdateManager.DownloadProgress))
            UpdateProgress.Value = Updater.DownloadProgress * 100;
        if (prop is nameof(UpdateManager.StatusMessage))
            UpdateStatusText.Text = Updater.StatusMessage;
        if (prop is nameof(UpdateManager.UpdateComplete) && Updater.UpdateComplete)
            InstallButton.Visibility = Visibility.Collapsed;
    }

    private void UpdateBannerState()
    {
        var visible = Updater.UpdateAvailable;
        UpdateBanner.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible) UpdateBannerTitle.Text = $"Update available — v{Updater.LatestVersion}";
    }

    private void UpdateFooter()
    {
        if (VM.LastUpdated.HasValue)
        {
            var diff = DateTime.Now - VM.LastUpdated.Value;
            LastUpdatedText.Text = diff.TotalSeconds < 10 ? "Updated just now"
                                                          : $"Updated {(int)diff.TotalSeconds}s ago";
        }
        else
        {
            LastUpdatedText.Text = "Not yet updated";
        }
        LoadingBar.Visibility = VM.IsLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Settings ─────────────────────────────────────────────────────

    private void InitSettings()
    {
        VersionLabel.Text    = $"v{Updater.CurrentVersion}";
        BetaToggle.IsChecked = Updater.BetaChannel;
        CheckUpdateButton.Content = Updater.UpdateAvailable ? "Install" : "Check";

        var intervals = new[] { ("1 min", 60), ("2 min", 120), ("3 min", 180),
                                ("4 min", 240), ("5 min", 300), ("10 min", 600) };
        RefreshPicker.ItemsSource        = intervals;
        RefreshPicker.DisplayMemberPath  = "Item1";
        var idx = Array.FindIndex(intervals, x => x.Item2 == VM.RefreshInterval);
        RefreshPicker.SelectedIndex      = idx < 0 ? 1 : idx;

        AuthPanel.Children.Clear();
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0),
            Fill = VM.IsSignedIn ? Brushes.LimeGreen : Brushes.Orange,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var label = new TextBlock
        {
            Text = VM.IsSignedIn ? $"Signed in — {VM.UserEmail}" : "Not signed in",
            FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var authRow = new StackPanel { Orientation = Orientation.Horizontal };
        authRow.Children.Add(dot);
        authRow.Children.Add(label);
        AuthPanel.Children.Add(authRow);

        SignOutButton.Visibility = VM.IsSignedIn ? Visibility.Visible : Visibility.Collapsed;
        SignInButton.Content     = VM.IsSignedIn ? "Re-authenticate" : "Sign In";
    }

    // ── Event handlers ───────────────────────────────────────────────

    private void Settings_Click(object s, RoutedEventArgs e)
    {
        InitSettings();
        MainPanel.Visibility     = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
        UpdatePanel.Visibility   = Visibility.Collapsed;
    }

    private void BackFromSettings_Click(object s, RoutedEventArgs e) => ShowMain();

    private void ShowMain()
    {
        MainPanel.Visibility     = Visibility.Visible;
        SettingsPanel.Visibility = Visibility.Collapsed;
        UpdatePanel.Visibility   = Visibility.Collapsed;
    }

    private async void Refresh_Click(object s, RoutedEventArgs e) => await VM.RefreshAsync();

    private void UpdateBanner_Click(object s, RoutedEventArgs e) => ShowUpdatePanel();

    private void ShowUpdatePanel()
    {
        CurrentVerLabel.Text  = $"v{Updater.CurrentVersion}";
        NewVerLabel.Text      = $"v{Updater.LatestVersion}";
        ReleaseNotesText.Text = Updater.ReleaseNotes;
        MainPanel.Visibility     = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        UpdatePanel.Visibility   = Visibility.Visible;
    }

    private void CloseUpdate_Click(object s, RoutedEventArgs e) => ShowMain();

    private async void Install_Click(object s, RoutedEventArgs e)
    {
        NotNowButton.Visibility     = Visibility.Collapsed;
        UpdateStatusText.Visibility = Visibility.Visible;
        UpdateProgress.Visibility   = Visibility.Visible;
        await Updater.DownloadAndInstallAsync();
    }

    private async void SignIn_Click(object s, RoutedEventArgs e)
    {
        _dialogOpen = true;
        var login = new LoginWindow { Owner = this };
        if (login.ShowDialog() == true)
        {
            await VM.RefreshAsync();
            InitSettings();
        }
        _dialogOpen = false;
        Show();
        Activate();
    }

    private async void SignOut_Click(object s, RoutedEventArgs e)
    {
        await VM.SignOutAsync();
        InitSettings();
    }

    private void BetaToggle_Changed(object s, RoutedEventArgs e)
    {
        Updater.BetaChannel = BetaToggle.IsChecked == true;
        _ = Updater.CheckForUpdatesAsync();
    }

    private void RefreshPicker_Changed(object s, SelectionChangedEventArgs e)
    {
        if (RefreshPicker.SelectedItem is ValueTuple<string, int> selected)
        {
            VM.RefreshInterval = selected.Item2;
            ((App)Application.Current).ScheduleTimer(selected.Item2);
        }
    }

    private async void CheckUpdate_Click(object s, RoutedEventArgs e)
    {
        if (Updater.UpdateAvailable) { ShowUpdatePanel(); return; }
        CheckUpdateButton.Content = "…";
        await Updater.CheckForUpdatesAsync();
        CheckUpdateButton.Content = Updater.UpdateAvailable ? "Install" : "Check";
    }
}
