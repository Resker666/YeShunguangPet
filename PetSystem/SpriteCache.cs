using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media.Imaging;

namespace YeShunguangPet;

internal static class SpriteCache
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, WeakReference<BitmapSource>> Images = new();
    private static readonly Queue<string> ImageOrder = new();
    private static readonly ConditionalWeakTable<BitmapSource, ImageIdentity> Identities = new();
    private static readonly Dictionary<string, PinnedImage> Pinned = new();
    private static readonly ConditionalWeakTable<BitmapSource, Frames> FrameTables = new();

    public static BitmapSource Decode(byte[] png, Func<BitmapSource> decode)
    {
        var key = Convert.ToHexString(SHA256.HashData(png));
        lock (Gate)
        {
            if (Pinned.TryGetValue(key, out var pinned)) return pinned.Image;
            if (Images.TryGetValue(key, out var reference) && reference.TryGetTarget(out var image)) return image;
            image = decode();
            Identities.Add(image, new ImageIdentity(key));
            if (!Images.ContainsKey(key)) ImageOrder.Enqueue(key);
            Images[key] = new WeakReference<BitmapSource>(image);
            while (ImageOrder.Count > 64) Images.Remove(ImageOrder.Dequeue());
            return image;
        }
    }

    public static BitmapSource Frame(BitmapSource sheet, Int32Rect rectangle)
    {
        var key = (rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
        var cache = FrameTables.GetValue(sheet, _ => new Frames());
        lock (cache)
        {
            if (cache.Items.TryGetValue(key, out var frame)) return frame;
            frame = new CroppedBitmap(sheet, rectangle);
            frame.Freeze();
            cache.Items.Add(key, frame);
            cache.Order.Enqueue(key);
            if (cache.Order.Count > 256) cache.Items.Remove(cache.Order.Dequeue());
            return frame;
        }
    }

    private sealed class Frames
    {
        public readonly Dictionary<(int, int, int, int), BitmapSource> Items = new();
        public readonly Queue<(int, int, int, int)> Order = new();
    }

    public static int ActiveLeases { get { lock (Gate) return System.Linq.Enumerable.Sum(Pinned.Values, p => p.Count); } }
    public static IDisposable Retain(BitmapSource image)
    {
        lock (Gate)
        {
            var key = Identities.GetValue(image, _ => throw new InvalidOperationException("Unknown decoded image.")).Key;
            if (!Pinned.TryGetValue(key, out var pinned)) Pinned.Add(key, pinned = new PinnedImage(image));
            pinned.Count++;
            return new ImageLease(key);
        }
    }

    private sealed record ImageIdentity(string Key);
    private sealed class PinnedImage
    {
        public BitmapSource Image { get; }
        public int Count;
        public PinnedImage(BitmapSource image) => Image = image;
    }
    private sealed class ImageLease : IDisposable
    {
        private string? _key;
        public ImageLease(string key) => _key = key;
        public void Dispose()
        {
            lock (Gate)
            {
                if (_key is null) return;
                if (--Pinned[_key].Count == 0) Pinned.Remove(_key);
                _key = null;
            }
        }
    }
}
