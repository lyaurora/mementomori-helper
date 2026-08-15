using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using MementoMori.Funcs;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace MementoMori.BlazorShared.Models;

public class AccountComponent : ComponentBase, IDisposable
{
    private readonly CompositeDisposable _accountSubscriptions = new();
    private IDisposable? _accountManagerSubscription;
    protected AccountInfo AccountInfo = null!;
    protected MementoMoriFuncs Funcs = null!;
    protected MementoNetworkManager NetworkManager = null!;

    [Inject]
    public AccountManager AccountManager { get; set; }

    [Inject]
    public ILogger<AccountComponent> Logger { get; set; }

    protected virtual Task AccountChanged()
    {
        return Task.CompletedTask;
    }

    protected override async Task OnInitializedAsync()
    {
        await ChangeAccount(AccountManager.CurrentUserId);
        _accountManagerSubscription = AccountManager.WhenAnyValue(d => d.CurrentUserId)
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(100))
            .Select(userId => Observable.FromAsync(() => InvokeAsync(() => ChangeAccount(userId)))
                .Catch<Unit, Exception>(e =>
                {
                    Logger.LogError(e, "Failed to change account");
                    return Observable.Empty<Unit>();
                }))
            .Concat()
            .Subscribe();
    }

    protected void TrackAccountSubscription(IDisposable subscription)
    {
        _accountSubscriptions.Add(subscription);
    }

    private async Task ChangeAccount(long userId)
    {
        _accountSubscriptions.Clear();
        var account = AccountManager.Get(userId);
        AccountInfo = account.AccountInfo;
        Funcs = account.Funcs;
        NetworkManager = account.NetworkManager;
        await AccountChanged();
    }

    public void Dispose()
    {
        _accountManagerSubscription?.Dispose();
        _accountSubscriptions.Dispose();
        GC.SuppressFinalize(this);
    }
}