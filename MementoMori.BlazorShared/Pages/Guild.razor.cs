using System.Reactive.Disposables;
using System.Reactive.Linq;
using MementoMori.BlazorShared.Components;
using MementoMori.Common.Localization;
using MementoMori.Exceptions;
using MementoMori.Ortega.Share;
using MementoMori.Ortega.Share.Data.ApiInterface.Guild;
using MementoMori.Ortega.Share.Data.ApiInterface.GuildRaid;
using MementoMori.Ortega.Share.Data.GuildRaid;
using MementoMori.Ortega.Share.Data.Player;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using ReactiveUI;
using static MementoMori.BlazorShared.Extensions.MasterResourceExtension;

namespace MementoMori.BlazorShared.Pages;

public partial class Guild
{
    [Inject] public IDialogService DialogService { get; set; } = null!;
    [Inject] public NavigationManager NavigationManager { get; set; } = null!;
    [Inject] public ISnackbar Snackbar { get; set; } = null!;

    internal readonly record struct GvgDisplayStatus(bool IsActive, string Label);

    private static readonly DialogOptions ProfileDialogOptions = new() { FullWidth = true, MaxWidth = MaxWidth.Medium, CloseButton = false, NoHeader = true };

    private enum MemberSortMode { Role, Power, Fame, Activity }

    private static (MemberSortMode Mode, string Label)[] SortModes =>
    [
        (MemberSortMode.Role, ResourceStrings.GuildSortRole),
        (MemberSortMode.Power, ResourceStrings.GuildSortPower),
        (MemberSortMode.Fame, ResourceStrings.GuildSortFame),
        (MemberSortMode.Activity, ResourceStrings.GuildSortActivity)
    ];

    private GetGuildBaseInfoResponse? _baseInfo;
    private List<GuildRaidInfo> _raids = [];
    private List<PlayerInfo> _rawMembers = [];
    private List<PlayerInfo> _sortedMembers = [];
    private CancellationTokenSource? _loadCts;
    private MemberSortMode _sortMode = MemberSortMode.Role;
    private bool _interactive;
    private bool _disposed;
    private bool _loading;
    private bool _actionBusy;
    private bool _noGuild;
    private int _revision;
    private string? _error;
    private string _memberSearch = "";

    private List<PlayerInfo> SortedMembers => _sortedMembers;

    private List<PlayerInfo> FilteredMembers => string.IsNullOrWhiteSpace(_memberSearch)
        ? _sortedMembers
        : _sortedMembers.Where(m =>
            (m.PlayerName?.Contains(_memberSearch.Trim(), StringComparison.OrdinalIgnoreCase) ?? false)
            || m.PlayerId.ToString().Contains(_memberSearch.Trim(), StringComparison.Ordinal)).ToList();

    private bool IsGuildTowerOpen =>
        Masters.GuildTowerEventTable.GetByInTime(Funcs.TimeManager.IsInTime) != null
        || (_baseInfo?.GuildTowerCurrentFloor ?? 0) > 0;

    private bool CanOpenRaid
    {
        get
        {
            var pos = _baseInfo?.GuildSyncData?.PlayerGuildPositionType ?? PlayerGuildPositionType.None;
            return pos is not (PlayerGuildPositionType.None or PlayerGuildPositionType.Member);
        }
    }

    protected override async Task AccountChanged()
    {
        _revision++;
        CancelAndDisposeLoadCts();
        _baseInfo = null;
        _raids = [];
        _rawMembers = [];
        _sortedMembers = [];
        _noGuild = false;
        _error = null;
        _memberSearch = "";

        var revision = _revision;
        TrackAccountSubscription(Funcs.WhenAnyValue(f => f.LoginOk).Skip(1).Subscribe(ok =>
        {
            if (ok && _interactive && !_disposed && revision == _revision)
                _ = InvokeAsync(() => LoadAsync(revision));
            else if (!_disposed && revision == _revision)
                _ = InvokeAsync(StateHasChanged);
        }));
        TrackAccountSubscription(Funcs.WhenAnyValue(f => f.IsQuickActionExecuting).Skip(1).Subscribe(executing =>
        {
            if (!_disposed && revision == _revision) _ = InvokeAsync(StateHasChanged);
        }));

        if (_interactive && Funcs.LoginOk)
            await LoadAsync(revision);
        else
            await InvokeAsync(StateHasChanged);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        _interactive = true;
        if (Funcs.LoginOk) await LoadAsync(_revision);
    }

