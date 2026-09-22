using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace YeShunguangPet;

public sealed record OcrText(string Text, string Language);

public interface IOcrService
{
    Task<OcrText> RecognizeAsync(BitmapSource image, CancellationToken cancellationToken = default);
}

public sealed class WindowsOcrService : IOcrService
{
    public async Task<OcrText> RecognizeAsync(BitmapSource image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        CaptureRaster.ValidateSize(image.PixelWidth, image.PixelHeight);
        cancellationToken.ThrowIfCancellationRequested();
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("Windows 没有可用的 OCR 语言包，请先在系统语言设置中安装文字识别语言。");
        var prepared = ScaleToOcrLimit(image);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(prepared));
        using var memory = new MemoryStream(); encoder.Save(memory);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(memory.ToArray());
            await writer.StoreAsync().AsTask(cancellationToken);
            await writer.FlushAsync().AsTask(cancellationToken);
        }
        stream.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore).AsTask(cancellationToken);
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken);
        var text = string.Join(Environment.NewLine, result.Lines.Select(line => line.Text)).Trim();
        return new OcrText(text, engine.RecognizerLanguage?.DisplayName ?? engine.RecognizerLanguage?.LanguageTag ?? "系统默认");
    }

    private static BitmapSource ScaleToOcrLimit(BitmapSource image)
    {
        var limit = OcrEngine.MaxImageDimension;
        var largest = Math.Max(image.PixelWidth, image.PixelHeight);
        if (largest <= limit) return image;
        var scale = limit / (double)largest;
        var transformed = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        transformed.Freeze();
        return transformed;
    }
}
