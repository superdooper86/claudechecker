using ClaudeCheckerWindows.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClaudeCheckerWindows;

public partial class PopupWindow : Window
{
    private static readonly UsageViewModel VM      = App.ViewModel;
    private static readonly UpdateManager  Updater = App.Updater;
    private readonly DispatcherTimer _clockTimer;

    public PopupWindow()
    {
        InitializeComponent();
        DataContext = VM;

        VM.PropertyChanged      += (_, e) => Dispatcher.InvokeAsync(() => OnVmChanged(e.PropertyName));
        Updater.PropertyChanged += (_, e) => Dispatcher.InvokeAsync(() => OnUpdaterChanged(e.PropertyName));

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _clockTimer.Tick += (_, _) => UpdateFooter();
        _clockTimer.Start();

        InitSettings();
        RebuildCards();
        UpdateBannerState();
        UpdateFooter();

        Deactivated += (_, _) => Hide();
    }

    // ── Card rendering ──────────────────────────────────────────────

    private void RebuildCards()
    {
        CardsPanel.Children.Clear();
        foreach (var limit in VM.Limits)
            CardsPanel.Children.Add(BuildCard(limit));
    }

    private static UIElement BuildCard(AgentLimit limit)
    {
        var accent = new SolidColorBrush(Color.FromRgb(0xF9, 0x73, 0x16));

        var gauge = new GaugeControl
        {
            Width = 76, Height = 76, Percent = limit.UsedPercent, Accent = accent,
        };

        var livePanel = new StackPanel { Orientation = Orientation.Horizontal };
        if (limit.IsLive)
        {
            livePanel.Children.Add(new Border
            {
                Background   = new SolidColorBrush(Color.FromArgb(26, 0, 255, 0)),
                CornerRadius = new CornerRadius(4),
                Padding      = new Thickness(6, 2, 6, 2),
                Margin       = new Thickness(0, 0, 6, 0),
                Child        = new TextBlock
                {
                    Text       = "Live",
                    FontSize   = 10,
                    FontWeight = FontWeights.Medium,
                    Foreground = new SolidColorBrush(Colors.LightGreen),
                }
            });
        }

        var usageBadge = new Border
        {
            Background    = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255)),
            CornerRadius  = new CornerRadius(4),
            Padding       = new Thickness(6, 2, 6, 2),
            Child         = new TextBlock
            {
                Text       = $"Usg: {limit.UsageLabel}",
                FontSize   = 10,
                FontWeight = FontWeights.Medium,
                Foreground = (SolidColorBrush)Application.Current.Resources["SecondaryBrush"],
            }
        };
        livePanel.Children.Add(usageBadge);

        var progressBar = new ProgressBar
        {
            Value      = limit.UsedPercent,
            Maximum    = 100,
            Height     = 4,
            Foreground = accent,
            Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
            BorderThickness = new Thickness(0),
            Margin     = new Thickness(0, 4, 0, 4),
        };

        var sparkline = new SparklineControl
        {
            Height    = 28,
            Data      = limit.BurnHistory.Count > 0 ? limit.BurnHistory : null,
            LineColor = Color.FromRgb(0xF9, 0x73, 0x16),
            Margin    = new Thickness(0, 8, 0, 0),
        };

        var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(new Grid
        {
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children    =
                    {
                        MakeText("✦", 11, accent),
                        MakeText("Claude", 13, Brushes.White, FontWeights.SemiBold, new Thickness(6,0,0,0)),
                        MakeText($"·  {VM.PlanLabel}", 11,
                            (SolidColorBrush)Application.Current.Resources["SecondaryBrush"],
                            margin: new Thickness(6,0,0,0)),
                    }
                }.Also(s => Grid.SetColumn(s, 0)),
                new StackPanel
                {
                    Orientation         = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children            = { /* live + usg */ },
                }.Also(s => { Grid.SetColumn(s, 1); ((StackPanel)s).Children.Add(livePanel); }),
            }.Tap(g => {
                var gc = (Grid)g.Parent ?? (Grid)Application.Current.MainWindow;
                ((Grid)g.Parent!).ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                ((Grid)g.Parent!).ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }),
        });

        // Simpler row approach
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftStack = new StackPanel { Orientation = Orientation.Horizontal };
        leftStack.Children.Add(MakeText("✦", 11, accent));
        leftStack.Children.Add(MakeText("Claude", 13, Brushes.White, FontWeights.SemiBold, new Thickness(6, 0, 0, 0)));
        leftStack.Children.Add(MakeText($"·  {VM.PlanLabel}", 11,
            (SolidColorBrush)Application.Current.Resources["SecondaryBrush"], margin: new Thickness(6, 0, 0, 0)));

        Grid.SetColumn(leftStack, 0);
        Grid.SetColumn(livePanel, 1);
        headerRow.Children.Add(leftStack);
        headerRow.Children.Add(livePanel);

        infoPanel.Children.Clear();
        infoPanel.Children.Add(headerRow);
        infoPanel.Children.Add(progressBar);

        var timeRow = new Grid();
        timeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var timeLeft = new StackPanel { Orientation = Orientation.Horizontal };
        timeLeft.Children.Add(MakeText("⏱ ", 10, (SolidColorBrush)Application.Current.Resources["SecondaryBrush"]));
        timeLeft.Children.Add(MakeText(limit.TimeRemaining, 12, Brushes.White, FontWeights.Medium));

        var resetText = MakeText(
            $"Resets {limit.ResetDate:d MMM yyyy} at {limit.ResetDate:HH:mm}",
            11, (SolidColorBrush)Application.Current.Resources["SecondaryBrush"]);

        Grid.SetColumn(timeLeft, 0);
        Grid.SetColumn(resetText, 1);
        timeRow.Children.Add(timeLeft);
        timeRow.Children.Add(resetText);
        infoPanel.Children.Add(timeRow);
        infoPanel.Children.Add(sparkline);

        var cardContent = new Grid { Margin = new Thickness(16, 12, 16, 12) };
        cardContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cardContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16, GridUnitType.Pixel) });
        cardContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(gauge, 0);
        Grid.SetColumn(infoPanel, 2);
        cardContent.Children.Add(gauge);
        cardContent.Children.Add(infoPanel);

        var card = new Border
        {
            Background      = (SolidColorBrush)Application.Current.Resources["CardBrush"],
            CornerRadius    = new CornerRadius(8),
            BorderBrush     = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
            BorderThickness = new Thickness(0.5),
            Child           = cardContent,
            Margin          = new Thickness(8, 0, 8, 0),
        };

        var sectionLabel = new TextBlock
        {
            Text      = limit.WindowLabel,
            Style     = (Style)Application.Current.Resources["HeaderText"],
            Margin    = new Thickness(16, 10, 16, 4),
        };

        var section = new StackPanel();
        section.Children.Add(sectionLabel);
        section.Children.Add(card);
        return section;
    }

    private static TextBlock MakeText(string text, double size, Brush fg,
        FontWeight? weight = null, Thickness? margin = null)
        => new()
        {
            Text                = text,
            FontSize            = size,
            Foreground          = fg,
            FontWeight          = weight ?? FontWeights.Normal,
            Margin              = margin ?? new Thickness(0),
            VerticalAlignment   = VerticalAlignment.Center,
        };

    // ── State updates ────────────────────────────────────────────────

    private void OnVmChanged(string? prop)
    {
        if (prop is nameof(UsageViewModel.Limits) or nameof(UsageViewModel.IsLoading)
                 or nameof(UsageViewModel.LastUpdated) or nameof(UsageViewModel.ErrorMessage))
        {
            RebuildCards();
            UpdateFooter();
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
        UpdateBanner.Visibility    = visible ? Visibility.Visible : Visibility.Collapsed;
        UpdateBannerDiv.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible) UpdateBannerTitle.Text = $"Update available — v{Updater.LatestVersion}";
    }

    private void UpdateFooter()
    {
        if (VM.LastUpdated.HasValue)
        {
            var diff = DateTime.Now - VM.LastUpdated.Value;
            LastUpdatedText.Text = diff.TotalSeconds < 10
                ? "Updated just now"
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
        VersionLabel.Text = $"v{App.Updater.CurrentVersion}";
        BetaToggle.IsChecked = Updater.BetaChannel;
        CheckUpdateButton.Content = Updater.UpdateAvailable ? "Install" : "Check";

        RefreshPicker.ItemsSource = new[]
        {
            ("1 min",  60),  ("2 min",  120), ("3 min",  180),
            ("4 min",  240), ("5 min",  300), ("10 min", 600),
        };
        RefreshPicker.DisplayMemberPath = "Item1";
        RefreshPicker.SelectedIndex = Array.FindIndex(
            new[] { 60, 120, 180, 240, 300, 600 },
            s => s == VM.RefreshInterval).Let(i => i < 0 ? 1 : i);

        var signedIn = VM.IsSignedIn;
        AuthPanel.Children.Clear();
        AuthPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children    =
            {
                new Ellipse
                {
                    Width = 7, Height = 7,
                    Fill  = signedIn ? Brushes.LimeGreen : Brushes.Orange,
                    Margin = new Thickness(0,0,6,0),
                    VerticalAlignment = VerticalAlignment.Center,
                },
                new TextBlock
                {
                    Text       = signedIn ? $"Signed in — {VM.UserEmail}" : "Not signed in",
                    FontSize   = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                }
            }
        });

        SignOutButton.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        SignInButton.Content     = signedIn ? "Re-authenticate" : "Sign In";
    }

    // ── Event handlers ───────────────────────────────────────────────

    private void Settings_Click(object s, RoutedEventArgs e)
    {
        InitSettings();
        MainPanel.Visibility    = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
        UpdatePanel.Visibility  = Visibility.Collapsed;
    }

    private void BackFromSettings_Click(object s, RoutedEventArgs e) => ShowMain();

    private void ShowMain()
    {
        MainPanel.Visibility    = Visibility.Visible;
        SettingsPanel.Visibility = Visibility.Collapsed;
        UpdatePanel.Visibility  = Visibility.Collapsed;
    }

    private void Close_Click(object s, RoutedEventArgs e) => Hide();

    private async void Refresh_Click(object s, RoutedEventArgs e) => await VM.RefreshAsync();

    private void UpdateBanner_Click(object s, RoutedEventArgs e) => ShowUpdatePanel();

    private void ShowUpdatePanel()
    {
        CurrentVerLabel.Text    = $"v{Updater.CurrentVersion}";
        NewVerLabel.Text        = $"v{Updater.LatestVersion}";
        ReleaseNotesText.Text   = Updater.ReleaseNotes;
        MainPanel.Visibility    = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        UpdatePanel.Visibility  = Visibility.Visible;
    }

    private void CloseUpdate_Click(object s, RoutedEventArgs e) => ShowMain();

    private async void Install_Click(object s, RoutedEventArgs e)
    {
        NotNowButton.Visibility  = Visibility.Collapsed;
        UpdateStatusText.Visibility = Visibility.Visible;
        UpdateProgress.Visibility   = Visibility.Visible;
        await Updater.DownloadAndInstallAsync();
    }

    private void SignIn_Click(object s, RoutedEventArgs e)
    {
        var login = new LoginWindow();
        login.Owner = this;
        if (login.ShowDialog() == true)
        {
            _ = VM.RefreshAsync();
            InitSettings();
        }
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
        if (RefreshPicker.SelectedItem is (string _, int seconds))
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

// Minimal extension helpers to avoid repeating boilerplate
file static class Extensions
{
    public static T Also<T>(this T t, Action<T> f) { f(t); return t; }
    public static TResult Let<T, TResult>(this T t, Func<T, TResult> f) => f(t);
}

file class Ellipse : System.Windows.Shapes.Ellipse { }
