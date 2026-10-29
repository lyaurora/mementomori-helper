using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using MementoMori.Chat;
using MementoMori.BlazorShared.Components;
using MementoMori.BlazorShared.Models;
using MementoMori.Common.Localization;
using MementoMori.Ortega.Share;
using MementoMori.Ortega.Share.Data.ApiInterface.Chat;
using MementoMori.Ortega.Share.Data.ApiInterface.GuildNote;
using MementoMori.Ortega.Share.Data.ApiInterface.GuildSurvey;
using MementoMori.Ortega.Share.Data.Chat;
using MementoMori.Ortega.Share.Data.GuildNote;
using MementoMori.Ortega.Share.Data.GuildSurvey;
using MementoMori.Ortega.Share.Data.Player;
using MementoMori.Ortega.Share.Enums;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using Newtonsoft.Json;
using static MementoMori.BlazorShared.Extensions.MasterResourceExtension;
using ReactiveUI;

namespace MementoMori.BlazorShared.Pages;

public partial class Chat
{
    [Inject] public IJSRuntime JS { get; set; } = null!;
    [Inject] public IDialogService DialogService { get; set; } = null!;
    [Inject] public IFileSaver FileSaver { get; set; } = null!;

    private static readonly ChatType[] Channels = [ChatType.World, ChatType.Guild, ChatType.SvS, ChatType.Block, ChatType.Private];
    private static readonly DialogOptions GuildDialogOptions = new() { FullWidth = true, MaxWidth = MaxWidth.Medium, BackdropClick = false, CloseOnEscapeKey = false };
    private readonly Dictionary<(ChatType, long), string> _drafts = new();
    private ChatSession _chat = null!;
    private ChatSession.Snapshot _snapshot = new(false, ResourceStrings.ChatNotConnected, null, 0, [], []);
    private CancellationTokenSource _accountCancellation = new();
    private IDisposable? _privateLease;
    private IJSObjectReference? _module;
    private MudDialog? _guildDialog;
    private MudButton? _emoticonButton;
    private ElementReference _messageElement;
    private ElementReference _inputElement;
    private ElementReference _noteTitleElement;
    private ChatType _channel = ChatType.World;
    private ChatType? _savedChannel;
    private bool _restoreChannel;
    private long _target;
    private int _revision;
    private int _operationVersion;
    private bool _interactive, _disposed, _busy, _showSettings, _showGuildInfo, _showPlayers, _showEmoticons, _noOlder;
    private int _emoticonTab = 1;
    private int _guildTab;
    private string? _error, _result, _details;
    private string _targetInput = "", _scrollMode = "end";
    private int _fontSize = 25;
    private int _textSize = 16, _stickerSize = 48;
    private List<PlayerInfo> _players = new();
    private List<AnnounceChatInfo> _announcements = new();
    private List<GuildSurveyInfo> _surveys = new();
    private List<GuildNoteInfo> _guildNotes = new();
    private long? _editingGuildNoteId;
    private bool _focusGuildNoteTitle;
    private bool _announcementMenuOpen;
    private readonly Dictionary<(long, long), MudMenu> _announcementMenus = new();
    private readonly Dictionary<(long, long), MudIconButton> _announcementMenuButtons = new();
    private string _noteTitle = "", _noteContent = "";
    private GuildNoteCategoryType _noteCategory = GuildNoteCategoryType.Normal;
    private readonly Dictionary<string, GuildSurveyChoiceType> _surveyChoices = new();
    private readonly Dictionary<string, string> _surveyDetails = new();
    private ChatInfo? _shareBattle;
    private ChatType _shareChannel;
    private string _shareTarget = "";
    private long ViewAccountId => AccountInfo?.UserId ?? 0;
    private (ChatType, long) DraftKey => (_channel, _channel == ChatType.Private ? _target : 0);
    private string Draft { get => _drafts.GetValueOrDefault(DraftKey, ""); set => _drafts[DraftKey] = value; }
    private int DisplayFontSize => _textSize;
    private IEnumerable<int> EmoticonIds => (Masters.ChatEmoticonTable.GetById(_emoticonTab)?.EmoticonList?.Select(e => (int)e.EmoticonId)
        ?? ChatEmoticons.Ids.Where(id => _emoticonTab == 1 ? id < 1000 : id >= 1000)).Where(id => ChatEmoticons.Ids.Contains(id));
    private bool CanSend => Funcs.LoginOk && _snapshot.Connected && !_busy && !string.IsNullOrWhiteSpace(Draft) && Draft.Length <= 80 && (_channel != ChatType.Private || _target > 0);
    private bool CanAnnounce(ChatInfo message) => ChatSession.CanRegisterAnnouncement(NetworkManager.GuildPositionType, message, _snapshot.PlayerId);
    private bool CanDeleteAnnouncement(ChatInfo message) => ChatSession.CanDeleteGuildPost(NetworkManager.GuildPositionType, message.PlayerId, _snapshot.PlayerId);
    private bool CanManageGuildNotes => ChatSession.CanManageGuildChat(NetworkManager.GuildPositionType);
    private bool CanSortGuildNotes => ChatSession.CanSortGuildNotes(NetworkManager.GuildPositionType);
    private bool CanEditGuildNote(GuildNoteInfo note) => _snapshot.PlayerId > 0 && note.PlayerId == _snapshot.PlayerId;
    private bool CanDeleteGuildNote(GuildNoteInfo note) => ChatSession.CanDeleteGuildNote(NetworkManager.GuildPositionType, note.PlayerId, _snapshot.PlayerId);
    private string ConversationName => _channel != ChatType.Private ? ChannelName(_channel) : _target == 0 ? ChannelName(ChatType.Private) :
        $"{PlayerName(_snapshot.Contacts.FirstOrDefault(c => c.PlayerInfo.PlayerId == _target)?.PlayerInfo.PlayerName, _target)} · {_target}";