    public new void Dispose()
    {
        _disposed = true;
        CancelAndDisposeLoadCts();
        base.Dispose();
    }

    private CancellationToken NextLoadToken()
    {
        CancelAndDisposeLoadCts();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        return cts.Token;
    }

    private void CancelAndDisposeLoadCts()
    {
        var cts = Interlocked.Exchange(ref _loadCts, null);
        if (cts == null) return;
        try { cts.Cancel(); }
        finally { cts.Dispose(); }
    }

    private Task ReloadAsync() => LoadAsync(_revision);

    private void SetSortMode(MemberSortMode mode)
    {
        if (_sortMode == mode) return;
        _sortMode = mode;
        RebuildSortedMembers();
    }

    private void RebuildSortedMembers()
    {
        _sortedMembers = _sortMode switch
        {
            MemberSortMode.Power => _rawMembers.OrderByDescending(m => m.BattlePower).ThenBy(m => PositionOrder(m.PlayerGuildPositionType)).ToList(),
            MemberSortMode.Fame => _rawMembers.OrderByDescending(m => m.GuildPeriodTotalFame).ThenByDescending(m => m.BattlePower).ToList(),
            MemberSortMode.Activity => _rawMembers.OrderBy(m => m.LastLoginTime).ThenByDescending(m => m.BattlePower).ToList(),
            _ => _rawMembers.OrderBy(m => PositionOrder(m.PlayerGuildPositionType)).ThenByDescending(m => m.BattlePower).ToList()
        };
    }

    private async Task LoadAsync(int revision)
    {
        if (_disposed || revision != _revision || !Funcs.LoginOk) return;
        var token = NextLoadToken();
        _loading = true;
        _error = null;
        StateHasChanged();

        try
        {
            var guildIdResp = await Funcs.GetResponse<GetGuildIdRequest, GetGuildIdResponse>(new GetGuildIdRequest(), token);
            if (_disposed || revision != _revision) return;

            if (guildIdResp.GuildId <= 0)
            {
                _noGuild = true;
                _baseInfo = null;
                _raids = [];
                _rawMembers = [];
                _sortedMembers = [];
                return;
            }

            _noGuild = false;
            var baseResp = await Funcs.GetResponse<GetGuildBaseInfoRequest, GetGuildBaseInfoResponse>(
                new GetGuildBaseInfoRequest { BelongGuildId = guildIdResp.GuildId }, token);
            if (_disposed || revision != _revision) return;

            List<GuildRaidInfo> raids = [];
            try
            {
                var raidResp = await Funcs.GetResponse<GetGuildRaidInfoRequest, GetGuildRaidInfoResponse>(
                    new GetGuildRaidInfoRequest { BelongGuildId = guildIdResp.GuildId }, token);
                raids = raidResp.GuildRaidInfos ?? [];
            }
            catch (ApiErrorException e) when (e.ErrorCode is not (ErrorCode.CommonNoSession or ErrorCode.CommonLoggedInAnotherDevice))
            {
            }

            if (_disposed || revision != _revision) return;
            _baseInfo = baseResp;
            _raids = raids;
            _rawMembers = baseResp.GuildSyncData?.GuildPlayerInfoList ?? [];
            RebuildSortedMembers();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (!_disposed && revision == _revision) _error = e.Message;
        }
        finally
        {
            if (!_disposed && revision == _revision)
            {
                _loading = false;
                StateHasChanged();
            }
        }
    }

