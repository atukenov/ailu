using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace Ailu.Api.Infrastructure;

// Where photo files live. Keys are built from ids only, so a key can never point outside a couple's folder.
// Production will use an S3-compatible bucket in a Kazakhstan data center (privacy doc); the disk store
// is enough for a single-server closed test.
//
// Two storage classes (roadmap, "cold storage after a year"): Standard, and Archive at about half the
// price, which opens a little slower. Reads find a file in either class. On S3 the archive is an
// instant-retrieval class (moving is a copy onto itself with the new class); a class that needs a
// restore before reading would break opening old memories.
public enum StorageTier { Standard, Archive }

public sealed record StoredFolder(string Key, DateTimeOffset ModifiedAt);

public interface IPhotoStore
{
    Task PutAsync(string key, byte[] data, StorageTier tier = StorageTier.Standard, CancellationToken ct = default);
    Task<Stream?> OpenAsync(string key, CancellationToken ct = default);

    // Moves one file to the archive class. False when there is no such file (a size never generated).
    Task<bool> ArchiveAsync(string key, CancellationToken ct = default);

    // Removes the folder from every class, so a deleted memory leaves nothing behind in the archive.
    Task DeleteFolderAsync(string folder, CancellationToken ct = default);

    // Every photo folder (couples/{couple}/{photo}) in any class, with its last change. Used to find files
    // no row points to any more; it walks the whole store, so it runs rarely.
    IAsyncEnumerable<StoredFolder> ListPhotoFoldersAsync(CancellationToken ct = default);
}

// Local stub until the provider is chosen: the archive class is a second directory tree beside the first.
public sealed class DiskPhotoStore(string root, string archiveRoot) : IPhotoStore
{
    public DiskPhotoStore(string root) : this(root, root.TrimEnd(Path.DirectorySeparatorChar) + "-archive") { }

    public async Task PutAsync(string key, byte[] data, StorageTier tier = StorageTier.Standard, CancellationToken ct = default)
    {
        var path = PathOf(Root(tier), key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Write then rename, so a reader never sees half a file.
        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(tmp, data, ct);
        File.Move(tmp, path, overwrite: true);
        // One key lives in one class only.
        var other = PathOf(Root(tier == StorageTier.Standard ? StorageTier.Archive : StorageTier.Standard), key);
        if (File.Exists(other)) File.Delete(other);
    }

    public Task<Stream?> OpenAsync(string key, CancellationToken ct = default)
    {
        foreach (var r in (string[])[root, archiveRoot])
        {
            var path = PathOf(r, key);
            if (File.Exists(path)) return Task.FromResult<Stream?>(File.OpenRead(path));
        }
        return Task.FromResult<Stream?>(null);
    }

    public Task<bool> ArchiveAsync(string key, CancellationToken ct = default)
    {
        var from = PathOf(root, key);
        var to = PathOf(archiveRoot, key);
        if (!File.Exists(from)) return Task.FromResult(File.Exists(to));
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(from, to, overwrite: true);
        return Task.FromResult(true);
    }

    public Task DeleteFolderAsync(string folder, CancellationToken ct = default)
    {
        foreach (var r in (string[])[root, archiveRoot])
        {
            var path = PathOf(r, folder);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<StoredFolder> ListPhotoFoldersAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var seen = new Dictionary<string, DateTimeOffset>();
        foreach (var r in (string[])[root, archiveRoot])
        {
            var couples = PathOf(r, PhotoKeys.Root);
            if (!Directory.Exists(couples)) continue;
            foreach (var dir in Directory.EnumerateDirectories(couples).SelectMany(Directory.EnumerateDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var key = Path.GetRelativePath(r, dir).Replace(Path.DirectorySeparatorChar, '/');
                var files = Directory.EnumerateFiles(dir).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(Directory.GetLastWriteTimeUtc(dir));
                var modified = new DateTimeOffset(files.Max(), TimeSpan.Zero);
                seen[key] = seen.TryGetValue(key, out var other) && other > modified ? other : modified;
            }
        }
        foreach (var (key, modified) in seen) yield return new StoredFolder(key, modified);
        await Task.CompletedTask;
    }

    private string Root(StorageTier tier) => tier == StorageTier.Archive ? archiveRoot : root;

    private static string PathOf(string root, string key) => Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
}

public static class PhotoKeys
{
    public const string Root = "couples";

    public static string Folder(Guid coupleId, Guid photoId) => $"{Root}/{coupleId:N}/{photoId:N}";
    public static string File(Guid coupleId, Guid photoId, int size) => $"{Folder(coupleId, photoId)}/{size}.jpg";

    // The photo id a folder key names, or null for anything else in the store.
    public static Guid? PhotoOf(string folder) =>
        folder.Split('/') is [Root, var couple, var photo] && Guid.TryParseExact(couple, "N", out _) &&
        Guid.TryParseExact(photo, "N", out var id) ? id : null;
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

    private string Sign(Guid photoId, int size, long expires) =>
        Base64Url.EncodeToString(HMACSHA256.HashData(key, Encoding.ASCII.GetBytes($"{photoId:N}:{size}:{expires}")))[..32];
}