    protected override Task AccountChanged()
    {
        _revision++;
        _accountCancellation.Dispose();
        var cancellation = _accountCancellation = new();
        TrackAccountSubscription(Disposable.Create(() => cancellation.Cancel()));
        TrackAccountSubscription(Disposable.Create(() => _privateLease?.Dispose()));
        _privateLease = null;
        _chat = Funcs.Chat;
        _restoreChannel = !AccountSelection.ChatChannels.TryGetValue(ViewAccountId, out _channel);
        if (_restoreChannel) _channel = ChatType.World;
        _savedChannel = _restoreChannel ? null : _channel;
        (_textSize, _stickerSize) = AccountSelection.ChatAppearance ?? (16, 48);
        _target = 0;
        _drafts.Clear();
        _players.Clear();
        _announcements.Clear();
        _surveys.Clear();
        _guildNotes.Clear();
        CancelGuildNoteEdit();
        _surveyChoices.Clear();
        _surveyDetails.Clear();
        _targetInput = "";
        _error = _result = _details = null;
        _shareBattle = null;
        _busy = _showGuildInfo = _showPlayers = _showEmoticons = _noOlder = false;
        _fontSize = Funcs.UserSyncData?.ChatSettingData?.FontSize is 15 or 20 or 25 or 30 ? Funcs.UserSyncData.ChatSettingData.FontSize : 25;
        _snapshot = _chat.Read(_channel);
        _scrollMode = "end";
        var chat = _chat;
        var revision = _revision;
        TrackAccountSubscription(chat.Changed.Subscribe(change => _ = UpdateFromChat(chat, revision)));
        TrackAccountSubscription(Funcs.WhenAnyValue(f => f.LoginOk).Skip(1).Subscribe(change => _ = UpdateFromChat(chat, revision)));
        if (_interactive) TrackAccountSubscription(chat.Attach());
        return InvokeAsync(StateHasChanged);
    }

