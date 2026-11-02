using MementoMori.Exceptions;
using MementoMori.Ortega.Share.Data.ApiInterface.Auth;
using MementoMori.Ortega.Share.Data.Auth;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace MementoMori.Funcs;

public partial class MementoMoriFuncs
{
    private readonly IHttpClientFactory _httpClientFactory;
    private int _autoLoginFailures;
    private bool _autoLoginStopped;

    public string? LastLoginError { get; private set; }
    public DateTimeOffset NextAutoLoginAttempt { get; private set; }
    public bool IsPaused => _accountManager.GetAccountInfo(UserId)?.PauseUntil > DateTimeOffset.UtcNow;
    public bool CanAutoLogin => !IsPaused && !_loggedOut && !LoginOk && !_autoLoginStopped && !IsQuickActionExecuting && DateTimeOffset.UtcNow >= NextAutoLoginAttempt;

    public async Task AuthLogin(PlayerDataInfo playerDataInfo)
    {
        _cancellationTokenSource?.Token.ThrowIfCancellationRequested();
        _lastPlayerDataInfo = playerDataInfo;
        LoginOk = false;
        if (_chat != null) await _chat.ResetAsync();
        await NetworkManager.Login(playerDataInfo.WorldId, AddLog, OperationCancellation);
        _cancellationTokenSource?.Token.ThrowIfCancellationRequested();
        await UserGetUserData();
        _cancellationTokenSource?.Token.ThrowIfCancellationRequested();
        LoginOk = true;
        _sessionLost = false;
        _loginGameDay = GameDay;
        // The cached character list keeps its old LastLoginTime; mark this world as the latest so "most recent" picks it.
        if (_playerDataInfos?.Find(d => d.WorldId == playerDataInfo.WorldId) is { } cached)
            cached.LastLoginTime = _playerDataInfos.Max(d => d.LastLoginTime) + 1;
        await _timeZoneAwareJobRegister.RegisterJobs(UserId);
        _chat?.Resume();
    }

    public async Task AutoLogin(bool manual = false, CancellationToken cancellationToken = default)
    {
        var logoutVersion = Volatile.Read(ref _logoutVersion);
        if (!manual && !CanAutoLogin) return;
        await ExecuteExclusive(async token =>
        {
            var accountInfo = _accountManager.GetAccountInfo(UserId);
            if (accountInfo == null || (!manual && (!accountInfo.AutoLogin || IsPaused || _loggedOut
                || logoutVersion != Volatile.Read(ref _logoutVersion) || LoginOk))) return;
            Logining = true;
            try
            {
                LastLoginError = null;
                AddLog(manual ? ResourceStrings.Login : ResourceStrings.AutoLoginonStartup);
                var playerDataInfos = await GetPlayerDataInfo();
                var playerDataInfo = accountInfo.AutoLoginWorldId > 0
                    ? playerDataInfos.Find(d => d.WorldId == accountInfo.AutoLoginWorldId)
                    : playerDataInfos.MaxBy(d => d.LastLoginTime);
                if (playerDataInfo == null) throw new InvalidOperationException(ResourceStrings.NoCharacterInSelectedWorld);
                await LoginCore(playerDataInfo, null);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                LoginOk = false;
                if (_chat != null) await _chat.ResetAsync();
                LastLoginError = e.Message;
                // The account itself is rejected (missing/deleted user, bad client key): retrying cannot help, wait for a manual login.
                _autoLoginStopped = e is ApiErrorException { ErrorCode: ErrorCode.AuthNotFoundUserAccountDto or ErrorCode.AuthLoginInvalidRequest
                    or ErrorCode.AuthLoginAlreadyDeletedUser };
                _logger.LogError(e, "Auto login failed");
                AddError(ErrorText(e));
            }
            finally
            {
                Logining = false;
                if (!LoginOk && !token.IsCancellationRequested)
                {
                    _autoLoginFailures = Math.Min(_autoLoginFailures + 1, 5);
                    NextAutoLoginAttempt = _autoLoginStopped ? default : DateTimeOffset.UtcNow.AddMinutes(Math.Min(1 << (_autoLoginFailures - 1), 15));
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
            if (_chat != null) await _chat.ResetAsync();
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
            _autoLoginStopped = false;
            NextAutoLoginAttempt = default;
            // await GetMonthlyLoginBonusInfo();
        }
        catch (OperationCanceledException) when (OperationCancellation.IsCancellationRequested)
        {
            LoginOk = false;
            if (_chat != null) await _chat.ResetAsync();
            throw;
        }
        catch (Exception e)
        {
            LoginOk = false;
            if (_chat != null) await _chat.ResetAsync();
            LastLoginError = e.Message;
            _operationError = e.Message;
            _logger.LogError(e, "Login failed");
            AddError(ErrorText(e));
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
        CreateUserResponse createUserResponse;
        try
        {
            createUserResponse = await GetResponse<CreateUserRequest, CreateUserResponse>(new CreateUserRequest
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
        }
        // GetAuthToken fell back to a token cached for an older game version: the rejection is most likely that token, say so.
        catch (ApiErrorException e) when (_usedStaleAuthToken)
        {
            throw new InvalidOperationException($"{ResourceStrings.AuthTokenLoadFailed} ({e.Message})", e);
        }
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

    // Set when GetAuthToken fell back to a token cached for an older game version.
    private bool _usedStaleAuthToken;

    private async Task<int> GetAuthToken()
    {
        _usedStaleAuthToken = false;
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
            if (AuthOption.CachedAuthToken.HasValue)
            {
                if (AuthOption.CachedAuthTokenVersion == AuthOption.AppVersion)
                {
                    _logger.LogWarning(e, "Using cached public AuthToken for game version {Version}", AuthOption.AppVersion);
                }
                else
                {
                    // the asset server may be gone for good; an older token is still worth a try, the server rejects it if it no longer fits
                    _logger.LogWarning(e, "Using public AuthToken cached for older game version {CachedVersion} (current {Version})",
                        AuthOption.CachedAuthTokenVersion, AuthOption.AppVersion);
                    AddLog(ResourceStrings.AuthTokenLoadFailed);
                    _usedStaleAuthToken = true;
                }
                return AuthOption.CachedAuthToken.Value;
            }
            throw new InvalidOperationException(ResourceStrings.AuthTokenLoadFailed, e);
        }
    }
}