    private async Task RunActionAsync(Func<Task> action, string actionLabel, string? emptyResultMessage = null)
    {
        if (_actionBusy || Funcs.IsQuickActionExecuting) return;
        _actionBusy = true;
        _error = null;
        StateHasChanged();

        MementoMori.Funcs.LogEntry? beforeHead;
        lock (Funcs.MesssageList)
        {
            beforeHead = Funcs.MesssageList.FirstOrDefault();
        }

        try
        {
            await action();

            var newEntries = new List<MementoMori.Funcs.LogEntry>();
            lock (Funcs.MesssageList)
            {
                foreach (var entry in Funcs.MesssageList)
                {
                    if (ReferenceEquals(entry, beforeHead) || newEntries.Count >= 20) break;
                    newEntries.Add(entry);
                }
            }
            newEntries.Reverse();

            await LoadAsync(_revision);

            var firstError = newEntries.FirstOrDefault(e => e.IsError);
            if (firstError != null)
            {
                Snackbar.Add(firstError.Message, Severity.Error);
            }
            else if (newEntries.Count > 0)
            {
                var summary = string.Join(" · ", newEntries.TakeLast(2).Select(e => e.Message.Trim()).Where(s => s.Length > 0));
                Snackbar.Add(string.IsNullOrEmpty(summary) ? $"{actionLabel} {ResourceStrings.Finished}" : summary, Severity.Success);
            }
            else if (!string.IsNullOrEmpty(emptyResultMessage))
            {
                Snackbar.Add(emptyResultMessage, Severity.Info);
            }
            else
            {
                Snackbar.Add($"{actionLabel} {ResourceStrings.Finished}", Severity.Success);
            }
        }
        catch (Exception e)
        {
            _error = e.Message;
            Snackbar.Add(e.Message, Severity.Error);
        }
        finally
        {
            _actionBusy = false;
            if (!_disposed) StateHasChanged();
        }
    }

    private (GvgDisplayStatus Local, GvgDisplayStatus Global) ResolveGvgDisplayState()
    {
        var worldId = Funcs.WorldId;
        var serverNow = Funcs.TimeManager.ServerNow;
        var activeGroup = worldId > 0 ? Masters.WorldGroupTable.GetByWorldIdLocalDateTime(worldId, serverNow) : null;
        var hasWorldGroup = activeGroup != null;
        var isGrandBattleWeek = activeGroup != null && activeGroup.IsOpenedGrandBattle(serverNow);
        return ResolveGvgDisplayState(
            hasWorldGroup,
            isGrandBattleWeek,
            _baseInfo?.LocalGuildGvgInfo?.IsOpen == true,
            _baseInfo?.GlobalGuildGvgInfo?.IsOpen == true);
    }

    internal static (GvgDisplayStatus Local, GvgDisplayStatus Global) ResolveGvgDisplayState(
        bool hasWorldGroup,
        bool isGrandBattleWeek,
        bool localIsOpen,
        bool globalIsOpen)
    {
        if (hasWorldGroup)
        {
            if (isGrandBattleWeek)
            {
                return (
                    new GvgDisplayStatus(false, ResourceStrings.GuildGvgIntermission),
                    new GvgDisplayStatus(globalIsOpen, globalIsOpen ? ResourceStrings.GuildGvgQualified : ResourceStrings.GuildGvgNotQualified));
            }

            return (
                new GvgDisplayStatus(localIsOpen, localIsOpen ? ResourceStrings.GuildGvgQualified : ResourceStrings.GuildGvgNotQualified),
                new GvgDisplayStatus(false, ResourceStrings.GuildGvgNotQualified));
        }

        return (
            new GvgDisplayStatus(localIsOpen, localIsOpen ? ResourceStrings.GuildGvgQualified : ResourceStrings.GuildGvgNotQualified),
            new GvgDisplayStatus(globalIsOpen, globalIsOpen ? ResourceStrings.GuildGvgQualified : ResourceStrings.GuildGvgNotQualified));
    }

    internal static bool IsMemberOnline(TimeSpan elapsed) => elapsed < TimeSpan.FromMinutes(5);

    private void OpenGuildChat()
    {
        var userId = AccountInfo?.UserId ?? AccountSelection.CurrentUserId;
        if (userId > 0)
        {
            AccountSelection.PendingPrivateChatTargets.Remove(userId);
            AccountSelection.ChatChannels[userId] = ChatType.Guild;
        }
        NavigationManager.NavigateTo("Chat");
    }

    private Task OpenMemberProfile(PlayerInfo member) =>
        OpenPlayerProfile(member.PlayerId, member.PlayerName, member.MainCharacterIconId, member);

