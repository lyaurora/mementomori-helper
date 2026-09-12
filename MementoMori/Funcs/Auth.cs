using MementoMori.Ortega.Share.Data.ApiInterface.Auth;
using MementoMori.Ortega.Share.Data.Auth;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace MementoMori.Funcs;

public partial class MementoMoriFuncs
{
    private readonly IHttpClientFactory _httpClientFactory;
    private int _autoLoginFailures;

    public string? LastLoginError { get; private set; }
    public DateTimeOffset NextAutoLoginAttempt { get; private set; }
    public bool CanAutoLogin => !_loggedOut && !LoginOk && !IsQuickActionExecuting && DateTimeOffset.UtcNow >= NextAutoLoginAttempt;

    public async Task AuthLogin(PlayerDataInfo playerDataInfo)
    {
        _cancellationTokenSource?.Token.ThrowIfCancellationRequested();
        _lastPlayerDataInfo = playerDataInfo;
        LoginOk = false;
        await NetworkManager.Login(playerDataInfo.WorldId, AddLog, OperationCancellation);
        _cancellationTokenSource?.Token.ThrowIfCancellationRequested();
        await UserGetUserData();
        _cancellationTokenSource?.Token.ThrowIfCancellationRequested();
        LoginOk = true;
        await _timeZoneAwareJobRegister.RegisterJobs(UserId);
    }

    public async Task AutoLogin(bool manual = false, CancellationToken cancellationToken = default)
    {
        var logoutVersion = Volatile.Read(ref _logoutVersion);
        if (!manual && !CanAutoLogin) return;
        await ExecuteExclusive(async token =>
        {
            var accountInfo = _accountManager.GetAccountInfo(UserId);
            if (accountInfo == null || (!manual && (!accountInfo.AutoLogin || _loggedOut
                || logoutVersion != Volatile.Read(ref _logoutVersion) || LoginOk))) return;
            Logining = true;
            try
            {
                LastLoginError = null;
                AddLog(ResourceStrings.AutoLoginonStartup);
                var playerDataInfos = await GetPlayerDataInfo();
                var playerDataInfo = accountInfo.AutoLoginWorldId > 0
                    ? playerDataInfos.Find(d => d.WorldId == accountInfo.AutoLoginWorldId)
                    : playerDataInfos.MaxBy(d => d.LastLoginTime);
                if (playerDataInfo == null) throw new InvalidOperationException("No character found in the selected world.");
                await LoginCore(playerDataInfo, null);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                LoginOk = false;
                LastLoginError = e.Message;
                AddLog(e.ToString());
            }
            finally
            {
                Logining = false;
                if (!LoginOk && !token.IsCancellationRequested)
                {
                    _autoLoginFailures = Math.Min(_autoLoginFailures + 1, 5);
                    NextAutoLoginAttempt = DateTimeOffset.UtcNow.AddMinutes(Math.Min(1 << (_autoLoginFailures - 1), 15));
                }
            }
        }, cancellationToken);
    }

    public async Task Logout()
    {
        _loggedOut = true;
        Interlocked.Increment(ref _logoutVersion);
        CancelQuickAction();
        await ExecuteExclusive(async _ =>
        {
            _loggedOut = true;
            LoginOk = false;
            LastLoginError = null;
            NextAutoLoginAttempt = default;
            _autoLoginFailures = 0;
            await _timeZoneAwareJobRegister.DeregisterJobs(UserId);
        });
    }

    public Task Login(PlayerDataInfo playerDataInfo = null, bool? autoLoginThisWorld = null)
    {
        return ExecuteExclusive(_ => LoginCore(playerDataInfo, autoLoginThisWorld));
    }

    private async Task LoginCore(PlayerDataInfo playerDataInfo, bool? autoLoginThisWorld)
    {
        Logining = true;
        try
        {
            LastLoginError = null;
            if (playerDataInfo == null) playerDataInfo = _lastPlayerDataInfo;
            if (playerDataInfo == null) throw new Exception("playerDataInfo is null");
            if (autoLoginThisWorld.HasValue) _AuthOption.Update(d =>
            {
                var account = d.Accounts.Find(x => x.UserId == UserId);
                if (account != null) account.AutoLoginWorldId = autoLoginThisWorld.Value ? playerDataInfo.WorldId : 0;
            });
            await AuthLogin(playerDataInfo);
            _loggedOut = false;
            await GetMyPage();
            await GetMissionInfo();
            await GetBountyRequestInfo();
            _autoLoginFailures = 0;
            NextAutoLoginAttempt = default;
            // await GetMonthlyLoginBonusInfo();
        }
        catch (OperationCanceledException) when (OperationCancellation.IsCancellationRequested)
        {
            LoginOk = false;
            throw;
        }
        catch (Exception e)
        {
            LoginOk = false;
            LastLoginError = e.Message;
            _operationError = e.Message;
            AddLog(e.ToString());
            return;
        }
        finally
        {
            Logining = false;
        }
    }

    public async Task<string> GetClientKey(string password)
    {
        string key = null;
        await ExecuteExclusive(async _ => key = await GetClientKeyCore(password));
        return key;
    }

    private async Task<string> GetClientKeyCore(string password)
    {
        var authToken = await GetAuthToken();
        var createUserResponse = await GetResponse<CreateUserRequest, CreateUserResponse>(new CreateUserRequest
        {
            AdverisementId = Guid.NewGuid().ToString("D"),
            AppVersion = AuthOption.AppVersion,
            CountryCode = "CN",
            DeviceToken = "",
            ModelName = AuthOption.ModelName,
            DisplayLanguage = NetworkManager.LanguageType,
            OSVersion = AuthOption.OSVersion,
            SteamTicket = "",
            AuthToken = authToken
        });
        var getComebackUserDataResponse = await GetResponse<GetComebackUserDataRequest, GetComebackUserDataResponse>(new GetComebackUserDataRequest
        {
            FromUserId = createUserResponse.UserId, Password = password, SnsType = SnsType.OrtegaId, UserId = UserId, AuthToken = authToken
        });
        var comebackUserResponse = await GetResponse<ComebackUserRequest, ComebackUserResponse>(new ComebackUserRequest
        {
            FromUserId = createUserResponse.UserId, OneTimeToken = getComebackUserDataResponse.OneTimeToken, ToUserId = UserId
        });
        return comebackUserResponse.ClientKey;
    }

    private async Task<int> GetAuthToken()
    {
        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            var url = GameConfig.AssetsUrl.TrimEnd('/') + "/AddressableLocalAssets/ScriptableObjects/AuthToken/AuthTokenData.json?v=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var json = await client.GetStringAsync(url, OperationCancellation);
            var token = JObject.Parse(json)["_authToken"]?.Value<int>() ?? throw new InvalidDataException("Missing public AuthToken.");
            _AuthOption.Update(option =>
            {
                option.CachedAuthToken = token;
                option.CachedAuthTokenVersion = option.AppVersion;
            });
            return token;
        }
        catch (OperationCanceledException) when (OperationCancellation.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            if (AuthOption.CachedAuthToken.HasValue && AuthOption.CachedAuthTokenVersion == AuthOption.AppVersion)
            {
                _logger.LogWarning(e, "Using cached public AuthToken for game version {Version}", AuthOption.AppVersion);
                return AuthOption.CachedAuthToken.Value;
            }
            throw new InvalidOperationException("Unable to load the public AuthToken. Check the asset server URL or try again later.", e);
        }
    }
}
