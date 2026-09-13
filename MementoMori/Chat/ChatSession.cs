using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using System.Threading.Channels;
using System.Text.RegularExpressions;
using MementoMori.Exceptions;
using MementoMori.Funcs;
using MementoMori.Ortega.Common;
using MementoMori.Ortega.Network.MagicOnion.Client;
using MementoMori.Ortega.Network.MagicOnion.Interface;
using MementoMori.Ortega.Share.Data.ApiInterface;
using MementoMori.Ortega.Share.Data.ApiInterface.Chat;
using MementoMori.Ortega.Share.Data.Chat;
using MementoMori.Ortega.Share.MagicOnionShare.Response;

namespace MementoMori.Chat;

// One connection per account, shared by its open chat pages. Nothing is written to disk.
public sealed class ChatSession(MementoMoriFuncs funcs) : IDisposable
{
    public const int HistoryLimit = 200;
    private readonly object _sync = new();
    private readonly ISubject<Unit> _changed = Subject.Synchronize(new Subject<Unit>());
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Dictionary<(ChatType Type, long Target), List<GuildChatInfo>> _history = new();
    private readonly Dictionary<(long Sender, long Timestamp, long Reactor), ChatReactionType> _reactions = new();
    private readonly Dictionary<long, int> _privateViewers = new();
    private readonly HashSet<long> _loadedPrivateHistory = new();
    private readonly Channel<long> _privateNotices = Channel.CreateBounded<long>(new BoundedChannelOptions(20) { FullMode = BoundedChannelFullMode.DropOldest });
    private List<PrivateChatLogPlayerInfo> _contacts = new();
    private List<AnnounceChatInfo> _announcements = new();
    private bool _loadedAnnouncements;
    private CancellationTokenSource? _lifetime;
    private Task _runner = Task.CompletedTask;
    private OrtegaMagicOnionClient? _client;
    private TaskCompletionSource? _sent;
    private (ChatType Type, string Text)? _sending;
    private bool _connected;
    private bool _disposed;
    private int _viewers;
    private int _epoch;
    private int _privateGeneration;
    private long _playerId;
    private long _sendAfterTimestamp;
    private string _status = "未连接";
    private string? _error;

    public IObservable<Unit> Changed => _changed;
    public record Snapshot(bool Connected, string Status, string? Error, long PlayerId,
        IReadOnlyList<GuildChatInfo> Messages, IReadOnlyList<PrivateChatLogPlayerInfo> Contacts);

    public Snapshot Read(ChatType type, long target = 0)
    {
        lock (_sync)
            return new(_connected, _status, _error, _playerId,
                _history.GetValueOrDefault((type, target))?.Where(e => !Blocked(e.ChatInfo.PlayerId)).Select(Copy).ToArray() ?? [],
                _contacts.Where(c => !Blocked(c.PlayerInfo.PlayerId)).Select(c => new PrivateChatLogPlayerInfo { PlayerInfo = c.PlayerInfo, ExistUnread = c.ExistUnread, LocalTimestamp = c.LocalTimestamp }).ToArray());
    }

    private static GuildChatInfo Copy(GuildChatInfo entry) => new()
    {
        ChatInfo = entry.ChatInfo, CanReact = entry.CanReact, IsAnnounced = entry.IsAnnounced,
        MyChatReactionType = entry.MyChatReactionType,
        ChatReactionCountMap = entry.ChatReactionCountMap == null ? new() : new(entry.ChatReactionCountMap)
    };

    public List<AnnounceChatInfo> ReadAnnouncements()
    {
        lock (_sync) return _announcements.OrderByDescending(a => a.RegisterLocalTimestamp)
            .Select(a => new AnnounceChatInfo { GuildChatInfo = Copy(a.GuildChatInfo), RegisterLocalTimestamp = a.RegisterLocalTimestamp }).ToList();
    }

    private GuildChatInfo? FindGuildMessage(ChatIdentityInfo id) =>
        _history.GetValueOrDefault((ChatType.Guild, 0))?.Find(e => e.ChatInfo.PlayerId == id.SendPlayerId && e.ChatInfo.LocalTimeStamp == id.SendLocalTimestamp)
        ?? _announcements.Find(a => a.GuildChatInfo.ChatInfo.PlayerId == id.SendPlayerId && a.GuildChatInfo.ChatInfo.LocalTimeStamp == id.SendLocalTimestamp)?.GuildChatInfo;

