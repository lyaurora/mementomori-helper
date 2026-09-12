using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using AutoCtor;
using Injectio.Attributes;
using MementoMori.Funcs;
using MementoMori.Option;
using MementoMori.Ortega.Share.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace MementoMori;

[RegisterSingleton<AccountManager>]
[AutoConstruct]
public partial class AccountManager : ReactiveObject
{
    private readonly ConcurrentDictionary<long, Account> _accounts = new();
    private readonly IWritableOptions<AuthOption> _authOption;
    private readonly IWritableOptions<GameConfig> _gameConfig;
    private readonly ILogger<AccountManager> _logger;
    private readonly IServiceProvider _serviceProvider;
    private CultureInfo _currentCulture;
    public IReadOnlyList<AccountInfo> AccountInfos => _authOption.Value.Accounts;

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentCulture, value);
            foreach (var account in _accounts.Values)
            {
                account.NetworkManager.SetCultureInfo(value);
            }

            CultureInfo.DefaultThreadCurrentCulture = value;
            CultureInfo.DefaultThreadCurrentUICulture = value;
        }
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public Account Get(long userId)
    {
        if (!_accounts.ContainsKey(userId))
        {
            var account = new Account
            {
                AccountInfo = _authOption.Value.Accounts.FirstOrDefault(x => x.UserId == userId),
                NetworkManager = _serviceProvider.GetService<MementoNetworkManager>(),
                Funcs = _serviceProvider.GetService<MementoMoriFuncs>()
            };
            account.Funcs.NetworkManager = account.NetworkManager;
            account.Funcs.UserId = userId;
            account.NetworkManager.UserId = userId;
            _accounts[userId] = account;
        }

        return _accounts[userId];
    }

    public bool Contains(long userId)
    {
        return _accounts.ContainsKey(userId);
    }

    public bool TryGet(long userId, out Account account) => _accounts.TryGetValue(userId, out account);

    public void AddAccountInfo(long userId, string clientKey, string name, bool autoLogin)
    {
        _authOption.Update(opt =>
        {
            if (opt.Accounts.Any(a => a.UserId == userId)) throw new InvalidOperationException("Account already exists.");
            opt.Accounts.Add(new AccountInfo
            {
                UserId = userId,
                ClientKey = clientKey,
                Name = name,
                AutoLogin = autoLogin
            });
        });
        UpdateAccountInfo(userId);
    }

    public AccountInfo GetAccountInfo(long userId)
    {
        return _authOption.Value.Accounts.FirstOrDefault(x => x.UserId == userId);
    }

    public Dictionary<long, Account> GetAll()
    {
        return _accounts.ToDictionary(x => x.Key, x => x.Value);
    }

    public void MigrateToAccountArray()
    {
        if (_authOption.Value.Accounts.Count == 0 && _authOption.Value.UserId > 0 && !_authOption.Value.ClientKey.IsNullOrEmpty())
        {
            _authOption.Update(opt =>
            {
                opt.Accounts = new List<AccountInfo>
                {
                    new()
                    {
                        UserId = opt.UserId,
                        ClientKey = opt.ClientKey,
                        Name = ResourceStrings.MainAccount,
                        AutoLogin = _gameConfig.Value.Login.AutoLogin
                    }
                };
            });
        }
    }

    public async Task AutoLogin(CancellationToken cancellationToken = default)
    {
        foreach (var account in _authOption.Value.Accounts.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (account.AutoLogin)
            {
                try
                {
                    await Get(account.UserId).Funcs.AutoLogin(false, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception e)
                {
                    _logger.LogError(e, "AutoLogin error");
                }
            }
        }
    }

    public void UpdateAccountInfo(long userId)
    {
        Get(userId).AccountInfo = GetAccountInfo(userId);
        this.RaisePropertyChanged(nameof(AccountInfos));
    }

    public async Task RemoveAccount(long userId)
    {
        if (_accounts.TryGetValue(userId, out var account)) await account.Funcs.Logout();
        _authOption.Update(opt => { opt.Accounts.RemoveAll(x => x.UserId == userId); });
        this.RaisePropertyChanged(nameof(AccountInfos));
        if (_accounts.TryRemove(userId, out account))
        {
            account.Funcs.Dispose();
            account.NetworkManager?.Dispose();
        }
    }
}

public class Account
{
    public AccountInfo AccountInfo { get; set; }
    public MementoNetworkManager NetworkManager { get; set; }
    public MementoMoriFuncs Funcs { get; set; }
}
