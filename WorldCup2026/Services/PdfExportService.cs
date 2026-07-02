using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QuestPDF.Fluent;

namespace WorldCup2026.Services;

/// <summary>
/// Exports WPF visual elements to PDF via screenshot-to-PDF.
/// </summary>
public class PdfExportService
{
    static PdfExportService()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    /// <summary>
    /// Capture a WPF FrameworkElement as a high-res bitmap and save to a single-page PDF.
    /// </summary>
    public void ExportVisual(string filePath, FrameworkElement element, DateTime timestamp)
    {
        // Force layout update
        element.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = element.DesiredSize;
        element.Arrange(new Rect(0, 0, desired.Width, desired.Height));
        element.UpdateLayout();

        int w = (int)Math.Ceiling(desired.Width);
        int h = (int)Math.Ceiling(desired.Height);
        if (w <= 0 || h <= 0) { w = 1600; h = 900; }
        if (w > 8000) w = 8000;
        if (h > 8000) h = 8000;

        var dpi = 96.0;
        var rtb = new RenderTargetBitmap(w, h, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        byte[] pngBytes;
        using (var ms = new System.IO.MemoryStream())
        {
            encoder.Save(ms);
            pngBytes = ms.ToArray();
        }

        // Build PDF — A3 landscape (420mm × 297mm)
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(420, 297, QuestPDF.Infrastructure.Unit.Millimetre);
                page.Margin(8);
                page.DefaultTextStyle(x => x.FontSize(8));

                page.Header().AlignCenter()
                    .Text($"FIFA World Cup 2026™  —  Knockout Bracket  |  {timestamp:yyyy-MM-dd HH:mm}")
                    .FontColor(QuestPDF.Helpers.Colors.Grey.Medium);

                page.Content().PaddingTop(4).Image(pngBytes).FitArea();
            });
        }).GeneratePdf(filePath);
    }
}
