using ClaudeCheckerWindows.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
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

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
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

        if (VM.ExtraUsage?.IsEnabled == true)
            CardsPanel.Children.Add(BuildExtraUsageSection());

        var fiveHour = VM.Limits.FirstOrDefault(l => l.Window == WindowKind.FiveHour);
        if (fiveHour != null && fiveHour.BurnHistory.Count > 0)
            CardsPanel.Children.Add(BuildDiarySection(fiveHour));
    }

    private static UIElement BuildCard(AgentLimit limit)
    {
        var accent    = new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16));
        var text      = (SolidColorBrush)Application.Current.Resources["TextBrush"];
        var secondary = (SolidColorBrush)Application.Current.Resources["SecondaryBrush"];
        var border    = (SolidColorBrush)Application.Current.Resources["BorderBrush"];

        var gauge = new GaugeControl { Width = 76, Height = 76, Percent = limit.UsedPercent, Accent = accent };

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
        nameStack.Children.Add(new TextBlock { Text = "✶", FontSize = 11, Foreground = accent, VerticalAlignment = VerticalAlignment.Center });
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

        // Left: time remaining + reset date inline
        var timeLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        timeLeft.Children.Add(new TextBlock { Text = "⏱ ", FontSize = 10, Foreground = secondary, VerticalAlignment = VerticalAlignment.Center });
        timeLeft.Children.Add(new TextBlock { Text = limit.TimeRemaining, FontSize = 12,
            FontWeight = FontWeights.Medium, Foreground = text, VerticalAlignment = VerticalAlignment.Center });
        timeLeft.Children.Add(new TextBlock
        {
            Text = $"   Resets {limit.ResetDate:d MMM yyyy} at {limit.ResetDate:HH:mm}",
            FontSize = 10, Foreground = secondary, VerticalAlignment = VerticalAlignment.Center
        });

        // Right: "after reset" when 0%, "today HH:MM" when refreshed today
        string? refreshLabel = limit.UsedPercent == 0
            ? "after reset"
            : VM.LastUpdated?.Date == DateTime.Today
                ? "today " + VM.LastUpdated.Value.ToString("h:mm tt")
                : null;

        UIElement timeRight;
        if (refreshLabel != null)
        {
            var inner = new StackPanel { Orientation = Orientation.Horizontal };
            inner.Children.Add(new TextBlock { Text = "↗ ", FontSize = 10, Foreground = secondary, VerticalAlignment = VerticalAlignment.Center });
            inner.Children.Add(new TextBlock { Text = refreshLabel, FontSize = 10, FontWeight = FontWeights.Medium,
                Foreground = secondary, VerticalAlignment = VerticalAlignment.Center });
            timeRight = new Border
            {
                Background   = new SolidColorBrush(Color.FromArgb(18, 0, 0, 0)),
                CornerRadius = new CornerRadius(4),
                Padding      = new Thickness(6, 2, 6, 2),
                Child        = inner
            };
        }
        else
        {
            timeRight = new TextBlock();
        }

        var timeRow = new Grid();
        timeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(timeLeft,  0);
        Grid.SetColumn(timeRight, 1);
        timeRow.Children.Add(timeLeft);
        timeRow.Children.Add(timeRight);

        var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(headerRow);
        infoPanel.Children.Add(progress);
        infoPanel.Children.Add(timeRow);

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

    private static UIElement BuildDiarySection(AgentLimit limit)
    {
        var secondary = (SolidColorBrush)Application.Current.Resources["SecondaryBrush"];
        var border    = (SolidColorBrush)Application.Current.Resources["BorderBrush"];

        var statsRow = new Grid { Margin = new Thickness(12, 10, 12, 6) };
        statsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var samplesText = new TextBlock
        {
            Text = $"{limit.BurnHistory.Count} samples",
            FontSize = 11, Foreground = secondary, VerticalAlignment = VerticalAlignment.Center
        };
        var burnText = new TextBlock
        {
            Text = $"Avg burn rate: {limit.BurnRate:F1}%/h",
            FontSize = 11, Foreground = secondary, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(samplesText, 0);
        Grid.SetColumn(burnText,    1);
        statsRow.Children.Add(samplesText);
        statsRow.Children.Add(burnText);

        var sparkline = new SparklineControl
        {
            Height    = 40,
            Margin    = new Thickness(8, 0, 8, 8),
            LineColor = Color.FromRgb(0xF9, 0x73, 0x16),
            Data      = limit.BurnHistory,
        };

        var cardContent = new StackPanel();
        cardContent.Children.Add(statsRow);
        cardContent.Children.Add(sparkline);

        var card = new Border
        {
            Background      = (SolidColorBrush)Application.Current.Resources["CardBrush"],
            CornerRadius    = new CornerRadius(6),
            BorderBrush     = border,
            BorderThickness = new Thickness(1),
            Margin          = new Thickness(12, 0, 12, 0),
            Child           = cardContent,
        };

        var sectionLabel = new TextBlock
        {
            Text   = "SESSION DIARY",
            Style  = (Style)Application.Current.Resources["HeaderText"],
            Margin = new Thickness(16, 12, 16, 6),
        };

        var section = new StackPanel();
        section.Children.Add(sectionLabel);
        section.Children.Add(card);
        return section;
    }

    private UIElement BuildExtraUsageSection()
    {
        var text      = (SolidColorBrush)Application.Current.Resources["TextBrush"];
        var secondary = (SolidColorBrush)Application.Current.Resources["SecondaryBrush"];
        var border    = (SolidColorBrush)Application.Current.Resources["BorderBrush"];
        var accent    = new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16));

        var overage  = VM.Overage;
        var prepaid  = VM.Prepaid;
        var currency = VM.ExtraUsage?.Currency ?? overage?.Currency ?? "$";

        var spent   = (overage?.UsedCredits        / 100.0) ?? (VM.ExtraUsage?.UsedCredits  / 100.0) ?? 0;
        var limit   = (overage?.MonthlyCreditLimit / 100.0) ?? (VM.ExtraUsage?.MonthlyLimit / 100.0) ?? 0;
        var balance = (prepaid?.Amount             / 100.0) ?? 0;

        bool unlimited = overage?.MonthlyCreditLimit == null && VM.ExtraUsage?.MonthlyLimit == null;

        UIElement MakeRow(string label, string value, bool highlight = false)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var lbl = new TextBlock { Text = label, FontSize = 12, Foreground = secondary };
            var val = new TextBlock { Text = value, FontSize = 12, FontWeight = FontWeights.Medium,
                                      Foreground = highlight ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)) : text };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(val, 1);
            row.Children.Add(lbl);
            row.Children.Add(val);
            return row;
        }

        var contentPanel = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
        contentPanel.Children.Add(MakeRow("Spent",   $"{currency} {spent:F2}", spent > 0));
        if (unlimited)      contentPanel.Children.Add(MakeRow("Limit",   "Unlimited"));
        else if (limit > 0) contentPanel.Children.Add(MakeRow("Limit",   $"{currency} {limit:F2}"));
        if (balance > 0)    contentPanel.Children.Add(MakeRow("Balance", $"{currency} {balance:F2}"));

        if (limit > 0)
        {
            var pct = Math.Min(spent / limit * 100, 100);
            contentPanel.Children.Add(new ProgressBar
            {
                Value           = pct, Maximum = 100, Height = 4,
                Margin          = new Thickness(0, 6, 0, 0),
                Foreground      = accent,
                Background      = new SolidColorBrush(Color.FromArgb(20, 0, 0, 0)),
                BorderThickness = new Thickness(0),
            });
        }

        var card = new Border
        {
            Background      = (SolidColorBrush)Application.Current.Resources["CardBrush"],
            CornerRadius    = new CornerRadius(6),
            BorderBrush     = border,
            BorderThickness = new Thickness(1),
            Margin          = new Thickness(12, 0, 12, 0),
            Child           = contentPanel,
        };

        var sectionLabel = new TextBlock
        {
            Text   = "EXTRA USAGE CREDITS",
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
                 or nameof(UsageViewModel.LastUpdated) or nameof(UsageViewModel.ErrorMessage)
                 or nameof(UsageViewModel.ExtraUsage) or nameof(UsageViewModel.Overage) or nameof(UsageViewModel.Prepaid))
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
        {
            UpdateBannerState();
            UpdateAboutSection();
        }
        if (prop is nameof(UpdateManager.DownloadProgress))
            UpdateProgress.Value = Updater.DownloadProgress * 100;
        if (prop is nameof(UpdateManager.StatusMessage))
            UpdateStatusText.Text = Updater.StatusMessage;
        if (prop is nameof(UpdateManager.UpdateComplete) && Updater.UpdateComplete)
            InstallButton.Visibility = Visibility.Collapsed;
    }

    private void UpdateAboutSection()
    {
        if (Updater.UpdateAvailable)
        {
            UpdateAvailableText.Text       = $"v{Updater.LatestVersion} available";
            UpdateAvailableText.Visibility = Visibility.Visible;
            CheckUpdateButton.Content      = "Install";
        }
        else
        {
            UpdateAvailableText.Visibility = Visibility.Collapsed;
            CheckUpdateButton.Content      = "Check";
        }
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
            string age;
            if (diff.TotalSeconds < 10)
                age = "just now";
            else if (diff.TotalMinutes < 1)
                age = $"{(int)diff.TotalSeconds} sec ago";
            else
                age = $"{(int)diff.TotalMinutes} min, {diff.Seconds} sec ago";
            LastUpdatedText.Text = $"Updated {age}";
        }
        else
        {
            LastUpdatedText.Text = "Not yet updated";
        }
        LoadingBar.Visibility = VM.IsLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Settings ─────────────────────────────────────────────────────

    private static readonly (string Label, int Seconds)[] RefreshIntervals =
    [
        ("1 min",  60),
        ("2 min",  120),
        ("3 min",  180),
        ("4 min",  240),
        ("5 min",  300),
        ("10 min", 600),
    ];

    private void InitSettings()
    {
        VersionLabel.Text    = $"v{Updater.CurrentVersion}";
        BetaToggle.IsChecked = Updater.BetaChannel;
        UpdateAboutSection();

        RefreshPicker.SelectionChanged -= RefreshPicker_Changed;
        RefreshPicker.Items.Clear();
        int selectIdx = 1;
        for (int i = 0; i < RefreshIntervals.Length; i++)
        {
            var (label, seconds) = RefreshIntervals[i];
            RefreshPicker.Items.Add(new ComboBoxItem { Content = label, Tag = seconds });
            if (seconds == VM.RefreshInterval) selectIdx = i;
        }
        RefreshPicker.SelectedIndex = selectIdx;
        RefreshPicker.SelectionChanged += RefreshPicker_Changed;

        AuthPanel.Children.Clear();
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0),
            Fill = VM.IsSignedIn ? Brushes.LimeGreen : Brushes.Orange,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var label2 = new TextBlock
        {
            Text = VM.IsSignedIn ? $"Signed in — {VM.UserEmail}" : "Not signed in",
            FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)Application.Current.Resources["TextBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        var authRow = new StackPanel { Orientation = Orientation.Horizontal };
        authRow.Children.Add(dot);
        authRow.Children.Add(label2);
        AuthPanel.Children.Add(authRow);

        SignOutButton.Visibility = VM.IsSignedIn ? Visibility.Visible : Visibility.Collapsed;
        SignInButton.Content     = VM.IsSignedIn ? "Re-authenticate" : "Sign In";
    }

    // ── Diagnostics ──────────────────────────────────────────────────

    private void Diagnostics_Click(object s, RoutedEventArgs e)
    {
        RebuildDiag();
        MainPanel.Visibility        = Visibility.Collapsed;
        SettingsPanel.Visibility    = Visibility.Collapsed;
        UpdatePanel.Visibility      = Visibility.Collapsed;
        DiagnosticsPanel.Visibility = Visibility.Visible;
    }

    private void BackFromDiag_Click(object s, RoutedEventArgs e)
    {
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility    = Visibility.Visible;
    }

    private void CopyDiag_Click(object s, RoutedEventArgs e)
    {
        Clipboard.SetText(BuildDiagText());
        CopyDiagButton.Content = "Copied!";
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        t.Tick += (_, _) => { CopyDiagButton.Content = "Copy All"; t.Stop(); };
        t.Start();
    }

    private void RebuildDiag()
    {
        DiagContentPanel.Children.Clear();
        var secondary = (SolidColorBrush)Application.Current.Resources["SecondaryBrush"];
        var border    = (SolidColorBrush)Application.Current.Resources["BorderBrush"];
        var text      = (SolidColorBrush)Application.Current.Resources["TextBrush"];

        FrameworkElement MakeRow(string label, string value)
        {
            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock
            {
                Text = label, FontSize = 11, Foreground = secondary,
                VerticalAlignment = VerticalAlignment.Top,
            };
            var val = new TextBlock
            {
                Text = value, FontSize = 11, FontFamily = new FontFamily("Consolas"),
                Foreground = text, TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(val, 1);
            grid.Children.Add(lbl);
            grid.Children.Add(val);
            return grid;
        }

        void AddSection(string title, IEnumerable<FrameworkElement> rows)
        {
            DiagContentPanel.Children.Add(new TextBlock
            {
                Text = title, Style = (Style)Application.Current.Resources["HeaderText"],
                Margin = new Thickness(0, 12, 0, 6),
            });
            var panel = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
            foreach (var row in rows) panel.Children.Add(row);
            DiagContentPanel.Children.Add(new Border
            {
                Background      = (SolidColorBrush)Application.Current.Resources["CardBrush"],
                CornerRadius    = new CornerRadius(6),
                BorderBrush     = border, BorderThickness = new Thickness(1),
                Child           = panel,
            });
        }

        // APP
        AddSection("APP",
        [
            MakeRow("Version",       Updater.CurrentVersion),
            MakeRow("Signed in",     VM.IsSignedIn ? "Yes" : "No"),
            MakeRow("Email",         string.IsNullOrEmpty(VM.UserEmail)          ? "(none)"     : VM.UserEmail),
            MakeRow("Org ID",        string.IsNullOrEmpty(VM.DiagOrgId)          ? "(none)"     : VM.DiagOrgId),
            MakeRow("lastActiveOrg", string.IsNullOrEmpty(VM.DiagLastActiveOrg)  ? "(not read)" : VM.DiagLastActiveOrg),
            MakeRow("Error",         VM.ErrorMessage ?? "(none)"),
        ]);

        // LAST REQUEST
        var reqRows = new List<FrameworkElement>
        {
            MakeRow("Path",   string.IsNullOrEmpty(VM.DiagLastPath)  ? "(none)" : VM.DiagLastPath),
            MakeRow("Status", VM.DiagLastStatus == 0 ? "(none)" : VM.DiagLastStatus.ToString()),
            MakeRow("Error",  string.IsNullOrEmpty(VM.DiagLastError) ? "(none)" : VM.DiagLastError),
        };
        if (VM.DiagLastFetch.HasValue)
            reqRows.Add(MakeRow("Time", VM.DiagLastFetch.Value.ToString("HH:mm:ss")));
        AddSection("LAST REQUEST", reqRows);

        // COOKIE STORE
        AddSection("COOKIE STORE",
        [
            MakeRow("Total cookies",  VM.DiagCookieCount.ToString()),
            MakeRow("Claude cookies", VM.DiagClaudeCookieCount.ToString()),
            MakeRow("All domains",    VM.DiagCookieDomains.Count == 0
                ? "(none)" : string.Join(", ", VM.DiagCookieDomains)),
        ]);

        // STORED COOKIE NAMES
        var storedNames = GetStoredCookieNames();
        var nameRows = new List<FrameworkElement>();
        if (storedNames.Count == 0)
        {
            nameRows.Add(MakeRow("(none)", ""));
        }
        else
        {
            var claudeC = storedNames.Where(c =>
                c.Domain.Contains("claude.ai") || c.Domain.Contains("anthropic.com")).ToList();
            var otherC  = storedNames.Where(c =>
                !c.Domain.Contains("claude.ai") && !c.Domain.Contains("anthropic.com")).ToList();
            foreach (var c in claudeC)
                nameRows.Add(MakeRow(c.Domain, c.Name));
            if (otherC.Count > 0)
                nameRows.Add(MakeRow($"other ({otherC.Count})",
                    string.Join(", ", otherC.Take(5).Select(c => c.Name))));
        }
        AddSection("STORED COOKIE NAMES", nameRows);
    }

    private static List<(string Name, string Domain)> GetStoredCookieNames()
    {
        try
        {
            var raw = AppSettings.Default.CookieStore;
            if (string.IsNullOrEmpty(raw)) return [];
            using var doc = JsonDocument.Parse(raw);
            var list = new List<(string Name, string Domain)>();
            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                var name   = elem.TryGetProperty("Name",   out var n) ? n.GetString() ?? "" : "";
                var domain = elem.TryGetProperty("Domain", out var d) ? d.GetString() ?? "" : "";
                list.Add((name, domain));
            }
            return list;
        }
        catch { return []; }
    }

    private static string BuildDiagText()
    {
        var storedNames = GetStoredCookieNames();
        var sb = new StringBuilder();
        sb.AppendLine("=== ClaudeChecker Diagnostics (Windows) ===");
        sb.AppendLine($"Version: {Updater.CurrentVersion}");
        sb.AppendLine($"Signed in: {(VM.IsSignedIn ? "Yes" : "No")}");
        sb.AppendLine($"Email: {(string.IsNullOrEmpty(VM.UserEmail) ? "(none)" : VM.UserEmail)}");
        sb.AppendLine($"Org ID: {(string.IsNullOrEmpty(VM.DiagOrgId) ? "(none)" : VM.DiagOrgId)}");
        sb.AppendLine($"lastActiveOrg: {(string.IsNullOrEmpty(VM.DiagLastActiveOrg) ? "(not read)" : VM.DiagLastActiveOrg)}");
        sb.AppendLine($"Error: {VM.ErrorMessage ?? "(none)"}");
        sb.AppendLine();
        sb.AppendLine($"Last path: {VM.DiagLastPath}");
        sb.AppendLine($"Last status: {VM.DiagLastStatus}");
        sb.AppendLine($"Last error: {VM.DiagLastError}");
        if (VM.DiagLastFetch.HasValue)
            sb.AppendLine($"Last fetch: {VM.DiagLastFetch.Value:HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"Total cookies: {VM.DiagCookieCount}");
        sb.AppendLine($"Claude cookies: {VM.DiagClaudeCookieCount}");
        sb.AppendLine($"Domains: {string.Join(", ", VM.DiagCookieDomains)}");
        sb.AppendLine();
        sb.AppendLine("Stored cookies:");
        foreach (var c in storedNames)
            sb.AppendLine($"  {c.Domain}  {c.Name}");
        return sb.ToString();
    }

    // ── Event handlers ───────────────────────────────────────────────

    private void Settings_Click(object s, RoutedEventArgs e)
    {
        InitSettings();
        MainPanel.Visibility        = Visibility.Collapsed;
        SettingsPanel.Visibility    = Visibility.Visible;
        UpdatePanel.Visibility      = Visibility.Collapsed;
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
    }

    private void BackFromSettings_Click(object s, RoutedEventArgs e) => ShowMain();

    private void ShowMain()
    {
        MainPanel.Visibility        = Visibility.Visible;
        SettingsPanel.Visibility    = Visibility.Collapsed;
        UpdatePanel.Visibility      = Visibility.Collapsed;
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
    }

    private async void Refresh_Click(object s, RoutedEventArgs e) => await VM.RefreshAsync();

    private void UpdateBanner_Click(object s, RoutedEventArgs e) => ShowUpdatePanel();

    private void ShowUpdatePanel()
    {
        CurrentVerLabel.Text  = $"v{Updater.CurrentVersion}";
        NewVerLabel.Text      = $"v{Updater.LatestVersion}";
        ReleaseNotesText.Text = Updater.ReleaseNotes;
        MainPanel.Visibility        = Visibility.Collapsed;
        SettingsPanel.Visibility    = Visibility.Collapsed;
        UpdatePanel.Visibility      = Visibility.Visible;
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
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
        var login = new LoginWindow { Owner = this };
        if (login.ShowDialog() == true)
        {
            await VM.LoadFromCacheAsync();
            InitSettings();
            ShowMain();

            _ = Task.Run(async () =>
            {
                await Task.Delay(3000);
                await VM.RefreshAsync();
                await Application.Current.Dispatcher.InvokeAsync(InitSettings);
            });
        }
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
        if (RefreshPicker.SelectedItem is ComboBoxItem item && item.Tag is int seconds)
        {
            VM.RefreshInterval = seconds;
            ((App)Application.Current).ScheduleTimer(seconds);
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