    private async Task OpenPlayerProfile(long playerId, string? fallbackName, long fallbackIconId, PlayerInfo? initialInfo = null)
    {
        if (!Funcs.LoginOk || playerId <= 0) return;
        var parameters = new DialogParameters<PlayerInfoDialog>
        {
            { x => x.PlayerId, playerId },
            { x => x.InitialPlayerInfo, initialInfo },
            { x => x.FallbackName, fallbackName },
            { x => x.FallbackIconId, fallbackIconId },
            { x => x.IsGlobal, false }
        };
        await DialogService.ShowAsync<PlayerInfoDialog>(ResourceStrings.PlayerProfileTitle, parameters, ProfileDialogOptions);
    }

    private GuildRaidBossMB? ResolveRaidBossMb(GuildRaidBossType bossType)
    {
        var all = Masters.GuildRaidBossTable.GetArray().Where(b => b.GuildRaidBossType == bossType).ToList();
        return all.FirstOrDefault(b => Funcs.TimeManager.IsInTime(b))
               ?? all.OrderByDescending(b => b.Id).FirstOrDefault();
    }

    private static string ResolveRaidBossName(GuildRaidInfo raid, GuildRaidBossMB? bossMb)
    {
        if (!string.IsNullOrWhiteSpace(raid.GuildRaidBossInfo?.Name))
            return raid.GuildRaidBossInfo.Name;
        if (bossMb != null && !string.IsNullOrWhiteSpace(bossMb.NameKey))
        {
            var resolved = Tr(bossMb.NameKey);
            if (!string.IsNullOrWhiteSpace(resolved) && resolved != bossMb.NameKey) return resolved;
        }
        return raid.GuildRaidDtoInfo?.BossType == GuildRaidBossType.Releasable
            ? $"{Tr("[CommonHeaderGuildRaidLabel]")} ({Tr("[GuildRaidRelease]")})"
            : Tr("[CommonHeaderGuildRaidLabel]");
    }

    private static string FormatQuest(long questId)
    {
        if (questId <= 0) return "-";
        var quest = Masters.QuestTable.GetById(questId);
        return !string.IsNullOrWhiteSpace(quest?.Memo) ? quest.Memo : questId.ToString();
    }

    internal static int PositionOrder(PlayerGuildPositionType position) => position switch
    {
        PlayerGuildPositionType.Leader => 0,
        PlayerGuildPositionType.SubLeader => 1,
        PlayerGuildPositionType.Commander => 2,
        PlayerGuildPositionType.Veteran => 3,
        PlayerGuildPositionType.Member => 4,
        _ => 9
    };

    private static string PositionLabel(PlayerGuildPositionType position) => position switch
    {
        PlayerGuildPositionType.Leader => Tr("[PlayerGuildPositionTypeLeader]"),
        PlayerGuildPositionType.SubLeader => Tr("[PlayerGuildPositionTypeSubLeader]"),
        PlayerGuildPositionType.Commander => Tr("[PlayerGuildPositionTypeCommander]"),
        PlayerGuildPositionType.Veteran => Tr("[PlayerGuildPositionTypeVeteran]"),
        PlayerGuildPositionType.Member => Tr("[PlayerGuildPositionTypeMember]"),
        _ => "-"
    };

    private static string RoleClass(PlayerGuildPositionType position) => position switch
    {
        PlayerGuildPositionType.Leader => "is-leader",
        PlayerGuildPositionType.SubLeader => "is-subleader",
        PlayerGuildPositionType.Commander => "is-commander",
        PlayerGuildPositionType.Veteran => "is-veteran",
        _ => "is-member"
    };

    private static string FormatCompact(long value) => value switch
    {
        >= 1_000_000_000 => $"{value / 1_000_000_000d:0.##}B",
        >= 1_000_000 => $"{value / 1_000_000d:0.##}M",
        >= 10_000 => $"{value / 1_000d:0.#}K",
        _ => value.ToString("N0")
    };

    internal static string FormatSinceLogin(TimeSpan elapsed)
    {
        if (elapsed >= TimeSpan.FromDays(7)) return "7d+";
        var totalMinutes = (long)Math.Max(0, elapsed.TotalMinutes);
        if (totalMinutes < 60) return $"{totalMinutes}m";
        if (totalMinutes < 60 * 24) return $"{totalMinutes / 60}h";
        return $"{totalMinutes / 1440}d";
    }
}
