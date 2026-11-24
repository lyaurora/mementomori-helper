using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using PropertyChanged.SourceGenerator;

namespace MementoMori;

public partial class MeMoriHttpClientHandler : HttpClientHandler
{
    public string OrtegaAccessToken { get; private set; }
    [Notify] private string _ortegaMasterVersion;
    [Notify] private string _ortegaAssetVersion;

    public string AppVersion
    {
        get { lock (_headerLock) return _managedHeaders["ortegaappversion"]; }
        set { lock (_headerLock) _managedHeaders["ortegaappversion"] = value ?? ""; }
    }

    public string OrtegaUuid
    {
        get { lock (_headerLock) return _managedHeaders["ortegauuid"]; }
        set { lock (_headerLock) _managedHeaders["ortegauuid"] = value ?? ""; }
    }

    public MeMoriHttpClientHandler()
    {
        _managedHeaders["ortegaaccesstoken"] = "";
        _managedHeaders["ortegaappversion"] = "";
        _managedHeaders["ortegadevicetype"] = "2";
        _managedHeaders["ortegauuid"] = Guid.NewGuid().ToString("N");
        _managedHeaders["accept-encoding"] = "gzip";
        // we advertise gzip, so decode it if the server ever honours it (base handler skips re-adding gzip to accept-encoding)
        AutomaticDecompression = System.Net.DecompressionMethods.GZip;
        _managedHeaders["user-agent"] = "BestHTTP/2 v2.3.0";
    }

    private readonly object _headerLock = new();
    private readonly Dictionary<string, string> _managedHeaders = new();
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private int _disposed;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0) _semaphoreSlim.Dispose();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken);
        try
        {
            KeyValuePair<string, string>[] snapshot;
            lock (_headerLock) snapshot = _managedHeaders.ToArray();
            foreach (var pair in snapshot) request.Headers.Add(pair.Key, pair.Value);

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("orteganextaccesstoken", out var headers))
            {
                var ortegaaccesstoken = headers.FirstOrDefault() ?? "";
                if (!string.IsNullOrEmpty(ortegaaccesstoken))
                {
                    lock (_headerLock) _managedHeaders["ortegaaccesstoken"] = ortegaaccesstoken;
                    OrtegaAccessToken = ortegaaccesstoken;
                }
            }

            if (response.Headers.TryGetValues("ortegamasterversion", out var headers1))
            {
                var masterVersion = headers1.FirstOrDefault() ?? "";
                OrtegaMasterVersion = masterVersion;
            }

            if (response.Headers.TryGetValues("ortegaassetversion", out var headers2))
            {
                var assetVersion = headers2.FirstOrDefault() ?? "";
                OrtegaAssetVersion = assetVersion;
            }

            return response;
        }
        finally
        {
            try { _semaphoreSlim.Release(); }
            catch (ObjectDisposedException) { }
        }
    }
}