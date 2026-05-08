using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClaudeCheckerWindows;

public static class ThemeManager
{
    public static bool IsDark { get; private set; }
    public static event Action? ThemeChanged;

    public static void Initialize()
    {
        IsDark = ReadIsDark();
        Apply();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            var nowDark = ReadIsDark();
            if (nowDark == IsDark) return;
            IsDark = nowDark;
            Apply();
            ThemeChanged?.Invoke();
        };
        timer.Start();
    }

    private static bool ReadIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return (int)(key?.GetValue("AppsUseLightTheme") ?? 1) == 0;
        }
        catch { return false; }
    }

    private static void Apply()
    {
        var res = Application.Current.Resources;
        if (IsDark)
        {
            Set(res, "BackgroundBrush",   Color.FromRgb(0x1C, 0x1C, 0x1E));
            Set(res, "CardBrush",         Color.FromRgb(0x2C, 0x2C, 0x2E));
            Set(res, "TextBrush",         Color.FromRgb(0xF5, 0xF5, 0xF5));
            Set(res, "SecondaryBrush",    Color.FromRgb(0x8A, 0x8A, 0x8E));
            Set(res, "BorderBrush",       Color.FromRgb(0x3A, 0x3A, 0x3C));
            Set(res, "BlueBrush",         Color.FromRgb(0x3B, 0x82, 0xF6));
            Set(res, "ButtonBrush",       Color.FromRgb(0x3A, 0x3A, 0x3C));
            Set(res, "SurfaceBrush",      Color.FromRgb(0x25, 0x25, 0x27));
            Set(res, "BannerBgBrush",     Color.FromRgb(0x1A, 0x31, 0x52));
            Set(res, "BannerBorderBrush", Color.FromRgb(0x2A, 0x4A, 0x72));
        }
        else
        {
            Set(res, "BackgroundBrush",   Color.FromRgb(0xF3, 0xF3, 0xF3));
            Set(res, "CardBrush",         Colors.White);
            Set(res, "TextBrush",         Color.FromRgb(0x1C, 0x1C, 0x1C));
            Set(res, "SecondaryBrush",    Color.FromRgb(0x6E, 0x6E, 0x6E));
            Set(res, "BorderBrush",       Color.FromRgb(0xE0, 0xE0, 0xE0));
            Set(res, "BlueBrush",         Color.FromRgb(0x00, 0x78, 0xD4));
            Set(res, "ButtonBrush",       Colors.White);
            Set(res, "SurfaceBrush",      Color.FromRgb(0xFA, 0xFA, 0xFA));
            Set(res, "BannerBgBrush",     Color.FromRgb(0xE8, 0xF2, 0xFB));
            Set(res, "BannerBorderBrush", Color.FromRgb(0xB3, 0xD6, 0xF5));
        }
    }

    private static void Set(ResourceDictionary res, string key, Color color)
    {
        if (res[key] is SolidColorBrush b) b.Color = color;
    }
}
