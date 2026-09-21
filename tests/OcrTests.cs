using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YeShunguangPet;

internal static class OcrTests
{
    public static void Run(Action<bool, string> check)
    {
        var image = TextImage("HELLO 123");
        try
        {
            var result = new WindowsOcrService().RecognizeAsync(image).GetAwaiter().GetResult();
            check(result.Text.Contains("123", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(result.Language),
                "Windows OCR recognizes local high-contrast text without a network provider");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("语言包", StringComparison.Ordinal))
        {
            Console.WriteLine("SKIP: Windows OCR language pack is unavailable for the test identity.");
        }
    }

    private static BitmapSource TextImage(string text)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 480, 140));
            drawing.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("en-US"), FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 54, Brushes.Black, 1), new Point(28, 30));
        }
        var image = new RenderTargetBitmap(480, 140, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image;
    }
}
