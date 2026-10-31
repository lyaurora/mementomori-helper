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
        get => _managedHeaders["ortegaappversion"];
        set => _managedHeaders["ortegaappversion"] = value;
    }

    public string OrtegaUuid
    {
        get => _managedHeaders["ortegauuid"];
        set => _managedHeaders["ortegauuid"] = value;
    }

    public MeMoriHttpClientHandler()
    {
        _managedHeaders["ortegaaccesstoken"] = "";
        // _managedHeaders["ortegaappversion"] = "2.2.0";
        _managedHeaders["ortegadevicetype"] = "2";
        _managedHeaders["ortegauuid"] = Guid.NewGuid().ToString("N");
        _managedHeaders["accept-encoding"] = "gzip";
        // we advertise gzip, so decode it if the server ever honours it (base handler skips re-adding gzip to accept-encoding)
        AutomaticDecompression = System.Net.DecompressionMethods.GZip;
        _managedHeaders["user-agent"] = "BestHTTP/2 v2.3.0";
    }

    private readonly Dictionary<string, string> _managedHeaders = new();
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken);
        try
        {
            foreach (var pair in _managedHeaders) request.Headers.Add(pair.Key, pair.Value);

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("orteganextaccesstoken", out var headers))
            {
                var ortegaaccesstoken = headers.FirstOrDefault() ?? "";
                if (!string.IsNullOrEmpty(ortegaaccesstoken))
                {
                    _managedHeaders["ortegaaccesstoken"] = ortegaaccesstoken;
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
            var previousCount = _semaphoreSlim.Release();
        }
    }
}