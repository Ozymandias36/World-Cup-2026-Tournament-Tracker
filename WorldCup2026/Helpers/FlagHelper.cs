using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WorldCup2026.Helpers;

/// <summary>
/// Creates flag visuals: colored circle with FIFA code letters.
/// Works 100% offline on all Windows versions.
/// </summary>
public static class FlagHelper
{
    private static readonly Dictionary<string, Color> ConfederationColors = new()
    {
        ["UEFA"] = Color.FromRgb(0x1a, 0x56, 0xdb),      // Blue - Europe
        ["CONMEBOL"] = Color.FromRgb(0x1b, 0x7a, 0x3d),   // Green - South America
        ["CONCACAF"] = Color.FromRgb(0xd4, 0x6a, 0x0e),    // Orange - North America
        ["CAF"] = Color.FromRgb(0xc8, 0xa9, 0x51),         // Gold - Africa
        ["AFC"] = Color.FromRgb(0xc4, 0x22, 0x33),         // Red - Asia
        ["OFC"] = Color.FromRgb(0x00, 0x7d, 0xb8),         // Light Blue - Oceania
    };

    // FIFA code → confederation mapping for all 2026 teams
    private static readonly Dictionary<string, string> Confederation = new()
    {
        ["MEX"] = "CONCACAF", ["RSA"] = "CAF", ["KOR"] = "AFC", ["CZE"] = "UEFA",
        ["CAN"] = "CONCACAF", ["BIH"] = "UEFA", ["QAT"] = "AFC", ["SUI"] = "UEFA",
        ["BRA"] = "CONMEBOL", ["MAR"] = "CAF", ["HAI"] = "CONCACAF", ["SCO"] = "UEFA",
        ["USA"] = "CONCACAF", ["PAR"] = "CONMEBOL", ["AUS"] = "AFC", ["TUR"] = "UEFA",
        ["GER"] = "UEFA", ["CUW"] = "CONCACAF", ["CIV"] = "CAF", ["ECU"] = "CONMEBOL",
        ["NED"] = "UEFA", ["JPN"] = "AFC", ["SWE"] = "UEFA", ["TUN"] = "CAF",
        ["BEL"] = "UEFA", ["EGY"] = "CAF", ["IRN"] = "AFC", ["NZL"] = "OFC",
        ["ESP"] = "UEFA", ["CPV"] = "CAF", ["KSA"] = "AFC", ["URU"] = "CONMEBOL",
        ["FRA"] = "UEFA", ["SEN"] = "CAF", ["IRQ"] = "AFC", ["NOR"] = "UEFA",
        ["ARG"] = "CONMEBOL", ["ALG"] = "CAF", ["AUT"] = "UEFA", ["JOR"] = "AFC",
        ["POR"] = "UEFA", ["COD"] = "CAF", ["COL"] = "CONMEBOL", ["UZB"] = "AFC",
        ["ENG"] = "UEFA", ["CRO"] = "UEFA", ["GHA"] = "CAF", ["PAN"] = "CONCACAF",
    };

    // FIFA code → ISO2 for flag URL generation
    private static readonly Dictionary<string, string> Iso2Map = new()
    {
        ["MEX"]="MX",["RSA"]="ZA",["KOR"]="KR",["CZE"]="CZ",["CAN"]="CA",["BIH"]="BA",
        ["QAT"]="QA",["SUI"]="CH",["BRA"]="BR",["MAR"]="MA",["HAI"]="HT",["SCO"]="GB-SCT",
        ["USA"]="US",["PAR"]="PY",["AUS"]="AU",["TUR"]="TR",["GER"]="DE",["CUW"]="CW",
        ["CIV"]="CI",["ECU"]="EC",["NED"]="NL",["JPN"]="JP",["SWE"]="SE",["TUN"]="TN",
        ["BEL"]="BE",["EGY"]="EG",["IRN"]="IR",["NZL"]="NZ",["ESP"]="ES",["CPV"]="CV",
        ["KSA"]="SA",["URU"]="UY",["FRA"]="FR",["SEN"]="SN",["IRQ"]="IQ",["NOR"]="NO",
        ["ARG"]="AR",["ALG"]="DZ",["AUT"]="AT",["JOR"]="JO",["POR"]="PT",["COD"]="CD",
        ["COL"]="CO",["UZB"]="UZ",["ENG"]="GB-ENG",["CRO"]="HR",["GHA"]="GH",["PAN"]="PA",
    };