    public IDisposable Attach()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _viewers++;
            Resume();
        }
        return Disposable.Create(() =>
        {
            lock (_sync)
                if (--_viewers == 0) Stop();
        });
    }

    public void Resume()
    {
        lock (_sync)
        {
            if (_disposed || _viewers == 0 || _lifetime != null || !funcs.LoginOk) return;
            var previous = _runner;
            var lifetime = _lifetime = new CancellationTokenSource();
            _playerId = funcs.NetworkManager.PlayerId;
            _runner = Task.Run(async () =>
            {
                try
                {
                    await previous;
                    if (!lifetime.IsCancellationRequested) await Run(lifetime.Token);
                }
                finally
                {
                    lock (_sync)
                    {
                        if (_lifetime == lifetime) _lifetime = null;
                        lifetime.Dispose();
                    }
                }
            });
        }
    }

    private void Stop()
    {
        _epoch++;
        _lifetime?.Cancel();
        _lifetime = null;
        _connected = false;
        _client = null;
        _status = "未连接";
        _sent?.TrySetException(new IOException("聊天连接已断开，发送结果未知，请核对消息记录后再试。"));
    }

    public void Reconnect()
    {
        lock (_sync) { Stop(); Resume(); }
        Notify();
    }

    public async Task ResetAsync()
    {
        Task runner;
        lock (_sync) { Stop(); runner = _runner; }
        await runner;
        lock (_sync)
        {
            _history.Clear();
            _contacts.Clear();
            _announcements.Clear();
            _loadedAnnouncements = false;
            _reactions.Clear();
            _privateGeneration++;
            _privateViewers.Clear();
            _loadedPrivateHistory.Clear();
            while (_privateNotices.Reader.TryRead(out _)) { }
            _playerId = 0;
            _error = null;
        }
        Notify();
    }

    public void Dispose()
    {
        lock (_sync) { _disposed = true; Stop(); }
        // The runner observes cancellation and disposes its own hub and token source.
    }

    private void Notify() => _changed.OnNext(Unit.Default);

    private async Task Run(CancellationToken token)
    {
        var failures = 0;
        while (!token.IsCancellationRequested)
        {
            OrtegaMagicOnionClient? client = null;
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
            var keepAlive = Task.CompletedTask;
            var privateUpdates = Task.CompletedTask;
            var authenticated = false;
            var connectedAt = DateTimeOffset.UtcNow;
            try
            {
                lock (_sync) { _status = "连接中"; _error = null; }
                Notify();
                client = funcs.NetworkManager.GetOnionClient();
                var receiver = new Receiver(this, connection.Token);
                client.SetupChat(receiver, receiver, receiver);
                lock (_sync) _client = client;
                await client.Connect(connection.Token).WaitAsync(TimeSpan.FromSeconds(30), token);
                await receiver.Authenticated.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
                authenticated = true;
                connectedAt = DateTimeOffset.UtcNow;
                lock (_sync) { token.ThrowIfCancellationRequested(); _connected = true; _status = "实时连接已建立"; }
                Notify();
                keepAlive = client.KeepAlive(connection.Token);
                privateUpdates = ReceivePrivateNotices(connection.Token);
                try
                {
                    await RefreshContactsAsync(connection.Token);
                    long[] watching;
                    bool refreshAnnouncements;
                    lock (_sync) { watching = _privateViewers.Keys.ToArray(); refreshAnnouncements = _loadedAnnouncements; }
                    foreach (var id in watching) await LoadPrivateMessagesAsync(id, cancellationToken: connection.Token);
                    if (refreshAnnouncements) await RequestAsync<GetAnnounceChatRequest, GetAnnounceChatResponse>(new(), connection.Token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception e) { lock (_sync) _error = "同步聊天记录失败：" + e.Message; Notify(); }
                var completed = await Task.WhenAny(keepAlive, privateUpdates, client.WaitForDisconnectAsync());
                await completed;
                throw new IOException("聊天连接已断开。");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                lock (_sync)
                {
                    _connected = false;
                    _error = e is TimeoutException ? "聊天连接超时。" : e.Message;
                    _status = !authenticated && e is ApiErrorException ? "认证失败，请重新登录账号" : "连接中断，等待重连";
                }
                Notify();
                if (!authenticated && e is ApiErrorException) break;
            }
            finally
            {
                connection.Cancel();
                lock (_sync)
                {
                    if (_client == client) { _client = null; _connected = false; }
                    _sent?.TrySetException(new IOException("聊天连接已断开，发送结果未知，请核对消息记录后再试。"));
                }
                try { await Task.WhenAll(keepAlive, privateUpdates); }
                catch (Exception) { /* Both tasks are observed; the connection error is already displayed. */ }
                if (client != null)
                {
                    try { await client.DisposeAsync(); }
                    catch (Exception) { /* A failed stream is already canceled and will not be reused. */ }
                }
                Notify();
            }
            if (authenticated && DateTimeOffset.UtcNow - connectedAt > TimeSpan.FromMinutes(1)) failures = 0;
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(5 * (1 << Math.Min(failures++, 4)), 60)), token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
        }
    }

    public async Task<TResponse> RequestAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : ApiRequestBase where TResponse : ApiResponseBase
    {
        CancellationToken token;
        lock (_sync)
        {
            if (_disposed || !_connected || _lifetime == null || !funcs.LoginOk || funcs.NetworkManager.PlayerId != _playerId)
                throw new InvalidOperationException("请先登录账号并等待聊天连接建立。");
            token = _lifetime.Token;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
        var response = await funcs.GetResponse<TRequest, TResponse>(request, linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        if (response is GetAnnounceChatResponse announcements)
        {
            lock (_sync)
            {
                linked.Token.ThrowIfCancellationRequested();
                _loadedAnnouncements = true;
                _announcements = (announcements.GuildChatInfoList ?? []).Where(a => a.GuildChatInfo?.ChatInfo != null)
                    .Select(a => new AnnounceChatInfo { GuildChatInfo = Copy(a.GuildChatInfo), RegisterLocalTimestamp = a.RegisterLocalTimestamp }).ToList();
                var history = _history.GetValueOrDefault((ChatType.Guild, 0));
                foreach (var announcement in _announcements)
                {
                    var info = announcement.GuildChatInfo.ChatInfo;
                    var index = history?.FindIndex(e => e.ChatInfo.PlayerId == info.PlayerId && e.ChatInfo.LocalTimeStamp == info.LocalTimeStamp) ?? -1;
                    if (index >= 0) history![index] = announcement.GuildChatInfo;
                }
            }
            Notify();
        }
        return response;
    }

    public async Task RefreshContactsAsync(CancellationToken cancellationToken = default)
    {
        var epoch = Volatile.Read(ref _epoch);
        var response = await RequestAsync<GetPrivateChatLogPlayerRequest, GetPrivateChatLogPlayerResponse>(new(), cancellationToken);
        lock (_sync)
        {
            if (epoch != _epoch) throw new OperationCanceledException();
            _contacts = (response.PrivateChatLogPlayerInfoList ?? []).Where(c => c.PlayerInfo != null && !Blocked(c.PlayerInfo.PlayerId))
                .OrderByDescending(c => c.LocalTimestamp).Take(ClientConst.Chat.MaxPrivateChatPlayerCount).ToList();
            foreach (var key in _history.Keys.Where(k => k.Type == ChatType.Private && !_privateViewers.ContainsKey(k.Target)
                         && _contacts.All(c => c.PlayerInfo.PlayerId != k.Target)).ToArray())
            {
                _history.Remove(key);
                _loadedPrivateHistory.Remove(key.Target);
            }
        }
        Notify();
    }

    public IDisposable WatchPrivate(long target)
    {
        ValidateTarget(target);
        int generation;
        lock (_sync)
        {
            generation = _privateGeneration;
            _privateViewers[target] = _privateViewers.GetValueOrDefault(target) + 1;
        }
        return Disposable.Create(() =>
        {
            lock (_sync)
            {
                if (generation != _privateGeneration) return;
                if (_privateViewers.GetValueOrDefault(target) <= 1) _privateViewers.Remove(target);
                else _privateViewers[target]--;
            }
        });
    }

    public async Task<int> LoadPrivateMessagesAsync(long target, bool older = false, CancellationToken cancellationToken = default)
    {
        var epoch = Volatile.Read(ref _epoch);
        ValidateTarget(target);
        var messages = Read(ChatType.Private, target).Messages;
        bool loaded;
        lock (_sync) loaded = _loadedPrivateHistory.Contains(target);
        var response = await RequestAsync<GetPrivateMessageRequest, GetPrivateMessageResponse>(new()
        {
            TargetPlayerId = target,
            LatestTimestamp = older || !loaded ? 0 : messages.LastOrDefault()?.ChatInfo.LocalTimeStamp ?? 0,
            OldestTimestamp = older ? messages.FirstOrDefault()?.ChatInfo.LocalTimeStamp ?? 0 : 0
        }, cancellationToken);
        lock (_sync)
        {
            if (epoch != _epoch) throw new OperationCanceledException();
            Merge(ChatType.Private, response.ChatInfoList?.Select(Plain) ?? [], target);
            _loadedPrivateHistory.Add(target);
            var contact = _contacts.Find(c => c.PlayerInfo.PlayerId == target);
            if (contact != null) contact.ExistUnread = false;
        }
        Notify();
        return response.ChatInfoList?.Count ?? 0;
    }

    private async Task ReceivePrivateNotices(CancellationToken token)
    {
        while (await _privateNotices.Reader.WaitToReadAsync(token))
        {
            var ids = new HashSet<long>();
            while (_privateNotices.Reader.TryRead(out var id)) ids.Add(id);
            try
            {
                await RefreshContactsAsync(token);
                long[] watching;
                lock (_sync) watching = _privateViewers.Keys.Where(id => ids.Contains(id) || _contacts.Any(c => c.PlayerInfo.PlayerId == id && c.ExistUnread)).ToArray();
                foreach (var id in watching)
                {
                    await LoadPrivateMessagesAsync(id, cancellationToken: token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception e) { lock (_sync) _error = "更新私聊失败：" + e.Message; Notify(); }
        }
    }

    public static string ValidateMessage(ChatType type, string text)
    {
        if (type is not (ChatType.World or ChatType.Guild or ChatType.SvS or ChatType.Block or ChatType.Private))
            throw new ArgumentOutOfRangeException(nameof(type));
        text = text?.Trim() ?? "";
        if (text.Length == 0 || text.Length > ClientConst.Chat.MaxLetter)
            throw new ArgumentException($"消息长度应为 1–{ClientConst.Chat.MaxLetter} 个字符。");
        return text;
    }

    private void ValidateTarget(long target)
    {
        if (target <= 0 || target == funcs.NetworkManager.PlayerId) throw new ArgumentException("请选择其他玩家作为私聊对象。");
        if (Blocked(target)) throw new InvalidOperationException("该玩家已在屏蔽名单中。");
    }

    public async Task SendAsync(ChatType type, string text, long target = 0, CancellationToken cancellationToken = default)
    {
        text = ValidateMessage(type, text);
        foreach (Match match in Regex.Matches(text, @"#([0-9]{1,9})#"))
            if (!IsEmoticonUnlocked(long.Parse(match.Groups[1].Value))) throw new InvalidOperationException("消息中包含尚未解锁的表情包。");
        if (type == ChatType.Private) ValidateTarget(target);
        CancellationToken sessionToken;
        lock (_sync)
        {
            if (_disposed || !_connected || _lifetime == null) throw new InvalidOperationException("聊天尚未连接。");
            sessionToken = _lifetime.Token;
            _error = null;
        }
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(sessionToken, cancellationToken);
        cancellationToken = operation.Token;
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type == ChatType.Private)
            {
                await RequestAsync<SendPrivateMessageRequest, SendPrivateMessageResponse>(new() { TargetPlayerId = target, Message = text }, cancellationToken);
                // The HTTP response confirms delivery. A history-refresh failure must not invite a duplicate send.
                try { await LoadPrivateMessagesAsync(target, cancellationToken: cancellationToken); }
                catch (Exception e) { lock (_sync) _error = "消息已发送，但更新记录失败：" + e.Message; Notify(); }
                return;
            }
            OrtegaMagicOnionClient client;
            Task sent;
            CancellationToken token;
            lock (_sync)
            {
                if (!_connected || _client == null || _lifetime == null || !funcs.LoginOk || funcs.NetworkManager.PlayerId != _playerId)
                    throw new InvalidOperationException("聊天尚未连接。");
                client = _client;
                token = _lifetime.Token;
                _sending = (type, text);
                _sendAfterTimestamp = _history.GetValueOrDefault((type, 0))?.Where(m => m.ChatInfo.PlayerId == _playerId).Max(m => (long?)m.ChatInfo.LocalTimeStamp) ?? 0;
                _sent = new(TaskCreationOptions.RunContinuationsAsynchronously);
                sent = _sent.Task;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
            // A hub invocation may succeed while OnError rejects the message: wait for our server echo.
            linked.Token.ThrowIfCancellationRequested();
            await client.SendChatMessageAsync(type, text).WaitAsync(TimeSpan.FromSeconds(15), linked.Token);
            await sent.WaitAsync(TimeSpan.FromSeconds(15), linked.Token);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("尚未收到服务器的发送确认，请核对聊天记录后再试，以免重复发送。");
        }
        finally
        {
            lock (_sync)
            {
                if (_sent?.Task.IsFaulted == true) _ = _sent.Task.Exception;
                _sent?.TrySetCanceled();
                _sending = null;
                _sent = null;
            }
            _sendLock.Release();
        }
    }

    public static ChatIdentityInfo Identity(ChatInfo info) => new() { SendPlayerId = info.PlayerId, SendLocalTimestamp = info.LocalTimeStamp };
    public Task ReactAsync(ChatInfo message, ChatReactionType selected, CancellationToken cancellationToken = default)
    {
        if (selected is < ChatReactionType.Wink or > ChatReactionType.Sad) throw new ArgumentOutOfRangeException(nameof(selected));
        GuildChatInfo? entry;
        lock (_sync) entry = FindGuildMessage(Identity(message));
        // CanReact is a retained legacy field. Client 4.22.0's ChatOptionData no longer uses it.
        if (entry == null) throw new InvalidOperationException("消息记录已更新，请重新选择要回应的消息。");
        // The game sends None (0), not the existing type, to cancel a reaction.
        return RequestAsync<ReactChatRequest, ReactChatResponse>(new()
        {
            ChatIdentityInfo = Identity(message), ChatReactionType = entry.MyChatReactionType == selected ? ChatReactionType.None : selected
        }, cancellationToken);
    }
    public bool IsEmoticonUnlocked(long id) => id > 0 && (id < 1000 || funcs.UserSyncData?.UserItemDtoInfo?.Any(item => item.ItemType == ItemType.ChatEmoticon && item.ItemId == id && item.ItemCount > 0) == true);
    // GuildPermissionUtil in client 4.22.0: veterans may manage their own announcements/surveys.
    public static bool CanManageGuildChat(PlayerGuildPositionType position) => position is PlayerGuildPositionType.Leader or PlayerGuildPositionType.SubLeader or PlayerGuildPositionType.Commander or PlayerGuildPositionType.Veteran;
    public static bool CanRegisterAnnouncement(PlayerGuildPositionType position, ChatInfo message, long player) => CanManageGuildChat(position)
        && player > 0 && message.PlayerId == player && message.ChatType == ChatType.Guild && message.SystemChatType == SystemChatType.None
        && message.ChatBattleInfo == null && message.ChatMusicPlaylistInfo == null && message.ChatRecruitGuildMemberInfo == null;
    public static bool CanDeleteGuildPost(PlayerGuildPositionType position, long author, long player) => CanManageGuildChat(position) && (author == player || position != PlayerGuildPositionType.Veteran);
    private bool Blocked(long id) => funcs.UserSyncData?.BlockPlayerIdList?.Contains(id) == true;
    private void ClearGuildChat()
    {
        funcs.NetworkManager.GuildPositionType = PlayerGuildPositionType.None;
        _history.Remove((ChatType.Guild, 0));
        _announcements.Clear();
        _loadedAnnouncements = false;
        _reactions.Clear();
        _error = "已离开公会。";
    }
    private static GuildChatInfo Plain(ChatInfo info) => new() { ChatInfo = info, CanReact = info != null && info.SystemChatType == SystemChatType.None, ChatReactionCountMap = new() };

    private void Merge(ChatType type, IEnumerable<GuildChatInfo> entries, long target = 0, bool replace = true)
    {
        var key = (type, target);
        if (!_history.TryGetValue(key, out var list)) _history[key] = list = new();
        foreach (var entry in entries.Where(e => e?.ChatInfo != null && !Blocked(e.ChatInfo.PlayerId)))
        {
            var index = list.FindIndex(e => e.ChatInfo.PlayerId == entry.ChatInfo.PlayerId && e.ChatInfo.LocalTimeStamp == entry.ChatInfo.LocalTimeStamp);
            if (index >= 0 && !replace) continue;
            var copy = Copy(entry);
            var announcement = type == ChatType.Guild ? _announcements.Find(a => a.GuildChatInfo.ChatInfo.PlayerId == entry.ChatInfo.PlayerId && a.GuildChatInfo.ChatInfo.LocalTimeStamp == entry.ChatInfo.LocalTimeStamp) : null;
            if (announcement != null)
            {
                if (!replace) copy = announcement.GuildChatInfo;
                else announcement.GuildChatInfo = copy;
            }
            if (index < 0) list.Add(copy);
            else list[index] = copy;
        }
        list.Sort((a, b) => a.ChatInfo.LocalTimeStamp.CompareTo(b.ChatInfo.LocalTimeStamp));
        if (list.Count > HistoryLimit) list.RemoveRange(0, list.Count - HistoryLimit);
        if (type == ChatType.Guild)
        {
            var keys = list.Select(e => (e.ChatInfo.PlayerId, e.ChatInfo.LocalTimeStamp)).ToHashSet();
            keys.UnionWith(_announcements.Select(a => (a.GuildChatInfo.ChatInfo.PlayerId, a.GuildChatInfo.ChatInfo.LocalTimeStamp)));
            foreach (var reaction in _reactions.Keys.Where(r => !keys.Contains((r.Sender, r.Timestamp))).ToArray()) _reactions.Remove(reaction);
        }
    }

    private sealed class Receiver(ChatSession owner, CancellationToken token) : IMagicOnionChatReceiver, IMagicOnionAuthenticateReceiver, IMagicOnionErrorReceiver
    {
        public TaskCompletionSource Authenticated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private void Receive(Action action)
        {
            lock (owner._sync)
            {
                if (token.IsCancellationRequested) return;
                action();
            }
            owner.Notify();
        }
        public void OnAuthenticateSuccess() => Receive(() => Authenticated.TrySetResult());
        public void OnError(ErrorCode error) => Receive(() =>
        {
            var exception = new ApiErrorException(error);
            Authenticated.TrySetException(exception);
            owner._sent?.TrySetException(exception);
            owner._error = exception.Message;
        });
        public void OnReceiveWorldChatLog(OnReceiveWorldChatLogResponse r) => Receive(() => owner.Merge(ChatType.World, r.ChatInfoList?.Select(Plain) ?? []));
        public void OnReceiveSvSChatLog(OnReceiveSvSChatLogResponse r) => Receive(() => owner.Merge(ChatType.SvS, r.ChatInfoList?.Select(Plain) ?? []));
        public void OnReceiveBlockChatLog(OnReceiveBlockChatLogResponse r) => Receive(() => owner.Merge(ChatType.Block, r.ChatInfoList?.Select(Plain) ?? []));
        public void OnReceiveGuildChatLog(OnReceiveGuildChatLogResponse r) => Receive(() =>
        {
            owner._reactions.Clear();
            owner.Merge(ChatType.Guild, r.GuildChatInfoList is { Count: > 0 } ? r.GuildChatInfoList : r.ChatInfoList?.Select(Plain) ?? []);
        });
        public void OnRemovedFromGuild() => Receive(owner.ClearGuildChat);
        public void OnNoticePrivateMessage(OnNoticePrivateMessageResponse r) => Receive(() =>
        {
            if (r.PlayerId <= 0 || owner.Blocked(r.PlayerId)) return;
            var contact = owner._contacts.Find(c => c.PlayerInfo.PlayerId == r.PlayerId);
            if (contact != null) contact.ExistUnread = true;
            owner._privateNotices.Writer.TryWrite(r.PlayerId);
        });
        public void OnReceiveMessage(OnReceiveMessageResponse r) => Receive(() =>
        {
            if (r.ChatInfo == null || owner.Blocked(r.ChatInfo.PlayerId)) return;
            var info = r.ChatInfo;
            if (info.ChatType is ChatType.Private or ChatType.Friend)
            {
                var target = r.OtherPlayerId > 0 ? r.OtherPlayerId : info.PlayerId;
                if (target <= 0 || target == owner._playerId || owner.Blocked(target)) return;
                owner.Merge(ChatType.Private, [Plain(info)], target, replace: false);
                var contact = owner._contacts.Find(c => c.PlayerInfo.PlayerId == target);
                if (contact != null && info.PlayerId != owner._playerId && !owner._privateViewers.ContainsKey(target)) contact.ExistUnread = true;
                owner._privateNotices.Writer.TryWrite(target);
                return;
            }
            owner.Merge(info.ChatType, [Plain(info)], replace: false);
            if (info.PlayerId == owner._playerId && info.LocalTimeStamp > owner._sendAfterTimestamp && owner._sending == (info.ChatType, info.Message)) owner._sent?.TrySetResult();
        });
        public void OnChangeChatOption(OnChangeChatOptionResponse r) => Receive(() =>
        {
            foreach (var option in r.ChangeChatOptionInfoList ?? [])
            {
                var entry = Find(option.ChatIdentityInfo);
                if (entry == null) continue;
                entry.CanReact = option.CanReact;
                entry.IsAnnounced = option.IsAnnounced;
                if (!option.IsAnnounced) owner._announcements.RemoveAll(a => a.GuildChatInfo == entry);
            }
        });
        public void OnReactChat(OnReactChatResponse r) => Receive(() =>
        {
            foreach (var reaction in r.ReactChatInfoList ?? [])
            {
                var identity = reaction.ChatIdentityInfo;
                var entry = Find(identity);
                if (entry == null || reaction.ChatReactionType == ChatReactionType.None) continue;
                var key = (identity.SendPlayerId, identity.SendLocalTimestamp, reaction.ReactPlayerId);
                var known = owner._reactions.TryGetValue(key, out var previous);
                if (!known && reaction.ReactPlayerId == owner._playerId) { known = true; previous = entry.MyChatReactionType; }
                if (reaction.IsCanceled)
                {
                    if (known && previous != reaction.ChatReactionType) continue;
                    entry.ChatReactionCountMap[reaction.ChatReactionType] = Math.Max(0, entry.ChatReactionCountMap.GetValueOrDefault(reaction.ChatReactionType) - 1);
                    owner._reactions[key] = ChatReactionType.None;
                }
                else
                {
                    if (known && previous == reaction.ChatReactionType) continue;
                    if (known && previous != ChatReactionType.None) entry.ChatReactionCountMap[previous] = Math.Max(0, entry.ChatReactionCountMap.GetValueOrDefault(previous) - 1);
                    entry.ChatReactionCountMap[reaction.ChatReactionType] = entry.ChatReactionCountMap.GetValueOrDefault(reaction.ChatReactionType) + 1;
                    owner._reactions[key] = reaction.ChatReactionType;
                }
                if (reaction.ReactPlayerId == owner._playerId) entry.MyChatReactionType = reaction.IsCanceled ? ChatReactionType.None : reaction.ChatReactionType;
            }
        });
        private GuildChatInfo? Find(ChatIdentityInfo? id) => id == null ? null : owner.FindGuildMessage(id);
    }
}
