using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AutoCtor;
using Grpc.Net.Client;
using Injectio.Attributes;
using MementoMori.AddressableTools;
using MementoMori.AddressableTools.Catalog;
using MementoMori.Exceptions;
using MementoMori.MagicOnion;
using MementoMori.Option;
using MementoMori.Ortega.Network.MagicOnion.Client;
using MementoMori.Ortega.Share.Data;
using MementoMori.Ortega.Share.Data.ApiInterface;
using MementoMori.Ortega.Share.Data.ApiInterface.Auth;
using MementoMori.Ortega.Share.Data.ApiInterface.User;
using MementoMori.Ortega.Share.Data.Auth;
using MementoMori.Ortega.Share.Master;
using MementoMori.Ortega.Share.Master.Data;
using MessagePack;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json.Linq;

namespace MementoMori;

[RegisterTransient<MementoNetworkManager>]
[AutoConstruct]
public partial class MementoNetworkManager : IDisposable
{
    private const string GameOs = "Android";

    private static Task? _masterDataUpdateTask;
    private static readonly object MasterUpdateLock = new();
    private static readonly SemaphoreSlim MasterDownloadLock = new(1, 1);
    private int _disposed;


    private static readonly AsyncSemaphore asyncSemaphore = new(1);
    private readonly IWritableOptions<AuthOption> _authOption;
    private readonly IWritableOptions<GameConfig> _gameConfig;
    private readonly ILogger<MementoNetworkManager> _logger;


    private Uri _apiAuth;
    private Uri _apiHost;
    private GrpcChannel _grpcChannel;
    private HttpClient _httpClient;

    private LoginRequest _lastLoginRequest;

    private HttpClient _unityHttpClient;
    private string AuthTokenOfMagicOnion;
    private readonly CancellationTokenSource cts = new();
    public TimeManager TimeManager { get; } = new();

    public long UserId { get; set; }
    public long PlayerId { get; set; }
    public PlayerGuildPositionType GuildPositionType { get; internal set; }
    public CultureInfo CultureInfo { get; private set; } = new("zh-CN");
    public LanguageType LanguageType => parseLanguageType(CultureInfo);

    public static string AssetCatalogUriFormat { get; private set; }
    public static string AssetCatalogFixedUriFormat { get; private set; }
    public static string MasterUriFormat { get; private set; }
    public static string NoticeBannerImageUriFormat { get; private set; }
    public static AppAssetVersionInfo AppAssetVersionInfo { get; private set; }

    public MeMoriHttpClientHandler MoriHttpClientHandler { get; private set; }


    public bool DisableAutoUpdateMasterData { get; set; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        cts.Cancel();
        cts.Dispose();
        MoriHttpClientHandler?.Dispose();
        _httpClient?.Dispose();
        _unityHttpClient?.Dispose();
        _grpcChannel?.Dispose();
    }

    [AutoPostConstruct]
    public void AutoPostConstruct()
    {

        _apiAuth = new Uri(string.IsNullOrEmpty(_authOption.Value.AuthUrl) ? "https://prd1-auth.mememori-boi.com/api/" : _authOption.Value.AuthUrl);

        MoriHttpClientHandler = new MeMoriHttpClientHandler {AppVersion = _authOption.Value.AppVersion};
        _httpClient = new HttpClient(MoriHttpClientHandler);
        if (!Debugger.IsAttached) _httpClient.Timeout = TimeSpan.FromSeconds(10);
        _unityHttpClient = new HttpClient();
        if (!Debugger.IsAttached) _unityHttpClient.Timeout = TimeSpan.FromSeconds(30);
        _unityHttpClient.DefaultRequestHeaders.Add("User-Agent", "UnityPlayer/2021.3.10f1 (UnityWebRequest/1.0, libcurl/7.80.0-DEV)");
        _unityHttpClient.DefaultRequestHeaders.Add("X-Unity-Version", "2021.3.10f1");

        lock (MasterUpdateLock)
            _masterDataUpdateTask ??= Task.Run(AutoUpdateMasterData);
    }

    public async Task Initialize(Action<string> log = null, CancellationToken cancellationToken = default)
    {
        var response = await GetResponse<GetDataUriRequest, GetDataUriResponse>(new GetDataUriRequest {CountryCode = "CN"}, log, cancellationToken: cancellationToken);
        AssetCatalogUriFormat = response.AssetCatalogUriFormat;
        AssetCatalogFixedUriFormat = response.AssetCatalogFixedUriFormat;
        MasterUriFormat = response.MasterUriFormat;
        NoticeBannerImageUriFormat = response.NoticeBannerImageUriFormat;
        AppAssetVersionInfo = response.AppAssetVersionInfo;
        if (_authOption.Value.AppVersion != AppAssetVersionInfo.Version)
            _authOption.Update(x => x.AppVersion = AppAssetVersionInfo.Version);
        MoriHttpClientHandler.AppVersion = AppAssetVersionInfo.Version;
    }

