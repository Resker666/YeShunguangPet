using System;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YeShunguangPet;

internal static class SpriteThreadTests
{
    public static void Run(Action<bool, string> check, string source, string temporary)
    {
        foreach (var folder in new[] { "YeShunguang", "robin" })
        {
            var path = Path.Combine(source, "Pets", folder, "pet.json");
            if (!File.Exists(path)) continue;
            var pngPath = Path.Combine(Path.GetDirectoryName(path)!, "spritesheet.png");
            var bytes = File.ReadAllBytes(pngPath);
            var hash = SHA256.HashData(bytes);
            var package = OnWorker(() => PetPackage.Load(path), ApartmentState.MTA);
            check(package.SpriteSheet.IsFrozen, "background-first sprite is frozen: " + folder);
            // Request an uncached crop only after its decoding thread has exited.
            var frame = package.GetFrame(0, 1);
            check(frame.IsFrozen && frame.PixelWidth == package.Manifest.CellWidth, "background decode then UI crop survives owner-thread exit: " + folder);
            var workerFrame = OnWorker(() => package.GetFrame(1, 1), ApartmentState.STA);
            check(workerFrame.IsFrozen && Pixels(workerFrame).Length > 0, "UI can consume a new crop from another STA: " + folder);
            var again = OnWorker(() => PetPackage.Load(path), ApartmentState.MTA);
            check(ReferenceEquals(package.SpriteSheet, again.SpriteSheet), "cross-thread cache hits preserve one shared decoded sheet: " + folder);
            var shared = OnWorker(() => again.GetFrame(0, 1), ApartmentState.MTA);
            check(ReferenceEquals(frame, shared), "cross-thread frame hits reuse the same frozen crop: " + folder);
            using var stream = new MemoryStream(bytes, false);
            var expected = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            check(package.SpriteSheet.Format == expected.Format && package.SpriteSheet.DpiX == expected.DpiX && package.SpriteSheet.DpiY == expected.DpiY &&
                  Pixels(package.SpriteSheet).SequenceEqual(Pixels(expected)), "thread-safe sheet preserves original pixel format, DPI and bytes: " + folder);
            var allMatch = true;
            for (var row = 0; row < package.Manifest.Rows; row++)
                for (var column = 0; column < package.Manifest.Columns; column++)
                {
                    var crop = package.GetFrame(row, column);
                    var rectangle = new Int32Rect(column * package.Manifest.CellWidth, row * package.Manifest.CellHeight, package.Manifest.CellWidth, package.Manifest.CellHeight);
                    var stride = (rectangle.Width * expected.Format.BitsPerPixel + 7) / 8;
                    var reference = new byte[stride * rectangle.Height];
                    expected.CopyPixels(rectangle, reference, stride, 0);
                    allMatch &= crop.IsFrozen && Pixels(crop).SequenceEqual(reference);
                }
            check(allMatch, "all sprite cells preserve original pixels across threads: " + folder);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var scan = OnWorker(() => new PetCatalog(Path.Combine(source, "Pets"), Path.Combine(temporary, "thread-users")).Scan());
            check(scan.Errors.Count == 0 && scan.Pets.All(p => p.Thumbnail is { IsFrozen: true }), "background catalog thumbnails remain usable after collection: " + folder);
            check(SHA256.HashData(File.ReadAllBytes(pngPath)).SequenceEqual(hash), "threading regression leaves original PNG bytes untouched: " + folder);
            GC.KeepAlive(package); GC.KeepAlive(again);
        }
        VerifyPixelFormats(check, temporary);
    }

    private static void VerifyPixelFormats(Action<bool, string> check, string temporary)
    {
        foreach (var format in new[] { PixelFormats.Bgra32, PixelFormats.Bgr24, PixelFormats.Gray8, PixelFormats.Indexed1, PixelFormats.Indexed8, PixelFormats.Rgba64 })
        {
            var directory = Path.Combine(temporary, "thread-format-" + format);
            Directory.CreateDirectory(directory);
            var palette = format == PixelFormats.Indexed1 || format == PixelFormats.Indexed8
                ? new BitmapPalette(new[] { Colors.Transparent, Colors.Crimson }) : null;
            var stride = (34 * format.BitsPerPixel + 7) / 8;
            var pixels = new byte[stride * 17];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 2);
            var bitmap = BitmapSource.Create(34, 17, 120, 144, format, palette, pixels, stride);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var pngPath = Path.Combine(directory, "spritesheet.png");
            using (var output = File.Create(pngPath)) encoder.Save(output);
            var path = Path.Combine(directory, "pet.json");
            File.WriteAllText(path, """
                {"schemaVersion":1,"id":"thread-test","name":"Thread test","description":"","cellWidth":17,"cellHeight":17,"columns":2,"rows":1,"animations":{"idle":{"row":0,"startColumn":0,"durationsMs":[100,100],"loop":true}}}
                """);
            var uiFirst = format == PixelFormats.Bgr24 || format == PixelFormats.Indexed8;
            var package = uiFirst ? PetPackage.Load(path) : OnWorker(() => PetPackage.Load(path));
            using var input = File.OpenRead(pngPath);
            var reference = new PngBitmapDecoder(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var frame = uiFirst ? OnWorker(() => package.GetFrame(0, 1)) : package.GetFrame(0, 1);
            var outputStride = (17 * reference.Format.BitsPerPixel + 7) / 8;
            var expected = new byte[outputStride * 17];
            reference.CopyPixels(new Int32Rect(17, 0, 17, 17), expected, outputStride, 0);
            MaskPadding(expected, 17, 17, reference.Format.BitsPerPixel);
            check(package.SpriteSheet.Format == reference.Format && frame.Format == reference.Format, "decoded sheet and crop format preserved: " + format);
            check(Pixels(package.SpriteSheet).SequenceEqual(Pixels(reference)), "decoded sheet pixel bits preserved: " + format);
            check(Pixels(frame).SequenceEqual(expected), "non-byte-aligned crop pixel bits preserved: " + format);
            check(package.SpriteSheet.Palette is null ? reference.Palette is null : package.SpriteSheet.Palette.Colors.SequenceEqual(reference.Palette.Colors),
                "decoded palette preserved: " + format);
        }
    }

    private static byte[] Pixels(BitmapSource source)
    {
        var stride = checked((source.PixelWidth * source.Format.BitsPerPixel + 7) / 8);
        var result = new byte[checked(stride * source.PixelHeight)];
        source.CopyPixels(result, stride, 0);
        MaskPadding(result, source.PixelWidth, source.PixelHeight, source.Format.BitsPerPixel);
        return result;
    }

    private static void MaskPadding(byte[] pixels, int width, int height, int bitsPerPixel)
    {
        // WIC does not promise values for unused bits at the end of an indexed row.
        var bits = width * bitsPerPixel;
        if (bits % 8 == 0) return;
        var stride = (bits + 7) / 8;
        var mask = (byte)(0xFF << (8 - bits % 8));
        for (var row = 0; row < height; row++) pixels[(row + 1) * stride - 1] &= mask;
    }

    internal static T OnWorker<T>(Func<T> action, ApartmentState apartment = ApartmentState.MTA)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() => { try { result = action(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
        thread.SetApartmentState(apartment);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) throw new TimeoutException("Sprite worker did not finish.");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
        return result;
    }
}