    /// <summary>
    /// Get ISO2 country code from FIFA code (e.g. "MEX" → "MX").
    /// </summary>
    public static string GetIso2(string fifaCode)
    {
        return Iso2Map.GetValueOrDefault(fifaCode.ToUpperInvariant(), "");
    }

    /// <summary>
    /// Load raw PNG bytes for a flag (disk first, then embedded resource fallback).
    /// Shared by WPF UI rendering and PDF export.
    /// </summary>
    public static byte[]? GetFlagPngBytes(string? fifaCode)
    {
        var iso = GetIso2(fifaCode ?? "");
        if (string.IsNullOrEmpty(iso)) return null;

        var dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Flags");
        var path = System.IO.Path.Combine(dir, $"{iso.ToLowerInvariant()}.png");

        // 1. Try file on disk (debug mode)
        if (System.IO.File.Exists(path))
        {
            try { return System.IO.File.ReadAllBytes(path); }
            catch { }
        }

        // 2. Try embedded resource (single-file publish)
        var asm = typeof(FlagHelper).Assembly;
        var resName = $"{asm.GetName().Name}.Resources.Flags.{iso.ToLowerInvariant()}.png";
        using var stream = asm.GetManifestResourceStream(resName);
        if (stream != null)
        {
            try
            {
                using var ms = new System.IO.MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// Create an Image control for a flag from FIFA code.
    /// Falls back to text if image not found.
    /// </summary>
    public static FrameworkElement CreateFlagImage(string? fifaCode, double w = 20, double h = 14)
    {
        var code = fifaCode ?? "?";
        var bytes = GetFlagPngBytes(fifaCode);

        if (bytes != null)
        {
            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = new System.IO.MemoryStream(bytes);
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                return new System.Windows.Controls.Image
                {
                    Width = w, Height = h,
                    Source = bmp,
                    Stretch = System.Windows.Media.Stretch.UniformToFill,
                    Margin = new System.Windows.Thickness(0, 0, 4, 0)
                };
            }
            catch { }
        }

        // Fallback: show colored code text
        var color = GetTeamColor(fifaCode);
        return new System.Windows.Controls.Border
        {
            Background = new System.Windows.Media.SolidColorBrush(color),
            CornerRadius = new System.Windows.CornerRadius(2),
            Padding = new System.Windows.Thickness(3, 1, 3, 1),
            Margin = new System.Windows.Thickness(0, 0, 4, 0),
            Child = new System.Windows.Controls.TextBlock
            {
                Text = code,
                FontSize = 9,
                FontWeight = System.Windows.FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            }
        };
    }

    /// <summary>
    /// Get a color based on the team's confederation.
    /// </summary>
    public static Color GetTeamColor(string? fifaCode)
    {
        if (fifaCode != null && Confederation.TryGetValue(fifaCode, out var conf))
            return ConfederationColors.GetValueOrDefault(conf, Colors.Gray);
        return Colors.Gray;
    }

    /// <summary>
    /// Create a small colored circle + 3-letter code TextBlock for display in lists.
    /// </summary>
    public static FrameworkElement CreateFlagBadge(string? fifaCode, double fontSize = 11)
    {
        var code = fifaCode ?? "??";
        var color = GetTeamColor(fifaCode);

        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        // Colored circle
        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 10, Height = 10,
            Fill = new SolidColorBrush(color),
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(dot);

        // Code text
        var text = new TextBlock
        {
            Text = code,
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(text);

        return panel;
    }
}
