using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace Ailu.Api.Infrastructure;

// Where photo files live. Keys are built from ids only, so a key can never point outside a couple's folder.
// Production will use an S3-compatible bucket in a Kazakhstan data center (privacy doc); the disk store
// is enough for a single-server closed test.
public interface IPhotoStore
{
    Task PutAsync(string key, byte[] data, CancellationToken ct = default);
    Task<Stream?> OpenAsync(string key, CancellationToken ct = default);
    Task DeleteFolderAsync(string folder, CancellationToken ct = default);
}

public sealed class DiskPhotoStore(string root) : IPhotoStore
{
    public async Task PutAsync(string key, byte[] data, CancellationToken ct = default)
    {
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Write then rename, so a reader never sees half a file.
        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(tmp, data, ct);
        File.Move(tmp, path, overwrite: true);
    }

    public Task<Stream?> OpenAsync(string key, CancellationToken ct = default)
    {
        var path = PathOf(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    public Task DeleteFolderAsync(string folder, CancellationToken ct = default)
    {
        var path = PathOf(folder);
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        return Task.CompletedTask;
    }

    private string PathOf(string key) => Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
}

public static class PhotoKeys
{
    public static string Folder(Guid coupleId, Guid photoId) => $"couples/{coupleId:N}/{photoId:N}";
    public static string File(Guid coupleId, Guid photoId, int size) => $"{Folder(coupleId, photoId)}/{size}.jpg";

    // A finished video story of the year sits with the couple's photos.
    public static string Story(Guid coupleId, Guid storyId) => $"couples/{coupleId:N}/stories/{storyId:N}.mp4";
}

public sealed record ProcessedPhoto(byte[] Jpeg, int Width, int Height);

// The app compresses before upload (2 560 px, JPEG 82, per the roadmap). The server does it again,
// so a client that skipped it can't eat the quota, and so EXIF (with the location) is never stored:
// re-encoding writes pixels only.
public sealed class PhotoProcessor
{
    public const int Full = 2560, Screen = 1080, Thumb = 320;
    public static readonly int[] Sizes = [Thumb, Screen, Full];

    private const long MaxPixels = 50_000_000;
    private static readonly SKEncodedImageFormat[] Accepted =
        [SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Png, SKEncodedImageFormat.Webp, SKEncodedImageFormat.Heif];

    // Decoding a large photo takes a couple of hundred MB; two at a time keeps a small server alive.
    private readonly SemaphoreSlim gate = new(2);

    public async Task<ProcessedPhoto?> NormalizeAsync(Stream input, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        await gate.WaitAsync(ct);
        try
        {
            using var codec = SKCodec.Create(buffer);
            if (codec is null || !Accepted.Contains(codec.EncodedFormat)) return null;
            if ((long)codec.Info.Width * codec.Info.Height > MaxPixels) return null;

            using var decoded = SKBitmap.Decode(codec, codec.Info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Premul));
            if (decoded is null) return null;
            using var upright = Orient(decoded, codec.EncodedOrigin);
            using var sized = Fit(upright, Full);
            return new ProcessedPhoto(Jpeg(sized, 82), sized.Width, sized.Height);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<byte[]> ResizeAsync(Stream full, int size, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await full.CopyToAsync(buffer, ct);
        buffer.Position = 0;

        await gate.WaitAsync(ct);
        try
        {
            using var decoded = SKBitmap.Decode(buffer) ?? throw new InvalidDataException("Stored photo can't be decoded.");
            using var sized = Fit(decoded, size);
            return Jpeg(sized, 80);
        }
        finally
        {
            gate.Release();
        }
    }

    private static byte[] Jpeg(SKBitmap bitmap, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    // Returns a new bitmap no larger than `max` on its long side (a copy even when it already fits,
    // so callers can always dispose what they get).
    private static SKBitmap Fit(SKBitmap source, int max)
    {
        var scale = Math.Min(1.0, (double)max / Math.Max(source.Width, source.Height));
        var info = new SKImageInfo(Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)), SKColorType.Rgba8888, SKAlphaType.Premul);
        return source.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell))
               ?? throw new InvalidOperationException("Resize failed.");
    }

    // Phones store photos sideways and record the rotation in EXIF; since EXIF is dropped, apply it to the pixels.
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var (w, h) = (source.Width, source.Height);
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? h : w, swap ? w : h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(result);
        var m = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity
        };
        canvas.SetMatrix(m);
        canvas.DrawBitmap(source, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
        return result;
    }
}

// Photos are fetched by plain <img>-style requests, which can't carry the bearer token, so each URL is
// signed instead. A URL stays the same for a whole hour, so the app's image cache can reuse it, and
// works for one to two hours. The same scheme moves to a CDN later without changing the app.
public sealed class PhotoUrls(AuthOptions auth, TimeProvider clock)
{
    private readonly byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes("photos:" + auth.SigningKey));

    public string For(Guid photoId, int size)
    {
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var expires = (now / 3600 + 2) * 3600;
        return $"/v1/photos/{photoId:N}/{size}?exp={expires}&sig={Sign(photoId, size, expires)}";
    }

    public bool IsValid(Guid photoId, int size, long expires, string? sig) =>
        sig is not null && expires >= clock.GetUtcNow().ToUnixTimeSeconds() &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(photoId, size, expires)), Encoding.ASCII.GetBytes(sig));

    // The year's video story, for downloading. Signed the same way, under its own prefix.
    public string ForVideo(Guid storyId)
    {
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var expires = (now / 3600 + 2) * 3600;
        return $"/v1/stories/{storyId:N}/video?exp={expires}&sig={SignVideo(storyId, expires)}";
    }

    public bool IsValidVideo(Guid storyId, long expires, string? sig) =>
        sig is not null && expires >= clock.GetUtcNow().ToUnixTimeSeconds() &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(SignVideo(storyId, expires)), Encoding.ASCII.GetBytes(sig));

    private string SignVideo(Guid storyId, long expires) =>
        Base64Url.EncodeToString(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes($"video:{storyId:N}:{expires}")))[..32];

    private string Sign(Guid photoId, int size, long expires) =>
        Base64Url.EncodeToString(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes($"{photoId:N}:{size}:{expires}")))[..32];
}
