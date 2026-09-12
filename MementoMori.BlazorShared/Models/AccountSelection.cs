using Injectio.Attributes;
using ReactiveUI;
using System.Reactive.Linq;

namespace MementoMori.BlazorShared.Models;

[RegisterScoped]
public sealed class AccountSelection : ReactiveObject, IDisposable
{
    private readonly AccountManager _manager;
    private readonly IDisposable _subscription;
    private long _userId;

    public AccountSelection(AccountManager manager)
    {
        _manager = manager;
        _subscription = manager.Changed.Where(change => change.PropertyName == nameof(AccountManager.AccountInfos))
            .Subscribe(_ => this.RaisePropertyChanged(nameof(CurrentUserId)));
    }

    public long CurrentUserId
    {
        get => _manager.AccountInfos.Any(account => account.UserId == _userId) ? _userId : _manager.AccountInfos.FirstOrDefault()?.UserId ?? 0;
        set => this.RaiseAndSetIfChanged(ref _userId, value);
    }

    public Account Current => _manager.Get(CurrentUserId);
    public void Dispose() => _subscription.Dispose();
}