    private async Task AutoUpdateMasterData()
    {
        while (!cts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(1), cts.Token);
                if (!DisableAutoUpdateMasterData)
                {
                    _logger.LogInformation("auto updating master data");
                    if (await DownloadMasterCatalog(cancellationToken: cts.Token)) LoadAllMasters();
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                _logger.LogError(e, "error auto update master data");
            }
        }
    }

    public async Task<bool> DownloadMasterCatalog(Action<string> log = null, CancellationToken cancellationToken = default)
    {
        await MasterDownloadLock.WaitAsync(cancellationToken);
        var staged = new List<(string Temporary, string Target)>();
        try
        {
            log ??= Console.WriteLine;
            log(ResourceStrings.Downloading_master_directory___);
            await GetLatestAvailableVersion(cancellationToken);
            var response = await GetResponse<GetDataUriRequest, GetDataUriResponse>(
                new GetDataUriRequest { CountryCode = "CN", UserId = 0 }, cancellationToken: cancellationToken);
            var version = MoriHttpClientHandler.OrtegaMasterVersion;
            var catalogBytes = await _unityHttpClient.GetByteArrayAsync(string.Format(response.MasterUriFormat, version, "master-catalog"), cancellationToken);
            var catalog = MessagePackSerializer.Deserialize<MasterBookCatalog>(catalogBytes);
            Directory.CreateDirectory("Master");
            HashSet<string> languages = ["TextResourceJaJpMB", "TextResourceZhTwMB", "TextResourceEnUsMB", "TextResourceKoKrMB"];
            foreach (var (name, info) in catalog.MasterBookInfoMap)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Regex.IsMatch(name, @"\A[A-Za-z_][A-Za-z0-9_]*\z")) throw new InvalidDataException("Invalid master file name.");
                if (name.StartsWith("TextResource") && !languages.Contains(name)) continue;
                var path = Path.Combine("Master", name);
                if (File.Exists(path) && string.Equals(await CalcFileMd5(path, cancellationToken), info.Hash, StringComparison.OrdinalIgnoreCase)) continue;
                log($"Updating {name}...");
                var bytes = await _unityHttpClient.GetByteArrayAsync(string.Format(response.MasterUriFormat, version, name), cancellationToken);
                if (!Convert.ToHexString(MD5.HashData(bytes)).Equals(info.Hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Master hash mismatch: {name}");
                ValidateMasterData(name, bytes);
                var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
                staged.Add((temporary, path));
                await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            }
            // Publish only after every changed file has downloaded and validated.
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var (temporary, target) in staged) File.Move(temporary, target, true);
            log(ResourceStrings.Download_master_directory_completed);
            return staged.Count > 0;
        }
        finally
        {
            foreach (var (temporary, _) in staged)
                if (File.Exists(temporary)) File.Delete(temporary);
            MasterDownloadLock.Release();
        }
    }

    private static Array? ValidateMasterData(string name, byte[] bytes)
    {
        var type = typeof(CharacterMB).Assembly.GetType($"MementoMori.Ortega.Share.Master.Data.{name}");
        return type == null ? null : (Array)MessagePackSerializer.Deserialize(type.MakeArrayType(), bytes);
    }

    public static bool HasUsableMasterData()
    {
        try
        {
            foreach (var name in new[] { "TimeServerMB", "CharacterMB", "EquipmentMB", "ItemMB", "TextResourceJaJpMB", "TextResourceZhTwMB", "TextResourceEnUsMB", "TextResourceKoKrMB" })
                if (ValidateMasterData(name, File.ReadAllBytes(Path.Combine("Master", name))) is not { Length: > 0 }) return false;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or MessagePackSerializationException) { return false; }
    }

    public void SetCultureInfo(CultureInfo cultureInfo)
    {
        CultureInfo = cultureInfo;
        TextResourceTable.SetLanguageType(parseLanguageType(cultureInfo));
        LoadAllMasters();
    }

    private LanguageType parseLanguageType(CultureInfo cultureInfo)
    {
        return cultureInfo.TwoLetterISOLanguageName switch
        {
            "zh" => LanguageType.zhTW,
            "en" => LanguageType.enUS,
            "ja" => LanguageType.jaJP,
            "ko" => LanguageType.koKR,
            "fr" => LanguageType.frFR,
            "de" => LanguageType.deDE,
            "es" => LanguageType.esMX,
            "pt" => LanguageType.ptBR,
            "th" => LanguageType.thTH,
            "id" => LanguageType.idID,
            "vi" => LanguageType.viVN,
            "ru" => LanguageType.ruRU,
            _ => LanguageType.enUS
        };
    }

    public async Task DownloadAssets(string gameOs, string assetsPath, string assetsTmpPath, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Downloading asset catalog...");
        var name = $"{gameOs}/{MoriHttpClientHandler.OrtegaAssetVersion}.json";
        var assetCatalogUrl = string.Format(AssetCatalogFixedUriFormat, name);
        _logger.LogInformation($"download {assetCatalogUrl}");

        var content = await _unityHttpClient.GetStringAsync(assetCatalogUrl, cancellationToken);
        var catalog = AddressablesJsonParser.FromString(content);
        var locations = catalog.Resources.SelectMany(d => d.Value).Where(d => d.ProviderId == "Ortega.Common.OrtegaAssestBundleProvider").ToList();

        Directory.CreateDirectory(assetsPath);
        Directory.CreateDirectory(assetsTmpPath);
        _logger.LogInformation("Downloading assets...");
        await Parallel.ForEachAsync(locations, cancellationToken, async (location, token) =>
        {
            if (cancellationToken.IsCancellationRequested) return;

            var bundleId = location.PrimaryKey;
            var localPath = Path.Combine(assetsPath, bundleId);
            if (location.Data is ClassJsonObject classJsonObject)
            {
                var json = JObject.Parse(classJsonObject.JsonText);
                var bundleSize = json.GetValue("m_BundleSize").Value<int>();
                var fileinfo = new FileInfo(localPath);
                if (fileinfo.Exists && fileinfo.Length == bundleSize) return;
            }
            else
            {
                if (File.Exists(localPath)) return;
            }

            var bundleUrl = string.Format(AssetCatalogFixedUriFormat, $"{GameOs}/{bundleId}");
            _logger.LogInformation($"download {bundleUrl}");
            var bytes = await ExecWithRetry(async () => await _unityHttpClient.GetByteArrayAsync(bundleUrl, cancellationToken), cancellationToken: cancellationToken);
            var localTmpPath = Path.Combine(assetsTmpPath, bundleId);
            await File.WriteAllBytesAsync(localTmpPath, bytes, cancellationToken);
        });

        _logger.LogInformation("Download assets finished");
    }

    private static async Task<T> ExecWithRetry<T>(Func<Task<T>> func, int retryCount = 10, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            try
            {
                return await func();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                retryCount--;
                if (retryCount <= 0) throw;
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private static async Task<string> CalcFileMd5(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await MD5.HashDataAsync(stream, cancellationToken));
    }

    public async Task<List<PlayerDataInfo>> GetPlayerDataInfoList(LoginRequest loginRequest, Action<string> log = null, CancellationToken cancellationToken = default)
    {
        _lastLoginRequest = loginRequest;
        var authLoginResp = await GetResponse<LoginRequest, LoginResponse>(loginRequest, log, cancellationToken: cancellationToken);
        return authLoginResp.PlayerDataInfoList;
    }

    public async Task Login(long worldId, Action<string> log = null, CancellationToken cancellationToken = default)
    {
        var authLoginResp = await GetResponse<LoginRequest, LoginResponse>(_lastLoginRequest, log, cancellationToken: cancellationToken);
        var playerDataInfo = authLoginResp.PlayerDataInfoList.First(x => x.WorldId == worldId);

        var timeServerId = playerDataInfo.WorldId / 1000;
        var timeServerMb = TimeServerTable.GetById(timeServerId);
        TimeManager.SetTimeServerMb(timeServerMb);

        // get server host
        await SetServerHost(playerDataInfo.WorldId, log, cancellationToken);

        // do login
        var loginPlayerResp = await GetResponse<LoginPlayerRequest, LoginPlayerResponse>(new LoginPlayerRequest
        {
            PlayerId = playerDataInfo.PlayerId, Password = playerDataInfo.Password
        }, log, cancellationToken: cancellationToken);
        PlayerId = playerDataInfo.PlayerId;
        AuthTokenOfMagicOnion = loginPlayerResp.AuthTokenOfMagicOnion;
        GuildPositionType = loginPlayerResp.GuildSyncData?.PlayerGuildPositionType ?? PlayerGuildPositionType.None;
    }

    public async Task SetServerHost(long worldId, Action<string> log = null, CancellationToken cancellationToken = default)
    {
        var resp = await GetResponse<GetServerHostRequest, GetServerHostResponse>(new GetServerHostRequest {WorldId = worldId}, log, cancellationToken: cancellationToken);
        _apiHost = new Uri(resp.ApiHost);
        var channel = GrpcChannel.ForAddress(new Uri($"https://{resp.MagicOnionHost}:{resp.MagicOnionPort}"));
        Interlocked.Exchange(ref _grpcChannel, channel)?.Dispose();
    }

    public OrtegaMagicOnionClient GetOnionClient()
    {
        var ortegaMagicOnionClient = new OrtegaMagicOnionClient(_grpcChannel, PlayerId, AuthTokenOfMagicOnion, new MagicOnionLocalRaidNotificaiton());
        return ortegaMagicOnionClient;
    }

    public async Task<TResp> GetResponse<TReq, TResp>(TReq req, Action<string>? log = null, Action<UserSyncData>? userData = null,
        Uri? apiAuth = null, Uri? apiHost = null, CancellationToken cancellationToken = default)
        where TReq : ApiRequestBase
        where TResp : ApiResponseBase
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
        var token = linkedCts.Token;
        // ponytail: serialize requests across accounts to preserve the current server request rate.
        using var releaser = await asyncSemaphore.EnterAsync(token);
        await Task.Delay(Math.Max(0, _gameConfig.Value.AutoRequestDelay), token);
        apiHost ??= _apiHost;
        apiAuth ??= _apiAuth;
        log ??= Console.WriteLine;
        var authAttr = typeof(TReq).GetCustomAttribute<OrtegaAuthAttribute>();
        var apiAttr = typeof(TReq).GetCustomAttribute<OrtegaApiAttribute>();
        var uri = authAttr != null ? new Uri(apiAuth, authAttr.Uri)
            : apiAttr != null ? new Uri(apiHost ?? throw new InvalidOperationException(ResourceStrings.PleaseLogin), apiAttr.Uri)
            : throw new NotSupportedException();

        try
        {
            for (var attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (req is LoginRequest login) login.AppVersion = MoriHttpClientHandler.AppVersion;
                if (req is CreateUserRequest create) create.AppVersion = MoriHttpClientHandler.AppVersion;
                using var content = new ByteArrayContent(MessagePackSerializer.Serialize(req));
                content.Headers.ContentType = new("application/json") { CharSet = "UTF-8" };
                using var responseMessage = await _httpClient.PostAsync(uri, content, token);
                responseMessage.EnsureSuccessStatusCode();
                await using var stream = await responseMessage.Content.ReadAsStreamAsync(token);
                if (responseMessage.Headers.TryGetValues("ortegastatuscode", out var headers) && headers.FirstOrDefault() != "0")
                {
                    var error = await MessagePackSerializer.DeserializeAsync<ApiErrorResponse>(stream, cancellationToken: token);
                    if (error.ErrorCode == ErrorCode.CommonRequireClientUpdate && attempt == 0)
                    {
                        await GetLatestAvailableVersion(token);
                        continue;
                    }
                    log($"{uri.AbsolutePath}: {TextResourceTable.GetErrorCodeMessage(error.ErrorCode)}");
                    throw new ApiErrorException(error.ErrorCode);
                }
                var response = await MessagePackSerializer.DeserializeAsync<TResp>(stream, cancellationToken: token);
                if (response is IUserSyncApiResponse sync) userData?.Invoke(sync.UserSyncData);
                if (response is IGuildSyncApiResponse guild && guild.GuildSyncData != null)
                    GuildPositionType = guild.GuildSyncData.PlayerGuildPositionType;
                return response;
            }
        }
        catch (OperationCanceledException e) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException(ResourceStrings.Request_timed_out__check_your_network, e);
        }
    }

    private async Task GetLatestAvailableVersion(CancellationToken cancellationToken = default)
    {
        var script = await _unityHttpClient.GetStringAsync("https://mememori-game.com/apps/vars.js", cancellationToken);
        var match = Regex.Match(script, @"/apps/mementomori_(?<version>\d+\.\d+\.\d+)\.apk");
        if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out var version))
            throw new InvalidDataException("The official download page did not provide a valid game version.");
        var appVersion = version.ToString(3);
        if (_authOption.Value.AppVersion != appVersion) _authOption.Update(x => x.AppVersion = appVersion);
        MoriHttpClientHandler.AppVersion = appVersion;
        _logger.LogInformation("Using official game version {Version}", appVersion);
    }
}
