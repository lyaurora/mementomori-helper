using System.Text.RegularExpressions;
using MementoMori.Option;
using MementoMori.WebUI.UI;
using Microsoft.AspNetCore.StaticFiles;

namespace MementoMori.WebUI.Services;

/// <summary>
/// GET /assets/{**path}: fetch a game image from the configured resource server once, keep it on disk, serve it immutable for a year.
/// </summary>
public static partial class AssetCache
{
    private sealed class PathLockEntry
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public int RefCount;
    }

    private const long MaxBytes = 300L * 1024 * 1024;
    private static readonly TimeSpan UpstreamTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan AtimeUpdateInterval = TimeSpan.FromHours(1);
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();
    private static readonly SemaphoreSlim EvictLock = new(1, 1);
    private static readonly Dictionary<string, PathLockEntry> PathLocks = new(StringComparer.Ordinal);

    internal static int ActivePathLockCount
    {
        get { lock (PathLocks) return PathLocks.Count; }
    }

    // Only plain asset paths under the two known prefixes; no "..", no query, no odd characters.
    [GeneratedRegex(@"^Addressable(Convert|Local)Assets/[A-Za-z0-9_\-/]+\.(png|jpg|jpeg|webp)$")]
    private static partial Regex SafePath();

    public static void MapAssetCache(this WebApplication app, string configDirectory, string pathBase = "")
    {
        var root = Path.Combine(configDirectory, "AssetCache");
        var normalizedBase = string.IsNullOrWhiteSpace(pathBase) ? "" : "/" + pathBase.Trim().Trim('/');
        AtlasManager.LocalAssetsUrl = $"{normalizedBase}/assets";

        app.MapGet("/assets/{**path}", async (string path, HttpContext ctx, IWritableOptions<GameConfig> config, IHttpClientFactory http, CancellationToken ct) =>
        {
            if (!SafePath().IsMatch(path) || path.Contains("..")) return Results.NotFound();
            var file = Path.GetFullPath(Path.Combine(root, path));
            if (!file.StartsWith(root + Path.DirectorySeparatorChar)) return Results.NotFound();

            var status = await EnsureCachedAsync(root, file, $"{config.Value.AssetsUrl.TrimEnd('/')}/{path}", http, ct);
            if (status != StatusCodes.Status200OK) return Results.StatusCode(status);

            ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(file, ContentTypes.TryGetContentType(file, out var type) ? type : "application/octet-stream");
        });
    }

    internal static async Task<int> EnsureCachedAsync(string root, string file, string upstreamUrl, IHttpClientFactory http, CancellationToken ct)
    {
        if (File.Exists(file))
        {
            TouchAccessTime(file);
            return StatusCodes.Status200OK;
        }

        PathLockEntry entry;
        lock (PathLocks)
        {
            if (!PathLocks.TryGetValue(file, out entry!))
                PathLocks[file] = entry = new PathLockEntry();
            entry.RefCount++;
        }

        var entered = false;
        try
        {
            await entry.Gate.WaitAsync(ct);
            entered = true;

            if (File.Exists(file))
            {
                TouchAccessTime(file);
                return StatusCodes.Status200OK;
            }

            var tmp = $"{file}.{Guid.NewGuid():N}.tmp";
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(UpstreamTimeout);
            try
            {
                using var resp = await http.CreateClient().GetAsync(upstreamUrl, timeoutCts.Token);
                if (!resp.IsSuccessStatusCode) return (int)resp.StatusCode;
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await using (var fs = File.Create(tmp)) await resp.Content.CopyToAsync(fs, timeoutCts.Token);
                File.Move(tmp, file, true);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return StatusCodes.Status504GatewayTimeout;
            }
            catch (Exception e) when (e is HttpRequestException or IOException && !ct.IsCancellationRequested)
            {
                // resource server dropped the connection: answer 502 instead of an unhandled 500
                return StatusCodes.Status502BadGateway;
            }
            finally
            {
                if (File.Exists(tmp))
                {
                    try { File.Delete(tmp); } catch (IOException) { }
                }
            }

            _ = Task.Run(() => Evict(root));
            return StatusCodes.Status200OK;
        }
        finally
        {
            if (entered) entry.Gate.Release();
            var disposeGate = false;
            lock (PathLocks)
            {
                entry.RefCount--;
                if (entry.RefCount == 0)
                {
                    PathLocks.Remove(file);
                    disposeGate = true;
                }
            }
            if (disposeGate) entry.Gate.Dispose();
        }
    }

    private static void TouchAccessTime(string file)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (now - File.GetLastAccessTimeUtc(file) > AtimeUpdateInterval)
                File.SetLastAccessTimeUtc(file, now); // relatime/noatime mounts don't update atime on read
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ponytail: full directory scan after every miss; fine for a few thousand icons, keep a running size if the cache grows past that.
    private static async Task Evict(string root)
    {
        if (!await EvictLock.WaitAsync(0)) return;
        try
        {
            var files = new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories).OrderBy(f => f.LastAccessTimeUtc).ToList();
            var total = files.Sum(f => f.Length);
            foreach (var f in files)
            {
                if (total <= MaxBytes) break;
                total -= f.Length;
                try { f.Delete(); } catch (IOException) { }
            }
        }
        finally { EvictLock.Release(); }
    }
}
