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
    private const long MaxBytes = 300L * 1024 * 1024;
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();
    private static readonly SemaphoreSlim EvictLock = new(1, 1);

    // Only plain asset paths under the two known prefixes; no "..", no query, no odd characters.
    [GeneratedRegex(@"^Addressable(Convert|Local)Assets/[A-Za-z0-9_\-/]+\.(png|jpg|jpeg|webp)$")]
    private static partial Regex SafePath();

    public static void MapAssetCache(this WebApplication app, string configDirectory)
    {
        var root = Path.Combine(configDirectory, "AssetCache");
        AtlasManager.LocalAssetsUrl = "/assets";

        app.MapGet("/assets/{**path}", async (string path, HttpContext ctx, IWritableOptions<GameConfig> config, IHttpClientFactory http, CancellationToken ct) =>
        {
            if (!SafePath().IsMatch(path) || path.Contains("..")) return Results.NotFound();
            var file = Path.GetFullPath(Path.Combine(root, path));
            if (!file.StartsWith(root + Path.DirectorySeparatorChar)) return Results.NotFound();

            if (!File.Exists(file))
            {
                using var resp = await http.CreateClient().GetAsync($"{config.Value.AssetsUrl.TrimEnd('/')}/{path}", ct);
                if (!resp.IsSuccessStatusCode) return Results.StatusCode((int)resp.StatusCode);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var tmp = $"{file}.{Guid.NewGuid():N}.tmp";
                await using (var fs = File.Create(tmp)) await resp.Content.CopyToAsync(fs, ct);
                File.Move(tmp, file, true);
                _ = Task.Run(() => Evict(root));
            }
            else
            {
                File.SetLastAccessTimeUtc(file, DateTime.UtcNow); // relatime/noatime mounts don't update atime on read
            }

            ctx.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(file, ContentTypes.TryGetContentType(file, out var type) ? type : "application/octet-stream");
        });
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