    private async Task UpdateFromChat(ChatSession chat, int revision)
    {
        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed || revision != _revision || chat != _chat) return;
                RefreshSnapshot();
                StateHasChanged();
            });
        }
        catch (Exception e) when (_disposed || e is ObjectDisposedException) { }
        catch (Exception e) { Logger.LogWarning(e, "Failed to update chat view"); }
    }

    private void RefreshSnapshot()
    {
        var snapshot = _chat.Read(_channel, _channel == ChatType.Private ? _target : 0);
        if (snapshot.PlayerId != _snapshot.PlayerId)
            _fontSize = Funcs.UserSyncData?.ChatSettingData?.FontSize is 15 or 20 or 25 or 30 ? Funcs.UserSyncData.ChatSettingData.FontSize : 25;
        if (_snapshot.PlayerId != 0 && (!Funcs.LoginOk || snapshot.PlayerId != _snapshot.PlayerId))
        {
            _operationVersion++;
            _busy = false;
            _error = _result = null;
            _privateLease?.Dispose();
            _privateLease = null;
            _target = 0;
            _targetInput = _shareTarget = "";
            _showPlayers = _showGuildInfo = _showEmoticons = false;
            _drafts.Clear();
            _players.Clear();
            _announcements.Clear();
            _surveys.Clear();
            _guildNotes.Clear();
            CancelGuildNoteEdit();
            _surveyChoices.Clear();
            _surveyDetails.Clear();
            _details = null;
            _shareBattle = null;
            snapshot = _chat.Read(_channel);
        }
        _snapshot = snapshot;
        _announcements = _chat.ReadAnnouncements();
        if (NetworkManager.GuildPositionType == PlayerGuildPositionType.None)
        {
            _showGuildInfo = false;
            _guildNotes.Clear();
            CancelGuildNoteEdit();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            if (firstRender)
            {
                _interactive = true;
                TrackAccountSubscription(_chat.Attach());
                var revision = _revision;
                var account = ViewAccountId.ToString();
                var module = await JS.InvokeAsync<IJSObjectReference>("import", "./" + Assets["_content/MementoMori.BlazorShared/chat.js"]);
                if (_disposed) { await module.DisposeAsync(); return; }
                var appearanceTask = AccountSelection.ChatAppearance is { } cached
                    ? Task.FromResult(new Appearance(cached.TextSize, cached.StickerSize))
                    : module.InvokeAsync<Appearance>("getAppearance").AsTask();
                var channelTask = _restoreChannel ? module.InvokeAsync<string?>("getChannel", account).AsTask() : Task.FromResult<string?>(null);
                await Task.WhenAll(appearanceTask, channelTask);
                if (_disposed) { await module.DisposeAsync(); return; }
                _module = module;
                var appearance = await appearanceTask;
                _textSize = Math.Clamp(appearance.TextSize, 12, 24);
                _stickerSize = Math.Clamp(appearance.StickerSize, 24, 96);
                AccountSelection.ChatAppearance = (_textSize, _stickerSize);
                if (revision == _revision && _restoreChannel) RestoreChannel(await channelTask);
                StateHasChanged();
                return;
            }
            if (_module != null && !_disposed)
            {
                var revision = _revision;
                var account = ViewAccountId.ToString();
                if (_restoreChannel)
                {
                    var saved = await _module.InvokeAsync<string?>("getChannel", account);
                    if (_disposed || revision != _revision) return;
                    // A channel chosen while storage was loading takes precedence.
                    if (_restoreChannel)
                    {
                        RestoreChannel(saved);
                        StateHasChanged();
                        return;
                    }
                }
                if (_savedChannel != _channel)
                {
                    _savedChannel = _channel;
                    var saved = await _module.InvokeAsync<bool>("saveChannel", account, _channel.ToString());
                    if (_disposed || revision != _revision) return;
                    if (!saved) { _error = ResourceStrings.ChatBrowserSaveFailed; StateHasChanged(); }
                }
                var mode = _scrollMode;
                _scrollMode = "follow";
                await _module.InvokeVoidAsync("followMessages", _messageElement, mode);
                if (_focusGuildNoteTitle && _showGuildInfo && _editingGuildNoteId != null && !_disposed && revision == _revision)
                {
                    _focusGuildNoteTitle = false;
                    await _noteTitleElement.FocusAsync();
                }
            }
        }
        catch (JSDisconnectedException) { }
        catch (OperationCanceledException) when (_disposed) { }
    }

    private void RestoreChannel(string? saved)
    {
        _restoreChannel = false;
        _channel = Enum.TryParse<ChatType>(saved, out var channel) && Channels.Contains(channel) ? channel : ChatType.World;
        _savedChannel = _channel;
        AccountSelection.ChatChannels[ViewAccountId] = _channel;
        RefreshSnapshot();
        _scrollMode = "end";
    }

    private void ToggleEmoticons() => _showEmoticons = !_showEmoticons;
    private async Task CloseEmoticons()
    {
        _showEmoticons = false;
        if (_emoticonButton != null) await _emoticonButton.FocusAsync();
    }
    private Task EmoticonKeyDown(KeyboardEventArgs args) => args.Key == "Escape" ? CloseEmoticons() : Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_guildDialog != null) await _guildDialog.CloseAsync();
        Dispose();
        _accountCancellation.Dispose();
        if (_module != null)
        {
            try { await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
    }

    private async Task Run<T>(Func<ChatSession, CancellationToken, Task<T>> operation, Action<T>? apply = null)
    {
        if (_busy || _disposed) return;
        var chat = _chat;
        var revision = _revision;
        var operationVersion = _operationVersion;
        var token = _accountCancellation.Token;
        bool Current() => !_disposed && revision == _revision && operationVersion == _operationVersion && chat == _chat;
        _busy = true;
        _error = _result = null;
        try
        {
            var value = await operation(chat, token);
            if (Current()) { apply?.Invoke(value); RefreshSnapshot(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e) { if (Current()) _error = e.Message; }
        finally { if (Current()) { _busy = false; StateHasChanged(); } }
    }

    private Task Run(Func<ChatSession, CancellationToken, Task> operation, Action? apply = null) =>
        Run(async (chat, token) => { await operation(chat, token); return true; }, _ => apply?.Invoke());

    private void Reconnect() { _error = _result = null; _chat.Reconnect(); }

    private Task ChangeChannel(ChatType channel)
    {
        if (_busy) return Task.CompletedTask;
        _privateLease?.Dispose();
        _privateLease = null;
        _restoreChannel = false;
        _channel = channel;
        _showGuildInfo = false;
        _showEmoticons = false;
        AccountSelection.ChatChannels[ViewAccountId] = _channel;
        _error = _result = _details = null;
        _scrollMode = "end";
        RefreshSnapshot();
        return channel == ChatType.Private && _target > 0 ? SelectPrivate(_target) : Task.CompletedTask;
    }

    private Task SelectPrivate(long target) => Run(async (chat, token) =>
    {
        BindPrivate(target);
        RefreshSnapshot();
        await chat.LoadPrivateMessagesAsync(target, cancellationToken: token);
    });

    private void BindPrivate(long target)
    {
        _privateLease?.Dispose();
        _privateLease = _chat.WatchPrivate(target);
        _restoreChannel = false;
        _channel = ChatType.Private;
        AccountSelection.ChatChannels[ViewAccountId] = _channel;
        _target = target;
        _targetInput = target.ToString();
        _noOlder = false;
        _showPlayers = false;
        _scrollMode = "end";
    }

    private Task StartPrivate(long target) => Run(async (chat, token) =>
    {
        if (target <= 0 || target == chat.Read(_channel).PlayerId) throw new ArgumentException(ResourceStrings.EnterOtherPlayerId);
        await chat.RequestAsync<AddPrivateChatLogPlayerRequest, AddPrivateChatLogPlayerResponse>(new() { TargetPlayerId = target }, token);
        await chat.RefreshContactsAsync(token);
        await chat.LoadPrivateMessagesAsync(target, cancellationToken: token);
        return target;
    }, BindPrivate);

    private Task AddPrivate()
    {
        if (long.TryParse(_targetInput, out var target)) return StartPrivate(target);
        _error = Tr("[ClientErrorMessage300009]");
        return Task.CompletedTask;
    }

    private Task SelectPlayer(ChangeEventArgs args) => long.TryParse(args.Value?.ToString(), out var target) ? StartPrivate(target) : Task.CompletedTask;
    private Task RefreshContacts() => Run((chat, token) => chat.RefreshContactsAsync(token));

    private Task TogglePlayers()
    {
        if (_showPlayers) { _showPlayers = false; return Task.CompletedTask; }
        _showPlayers = true;
        return LoadPlayers();
    }

    private Task LoadPlayers() => Run(async (chat, token) =>
    {
        var ids = chat.Read(ChatType.World).Messages.Select(m => m.ChatInfo.PlayerId).Where(id => id > 0).Distinct().ToList();
        var response = await chat.RequestAsync<GetPlayerRequest, GetPlayerResponse>(new() { WorldPlayerIdList = ids }, token);
        return (response.FriendPlayerInfoList ?? []).Concat(response.GuildPlayerInfoList ?? []).Concat(response.WorldPlayerInfoList ?? [])
            .DistinctBy(p => p.PlayerId).Where(p => p.PlayerId != chat.Read(ChatType.World).PlayerId && !p.IsBlock).ToList();
    }, players => { _players = players; if (players.Count == 0) _result = Tr("[ChatNotExistsSelectablePlayerMessage]"); });

    private Task LoadOlder()
    {
        var target = _target;
        return Run((chat, token) => chat.LoadPrivateMessagesAsync(target, true, token), count => { _noOlder = count == 0; _scrollMode = "older"; });
    }

    private Task RemovePrivate()
    {
        var target = _target;
        var name = ConversationName;
        return Run(async (chat, token) =>
        {
            if (await DialogService.ShowMessageBox(Tr("[ChatPlayerListRemoveDialogTitle]"), Tr("[ChatPlayerListRemoveDialogMessage]", name), yesText: Tr("[DialogCommonFormationRemoveButton]"), cancelText: Tr("[CommonCancelLabel]")) != true) return false;
            token.ThrowIfCancellationRequested();
            await chat.RequestAsync<DeletePrivateChatLogPlayerRequest, DeletePrivateChatLogPlayerResponse>(new() { TargetPlayerId = target }, token);
            await chat.RefreshContactsAsync(token);
            return true;
        }, removed =>
        {
            if (!removed) return;
            _privateLease?.Dispose();
            _privateLease = null;
            _target = 0;
        });
    }

    private Task Send()
    {
        if (!CanSend) return Task.CompletedTask;
        var (channel, target) = DraftKey;
        var text = Draft;
        return Run((chat, token) => chat.SendAsync(channel, text, target, token), () =>
        {
            if (_drafts.GetValueOrDefault((channel, target)) == text) _drafts.Remove((channel, target));
            _result = ResourceStrings.Sent;
            _showEmoticons = false;
            _scrollMode = "end";
        });
    }

    private Task MessageKeyDown(KeyboardEventArgs args) => args.Key == "Escape" && _showEmoticons ? CloseEmoticons()
        : args.Key == "Enter" && args.CtrlKey ? Send() : Task.CompletedTask;
    private async Task InsertEmoticon(int id)
    {
        if (_busy || !_chat.IsEmoticonUnlocked(id)) return;
        var token = $"#{id}#";
        if (Draft.Length + token.Length > 80) { _error = string.Format(ResourceStrings.StickerLimitExceeded, 80); return; }
        Draft += token;
        await _inputElement.FocusAsync();
    }
    private Task React(ChatInfo message, ChatReactionType reaction) => Run((chat, token) => chat.ReactAsync(message, reaction, token));

    private Task ReactionDetails(ChatInfo message) => Run(async (chat, token) =>
    {
        var response = await chat.RequestAsync<GetChatReactionDetailInfoRequest, GetChatReactionDetailInfoResponse>(new() { ChatIdentityInfo = ChatSession.Identity(message) }, token);
        return string.Join("\n", (response.PlayerInfoListByReactionType ?? []).Select(pair => DetailLine(ChatReactions.Name(pair.Key), pair.Value.Select(p => p.PlayerName))));
    }, details => _details = string.IsNullOrWhiteSpace(details) ? ResourceStrings.NoReactions : details);

    private Task LoadGuildInfo() => Run(async (chat, token) =>
    {
        await chat.RequestAsync<GetGuildChatTabInfoRequest, GetGuildChatTabInfoResponse>(new(), token);
        var announcements = await chat.RequestAsync<GetAnnounceChatRequest, GetAnnounceChatResponse>(new(), token);
        var surveys = await chat.RequestAsync<GetGuildSurveyListRequest, GetGuildSurveyListResponse>(new(), token);
        var notes = await chat.RequestAsync<GetGuildNoteListRequest, GetGuildNoteListResponse>(new(), token);
        return (surveys, announcements, notes);
    }, data =>
    {
        _surveys = data.surveys.SurveyList?.OrderByDescending(s => s.CreateLocalTimestamp).ToList() ?? [];
        _announcements = data.announcements.GuildChatInfoList?.Where(a => a.GuildChatInfo?.ChatInfo != null).ToList() ?? [];
        _guildNotes = data.notes.GuildNoteInfoList?.OrderBy(n => n.Order).ThenBy(n => n.GuildNoteId).ToList() ?? [];
    });

    private Task LoadGuildNotes() => Run((chat, token) => chat.RequestAsync<GetGuildNoteListRequest, GetGuildNoteListResponse>(new(), token), response =>
        _guildNotes = response.GuildNoteInfoList?.OrderBy(n => n.Order).ThenBy(n => n.GuildNoteId).ToList() ?? []);

    private void BeginGuildNote(GuildNoteInfo? note = null)
    {
        if (_busy || !_snapshot.Connected) return;
        if (note == null ? !CanManageGuildNotes || _guildNotes.Count >= OrtegaConst.GuildNote.MaxGuildNoteCount : !CanEditGuildNote(note))
        { _error = ResourceStrings.GuildNoteNoPermissionOrLimit; return; }
        _editingGuildNoteId = note?.GuildNoteId ?? 0;
        _noteTitle = note?.Title ?? "";
        _noteContent = note?.Content ?? "";
        _noteCategory = note?.GuildNoteCategoryType ?? GuildNoteCategoryType.Normal;
        _focusGuildNoteTitle = true;
    }

    private void CancelGuildNoteEdit() { _editingGuildNoteId = null; _focusGuildNoteTitle = false; _noteTitle = _noteContent = ""; }

    private Task SaveGuildNote()
    {
        if (_editingGuildNoteId is not { } id) return Task.CompletedTask;
        var title = _noteTitle;
        var content = _noteContent;
        var category = _noteCategory;
        return Run(async (chat, token) =>
        {
            ChatSession.ValidateGuildNote(title, content, category);
            if (id == 0)
            {
                if (!CanManageGuildNotes || _guildNotes.Count >= OrtegaConst.GuildNote.MaxGuildNoteCount)
                    throw new InvalidOperationException(ResourceStrings.GuildNoteNoPermissionOrLimit);
                var response = await chat.RequestAsync<CreateGuildNoteRequest, CreateGuildNoteResponse>(new()
                { Title = title, Content = content, GuildNoteCategoryType = category }, token);
                return (response.GuildNoteInfoList, response.IsNgWordReplaced);
            }
            var note = _guildNotes.Find(n => n.GuildNoteId == id);
            if (note == null || !CanEditGuildNote(note)) throw new InvalidOperationException(ResourceStrings.GuildNoteChangedOrNoPermission);
            var updated = await chat.RequestAsync<UpdateGuildNoteRequest, UpdateGuildNoteResponse>(new()
            { GuildNoteId = id, Title = title, Content = content, GuildNoteCategoryType = category }, token);
            return (updated.GuildNoteInfoList, updated.IsNgWordReplaced);
        }, response =>
        {
            _guildNotes = response.GuildNoteInfoList?.OrderBy(n => n.Order).ThenBy(n => n.GuildNoteId).ToList() ?? [];
            CancelGuildNoteEdit();
            _result = response.IsNgWordReplaced ? ResourceStrings.GuildNoteSavedReplaced : ResourceStrings.Save_successful;
        });
    }

    private Task DeleteGuildNote(GuildNoteInfo note)
    {
        var before = _guildNotes.Select(n => n.GuildNoteId).ToList();
        return Run(async (chat, token) =>
        {
            if (!before.Contains(note.GuildNoteId) || !CanDeleteGuildNote(note)) throw new InvalidOperationException(ResourceStrings.GuildNoteChangedOrNoPermission);
            if (await DialogService.ShowMessageBox(Tr("[GuildNoteConfirmationTitle]"), string.Format(ResourceStrings.DeleteGuildNoteConfirm, note.Title), yesText: Tr("[DialogCommonFormationRemoveButton]"), cancelText: Tr("[CommonCancelLabel]")) != true) return null;
            token.ThrowIfCancellationRequested();
            return await chat.RequestAsync<UpdateGuildNoteListRequest, UpdateGuildNoteListResponse>(new()
            { BeforeUpdateGuildNoteIdList = before, AfterUpdateGuildNoteIdList = before.Where(id => id != note.GuildNoteId).ToList() }, token);
        }, response =>
        {
            if (response == null) return;
            _guildNotes = response.GuildNoteInfoList?.OrderBy(n => n.Order).ThenBy(n => n.GuildNoteId).ToList() ?? [];
            if (_editingGuildNoteId == note.GuildNoteId) CancelGuildNoteEdit();
            _result = ResourceStrings.GuildNoteDeleted;
        });
    }

    private Task MoveGuildNote(GuildNoteInfo note, int direction)
    {
        var before = _guildNotes.Select(n => n.GuildNoteId).ToList();
        return Run((chat, token) =>
        {
            if (!CanSortGuildNotes) throw new InvalidOperationException(ResourceStrings.GuildPermissionDenied);
            var index = before.IndexOf(note.GuildNoteId);
            if (direction is not (-1 or 1) || index < 0 || index + direction < 0 || index + direction >= before.Count)
                throw new InvalidOperationException(ResourceStrings.GuildNoteMoveFailed);
            var after = new List<long>(before);
            (after[index], after[index + direction]) = (after[index + direction], after[index]);
            return chat.RequestAsync<UpdateGuildNoteListRequest, UpdateGuildNoteListResponse>(new()
            { BeforeUpdateGuildNoteIdList = before, AfterUpdateGuildNoteIdList = after }, token);
        }, response =>
        {
            _guildNotes = response.GuildNoteInfoList?.OrderBy(n => n.Order).ThenBy(n => n.GuildNoteId).ToList() ?? [];
            _result = Tr("[ChatAnnounceArrangementCompleteMessage]");
        });
    }

    private Task ShareGuildNote(GuildNoteInfo note) => Run((chat, token) =>
    {
        if (!CanManageGuildNotes) throw new InvalidOperationException(ResourceStrings.GuildPermissionDenied);
        return chat.RequestAsync<ShareGuildNoteRequest, ShareGuildNoteResponse>(new() { GuildNoteId = note.GuildNoteId }, token);
    }, _ => _result = ResourceStrings.GuildNoteShared);

    private Task ReactGuildNote(GuildNoteInfo note, ChatReactionType reaction)
    {
        var selected = note.MyChatReactionType == reaction ? ChatReactionType.None : reaction;
        return Run((chat, token) =>
        {
            if (reaction is < ChatReactionType.Wink or > ChatReactionType.Sad) throw new ArgumentOutOfRangeException(nameof(reaction));
            return chat.RequestAsync<ReactGuildNoteRequest, ReactGuildNoteResponse>(new() { GuildNoteId = note.GuildNoteId, ChatReactionType = selected }, token);
        }, _ =>
        {
            note.ChatReactionCountMap ??= new();
            if (note.MyChatReactionType != ChatReactionType.None)
                note.ChatReactionCountMap[note.MyChatReactionType] = Math.Max(0, note.ChatReactionCountMap.GetValueOrDefault(note.MyChatReactionType) - 1);
            if (selected != ChatReactionType.None) note.ChatReactionCountMap[selected] = note.ChatReactionCountMap.GetValueOrDefault(selected) + 1;
            note.MyChatReactionType = selected;
        });
    }

    private Task GuildNoteReactionDetails(GuildNoteInfo note) => Run(async (chat, token) =>
    {
        var response = await chat.RequestAsync<GetReactionDetailInfoRequest, GetReactionDetailInfoResponse>(new() { GuildNoteId = note.GuildNoteId }, token);
        return string.Join("\n", (response.PlayerInfoListByReactionType ?? []).Select(pair => DetailLine(ChatReactions.Name(pair.Key), pair.Value.Select(p => p.PlayerName))));
    }, details => _details = string.IsNullOrWhiteSpace(details) ? ResourceStrings.NoReactions : details);

    private async Task OpenGuildInfo()
    {
        if (_busy || _disposed) return;
        _announcementMenuOpen = false;
        _announcementMenus.Clear();
        _announcementMenuButtons.Clear();
        _showGuildInfo = true;
        var loading = LoadGuildInfo();
        StateHasChanged();
        if (_module != null) await _module.InvokeVoidAsync("labelGuildDialog");
        await loading;
    }

    private void GuildInfoKeyDown(KeyboardEventArgs args) { if (args.Key == "Escape") _showGuildInfo = false; }

    private async Task AnnouncementMenuKeyDown(KeyboardEventArgs args, (long, long) key)
    {
        if (args.Key != "Escape" || !_announcementMenus.TryGetValue(key, out var menu) || !menu.Open) return;
        await menu.CloseMenuAsync();
        if (_announcementMenuButtons.TryGetValue(key, out var button)) await button.FocusAsync();
    }

    private Task SetAnnouncement(ChatInfo message, bool announced) => Run(async (chat, token) =>
    {
        if (announced ? !CanAnnounce(message) : !CanDeleteAnnouncement(message)) throw new InvalidOperationException(ResourceStrings.GuildPermissionDenied);
        if (announced)
            await chat.RequestAsync<RegisterAnnounceChatRequest, RegisterAnnounceChatResponse>(new() { ChatIdentityInfo = ChatSession.Identity(message) }, token);
        else
            await chat.RequestAsync<DeleteAnnounceChatRequest, DeleteAnnounceChatResponse>(new() { ChatIdentityInfoList = [ChatSession.Identity(message)] }, token);
        return await chat.RequestAsync<GetAnnounceChatRequest, GetAnnounceChatResponse>(new(), token);
    }, response => _announcements = response.GuildChatInfoList?.Where(a => a.GuildChatInfo?.ChatInfo != null).ToList() ?? []);

    private bool VotingOpen(GuildSurveyInfo survey) => survey.VotingEndLocalTimestamp > DateTimeOffset.UtcNow.Add(NetworkManager.TimeManager.DiffFromUtc).ToUnixTimeMilliseconds();
    private static long VoteTotal(GuildSurveyInfo survey) => survey.VoteCountMap?.Values.Sum(count => (long)Math.Max(0, count)) ?? 0;
    private static int VotePercent(GuildSurveyInfo survey, GuildSurveyChoiceType choice)
    {
        var total = VoteTotal(survey);
        return total == 0 ? 0 : (int)Math.Round(100d * Math.Max(0, survey.VoteCountMap?.GetValueOrDefault(choice) ?? 0) / total);
    }

    private Task Vote(GuildSurveyInfo survey)
    {
        if (!_surveyChoices.TryGetValue(survey.SurveyGuid, out var choice) || survey.IsVoted || !VotingOpen(survey)) return Task.CompletedTask;
        return Run((chat, token) => chat.RequestAsync<VoteGuildSurveyRequest, VoteGuildSurveyResponse>(new()
        {
            SurveyGuid = survey.SurveyGuid, SelectedChoiceType = choice
        }, token), response => { _surveys = response.SurveyList?.OrderByDescending(s => s.CreateLocalTimestamp).ToList() ?? []; _result = ResourceStrings.VoteSubmitted; });
    }

    private Task SurveyDetails(GuildSurveyInfo survey)
    {
        if (_surveyDetails.Remove(survey.SurveyGuid)) return Task.CompletedTask;
        return Run(async (chat, token) =>
        {
            var response = await chat.RequestAsync<GetGuildSurveyDetailInfoRequest, GetGuildSurveyDetailInfoResponse>(new() { SurveyGuid = survey.SurveyGuid }, token);
            return string.Join("\n", (response.VotedPlayerInfoListByChoiceType ?? []).Select(pair => DetailLine(survey.ChoiceMap?.GetValueOrDefault(pair.Key), pair.Value.Select(p => p.PlayerName))));
        }, details => _surveyDetails[survey.SurveyGuid] = string.IsNullOrWhiteSpace(details) ? ResourceStrings.NoVotes : details);
    }

    private Task SaveSettings()
    {
        var settings = Funcs.UserSyncData.ChatSettingData?.DeepCopy();
        if (settings == null) { _error = ResourceStrings.ChatSettingsUnavailable; return Task.CompletedTask; }
        settings.FontSize = _fontSize;
        return Run((chat, token) => chat.RequestAsync<UpdateSettingsRequest, UpdateSettingsResponse>(new() { ChatSettingData = settings }, token), _ => _result = ResourceStrings.Save_successful);
    }

    private sealed record Appearance(int TextSize, int StickerSize);
    private async Task SaveAppearance()
    {
        _textSize = Math.Clamp(_textSize, 12, 24);
        _stickerSize = Math.Clamp(_stickerSize, 24, 96);
        AccountSelection.ChatAppearance = (_textSize, _stickerSize);
        if (_module == null) return;
        try
        {
            if (!await _module.InvokeAsync<bool>("saveAppearance", new Appearance(_textSize, _stickerSize)))
                _error = ResourceStrings.ChatBrowserSaveFailed;
        }
        catch (JSDisconnectedException) { }
    }
    private Task ResetAppearance() { _textSize = 16; _stickerSize = 48; return SaveAppearance(); }

    private Task DownloadBattle(ChatInfo message) => Run(async (chat, token) =>
    {
        var response = await chat.RequestAsync<GetChatBattleLogRequest, GetChatBattleLogResponse>(new()
        {
            TargetPlayerId = message.PlayerId, ChatBattlePropertyInfo = message.ChatBattleInfo.ChatBattlePropertyInfo
        }, token);
        token.ThrowIfCancellationRequested();
        await FileSaver.SaveFile(JsonConvert.SerializeObject(response), $"chat-battle-{message.PlayerId}-{message.LocalTimeStamp}.json");
    });

    private void PrepareBattleShare(ChatInfo message) { _shareBattle = message; _shareChannel = _channel; _shareTarget = _target > 0 ? _target.ToString() : ""; }

    private Task ShareBattle()
    {
        if (_shareBattle == null) return Task.CompletedTask;
        var property = _shareBattle.ChatBattleInfo.ChatBattlePropertyInfo;
        var channel = _shareChannel;
        var target = 0L;
        if (channel == ChatType.Private && (!long.TryParse(_shareTarget, out target) || target <= 0 || target == _snapshot.PlayerId))
        { _error = ResourceStrings.EnterOtherPlayerId; return Task.CompletedTask; }
        return Run((chat, token) => chat.RequestAsync<SendChatBattleLogRequest, SendChatBattleLogResponse>(new()
        {
            ChatType = channel, TargetPlayerId = target, ChatBattlePropertyInfo = property
        }, token), _ => { _shareBattle = null; _result = ResourceStrings.BattleReportShared; });
    }

    private static string ChannelName(ChatType type) => type switch
    {
        ChatType.World => Tr("[ChatTabAll]"), ChatType.Guild => Tr("[ChatTabGuild]"), ChatType.SvS => Tr("[ChatTabSvS]"), ChatType.Block => Tr("[ChatTabBlock]"),
        ChatType.Private => Tr("[ChatTabPrivate]"), _ => type.ToString()
    };
    private string GuildInfoTabs => $"{Tr("[ChatAnnounceListTabLabel]")} / {Tr("[ChatSurveyListTabLabel]")} / {Tr("[ChatGuildNoteTabLabel]")}";
    private static string DetailLine(string? label, IEnumerable<string> names) =>
        $"{Tr("[GuildSurveyChoiceLabelFormat]", label ?? "").TrimEnd()} {string.Join(ResourceStrings.ListSeparator, names)}";
    private static string BattleTypeName(BattleType? type) => type switch
    {
        null => "",
        BattleType t and (BattleType.Auto or BattleType.Boss or BattleType.BattleLeague or BattleType.LegendLeague or BattleType.FriendBattle) => Tr(t),
        BattleType.GuildBattle => Tr("[CommonHeaderGvgLabel]"),
        BattleType.GrandBattle => Tr("[CommonHeaderGlobalGvgLabel]"),
        BattleType.LocalRaid => Tr("[CommonHeaderLocalRaidLabel]"),
        BattleType.TowerBattle => Tr("[CommonHeaderTowerBattleLabel]"),
        BattleType.DungeonBattle => Tr("[CommonHeaderDungeonBattleLabel]"),
        BattleType.GuildRaid => Tr("[CommonHeaderGuildRaidLabel]"),
        BattleType.GuildTower => Tr("[GuildTowerTitle]"),
        _ => type.Value.ToString()
    };
    private static string PlayerName(string? name, long id) => !string.IsNullOrEmpty(name) ? name : id > 0 ? string.Format(ResourceStrings.PlayerIdFormat, id) : Tr("[GameSettingTypeSystem]");
    private static string FormatTime(long timestamp, string format = "MM-dd HH:mm")
    {
        if (timestamp <= 0) return "—";
        try { return DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime.ToString(format); }
        catch (ArgumentOutOfRangeException) { return ""; }
    }
    private static bool IsSystemMessage(ChatInfo message) => (message.SystemChatType != SystemChatType.None || message.PlayerId == 0)
        && message.ChatRecruitGuildMemberInfo == null && message.ChatBattleInfo == null && message.ChatMusicPlaylistInfo == null && message.ChatGuildNoteInfo == null;
    private static bool IsStickerMessage(ChatInfo message) => string.IsNullOrEmpty(message.SystemChatMessageKey)
        && message.ChatGuildNoteInfo == null
        && ChatEmoticons.Split(message.Message).All(part => string.IsNullOrWhiteSpace(part) || ChatEmoticons.TryGetId(part, out _))
        && !string.IsNullOrWhiteSpace(message.Message);
    private static string MessageText(ChatInfo message)
    {
        if (string.IsNullOrEmpty(message.SystemChatMessageKey)) return message.Message ?? "";
        try
        {
            var text = Masters.TextResourceTable.Get(message.SystemChatMessageKey, (message.SystemChatMessageArgs ?? []).Cast<object>().ToArray());
            text = Regex.Replace(text, @"\[[A-Za-z][A-Za-z0-9_]*\]", match => Masters.TextResourceTable.Get(match.Value));
            return Regex.Replace(text, @"</?(?:color|size|b|i)(?:=[^<>]*)?>", "");
        }
        catch (FormatException) { return message.Message ?? message.SystemChatMessageKey; }
    }
}
