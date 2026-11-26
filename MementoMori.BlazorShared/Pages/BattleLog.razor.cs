using MementoMori.Ortega.Share;
using MementoMori.Ortega.Share.Data.Battle.Result;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Extensions;
using MementoMori.Common.Localization;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using static MementoMori.BlazorShared.Extensions.MasterResourceExtension;

namespace MementoMori.BlazorShared.Pages;

public partial class BattleLog
{
    public sealed record BattleLogEntry(
        string FileName,
        string FullPath,
        BattleLogType Type,
        string KindLabel,
        string Target,
        bool Win,
        DateTimeOffset Time,
        DateTime LastWriteUtc);

    private List<BattleLogEntry> _entries = [];
    private List<BattleLogEntry> _visibleEntries = [];
    private List<BattleLogType> _presentTypes = [];
    private BattleLogType? _filterType;
    private BattleLogEntry? _selectedEntry;
    private BattleResult? _selectedResult;
    private string? _parseError;
    private bool _showReplay;
    private bool _pendingReplayPush;

    private IReadOnlyList<BattleLogEntry> VisibleEntries => _visibleEntries;

    private List<string> BattleResults { get; set; } = new();

    private BattleLogType selectedBattleLogType
    {
        get => _selectedBattleLogType;
        set
        {
            _selectedBattleLogType = value;
            GetLogs();
        }
    }

    private string? SelectedBattleResult { get; set; }

    protected override Task OnInitializedAsync()
    {
        ReloadLogs();
        return Task.CompletedTask;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_pendingReplayPush || !_showReplay || _selectedResult == null) return;
        _pendingReplayPush = false;
        await JS.InvokeVoidAsync("showBattleLog", SerializeViewerJson(_selectedResult));
    }

    private void ReloadLogs()
    {
        var previousFile = _selectedEntry?.FileName;
        LoadEntries();
        if (previousFile != null && VisibleEntries.FirstOrDefault(e => e.FileName == previousFile) is { } existing)
            SelectEntry(existing);
        else if (VisibleEntries.Count > 0)
            SelectEntry(VisibleEntries[0]);
        else
            ClearSelection();
    }

    private void LoadEntries()
    {
        _entries.Clear();
        _presentTypes.Clear();
        var dir = GameConfig.Value.BattleLogDir;
        if (!Directory.Exists(dir))
        {
            _visibleEntries = [];
            return;
        }

        foreach (var path in Directory.EnumerateFiles(dir).Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            if (ParseLogEntry(path) is { } entry) _entries.Add(entry);
        }

        _entries = _entries
            .OrderByDescending(e => e.Time)
            .ThenByDescending(e => e.LastWriteUtc)
            .ToList();
        _presentTypes = _entries.Select(e => e.Type).Distinct().ToList();
        if (_filterType is { } active && !_presentTypes.Contains(active)) _filterType = null;
        RefreshVisibleEntries();
    }

    private void RefreshVisibleEntries()
    {
        _visibleEntries = _filterType is { } t ? _entries.Where(e => e.Type == t).ToList() : _entries;
    }

    internal static BattleLogEntry? ParseLogEntry(string fullPath)
    {
        var fileName = Path.GetFileName(fullPath);
        foreach (var (type, prefix) in LogTypePrefixes)
        {
            if (!fileName.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var details = GetLogDetails(fileName, type);
            if (details is not { } d) return null;
            DateTime lastWrite;
            try { lastWrite = File.GetLastWriteTimeUtc(fullPath); }
            catch (IOException) { lastWrite = d.Time.UtcDateTime; }
            return new BattleLogEntry(fileName, fullPath, type, BattleLogTypeName(type), d.Name, d.Result == "win", d.Time, lastWrite);
        }

        return null;
    }

    private void SetFilter(BattleLogType? type)
    {
        _filterType = _filterType == type ? null : type;
        RefreshVisibleEntries();
        if (_selectedEntry == null || !_visibleEntries.Contains(_selectedEntry))
        {
            if (_visibleEntries.Count > 0) SelectEntry(_visibleEntries[0]);
            else ClearSelection();
        }
    }

    private void SelectEntry(BattleLogEntry entry)
    {
        _selectedEntry = entry;
        SelectedBattleResult = entry.FileName;
        _parseError = null;
        try
        {
            _selectedResult = JsonConvert.DeserializeObject<BattleResult>(File.ReadAllText(entry.FullPath));
            if (_selectedResult == null) _parseError = ResourceStrings.BattleEmpty;
            if (_showReplay && _selectedResult != null) _pendingReplayPush = true;
        }
        catch (Exception e)
        {
            _selectedResult = null;
            _parseError = e.Message;
        }
    }

    private void ClearSelection()
    {
        _selectedEntry = null;
        _selectedResult = null;
        _parseError = null;
        SelectedBattleResult = null;
        _showReplay = false;
        _pendingReplayPush = false;
    }

    private void ToggleReplay()
    {
        _showReplay = !_showReplay;
        if (_showReplay && _selectedResult != null) _pendingReplayPush = true;
    }

    private void GetLogs()
    {
        var files = Directory.Exists(GameConfig.Value.BattleLogDir)
            ? Directory.GetFiles(GameConfig.Value.BattleLogDir)
            : [];
        var prefix = LogTypePrefixes[selectedBattleLogType];
        BattleResults.Clear();
        foreach (var file in files.OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var fileName = Path.GetFileName(file);
            if (!fileName.StartsWith(prefix, StringComparison.Ordinal)) continue;
            BattleResults.Add(fileName);
        }

        SelectedBattleResult = BattleResults.FirstOrDefault();
    }

    private (string Name, string Result, DateTimeOffset Time)? GetLogDetails(string filename) =>
        GetLogDetails(filename, selectedBattleLogType);

    internal static (string Name, string Result, DateTimeOffset Time)? GetLogDetails(string filename, BattleLogType type)
    {
        var parts = Path.GetFileNameWithoutExtension(filename).Split('-');
        var prefix = LogTypePrefixes[type];
        var descriptor = parts.Length >= 4 ? string.Join("-", parts[..^3]) : string.Empty;
        if (!filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || !filename.StartsWith(prefix, StringComparison.Ordinal) || descriptor.Length <= prefix.Length
            || parts[^3] is not ("win" or "lose")
            || !long.TryParse(parts[^2], out var timestamp) || timestamp is < 0 or > 253402300799999
            || !Guid.TryParseExact(parts[^1], "N", out _)) return null;
        return (descriptor[prefix.Length..], parts[^3], DateTimeOffset.FromUnixTimeMilliseconds(timestamp));
    }

    private static string SerializeViewerJson(BattleResult result) =>
        JsonConvert.SerializeObject(new { BattleSimulationResult = result.SimulationResult });

    private async Task DownloadBattleLog(string? filename)
    {
        if (string.IsNullOrEmpty(filename)) return;
        string? json;
        if (_selectedEntry?.FileName == filename && _selectedResult != null)
            json = SerializeViewerJson(_selectedResult);
        else if (!ReadLogContent(filename, out json))
            return;

        try
        {
            await FileSaver.SaveFile(json!, filename);
        }
        catch (Exception e)
        {
            await DialogService.ShowMessageBoxAsync(Tr("[CommonErrorLabel]"), e.Message, yesText: Tr("[CommonOkLabel]"));
        }
    }

    private bool ReadLogContent(string filename, out string? json)
    {
        try
        {
            json = null;
            if (string.IsNullOrEmpty(filename)) return false;
            var path = Path.Combine(GameConfig.Value.BattleLogDir, filename);
            var content = File.ReadAllText(path);
            var battleResult = JsonConvert.DeserializeObject<BattleResult>(content);
            if (battleResult == null) return false;
            json = SerializeViewerJson(battleResult);
            return true;
        }
        catch (Exception e)
        {
            _ = DialogService.ShowMessageBoxAsync(Tr("[CommonErrorLabel]"), e.Message, yesText: Tr("[CommonOkLabel]"));
            json = null;
            return false;
        }
    }

    private async Task CleanBattleLogs(int days)
    {
        var dir = GameConfig.Value.BattleLogDir;
        if (!Directory.Exists(dir)) return;

        var threshold = days switch
        {
            1 => DateTimeOffset.Now.AddDays(-1),
            7 => DateTimeOffset.Now.AddDays(-7),
            30 => DateTimeOffset.Now.AddDays(-30),
            _ => DateTimeOffset.MinValue
        };

        var filesToDelete = days == 0
            ? Directory.GetFiles(dir).ToList()
            : Directory.GetFiles(dir)
                .Where(f => File.GetLastWriteTimeUtc(f) < threshold.UtcDateTime)
                .ToList();

        if (filesToDelete.Count == 0)
        {
            await DialogService.ShowMessageBoxAsync(ResourceStrings.BattleLogCleanRange, ResourceStrings.BattleLogNoFilesToClean, yesText: Tr("[CommonOkLabel]"));
            return;
        }

        long totalSize = 0;
        foreach (var f in filesToDelete)
        {
            try { totalSize += new FileInfo(f).Length; }
            catch (IOException) { }
        }

        var result = await DialogService.ShowMessageBoxAsync(
            ResourceStrings.BattleLogCleanRange,
            string.Format(ResourceStrings.BattleLogCleanConfirm, filesToDelete.Count, FormatFileSize(totalSize)),
            yesText: ResourceStrings.Confirm, cancelText: ResourceStrings.Cancel);

        if (result != true) return;

        var deletedCount = 0;
        foreach (var file in filesToDelete)
        {
            try
            {
                File.Delete(file);
                deletedCount++;
            }
            catch (Exception ex)
            {
                await DialogService.ShowMessageBoxAsync(Tr("[CommonErrorLabel]"),
                    string.Format(ResourceStrings.BattleLogCleanError, file, ex.Message), yesText: Tr("[CommonOkLabel]"));
            }
        }

        ReloadLogs();
        await DialogService.ShowMessageBoxAsync(ResourceStrings.Finished,
            string.Format(ResourceStrings.BattleLogCleanComplete, deletedCount), yesText: Tr("[CommonOkLabel]"));
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var order = 0;
        while (value >= 1024 && order < sizes.Length - 1)
        {
            order++;
            value /= 1024;
        }

        return $"{value:0.##} {sizes[order]}";
    }

    internal static List<BattleCharacterReport> GetSideReports(BattleSimulationResult sim, BattleFieldCharacterGroupType side) =>
        (sim.BattleCharacterReports ?? [])
            .Where(r => r.GroupType == side)
            .OrderByDescending(r => r.TotalGiveDamage)
            .ToList();

    internal static string GetUnitName(BattleCharacterReport r)
    {
        if (r.UnitType is UnitType.Character or UnitType.ShareCharacter)
        {
            if (Masters.CharacterTable.GetById(r.UnitId) != null)
            {
                Masters.CharacterTable.GetCharacterName(r.UnitId, out var name1, out var name2);
                if (!name1.IsNullOrEmpty()) return name2.IsNullOrEmpty() ? name1 : $"[{name2}] {name1}";
            }

            return r.UnitId.ToString();
        }

        var key = r.UnitType switch
        {
            UnitType.BossBattleEnemy => Masters.BossBattleEnemyTable.GetById(r.UnitId)?.NameKey,
            UnitType.AutoBattleEnemy => Masters.AutoBattleEnemyTable.GetById(r.UnitId)?.NameKey,
            UnitType.TowerBattleEnemy => Masters.TowerBattleEnemyTable.GetById(r.UnitId)?.NameKey,
            UnitType.GuildTowerEnemy => Masters.GuildTowerEnemyTable.GetById(r.UnitId)?.NameKey,
            _ => null
        };
        if (!string.IsNullOrEmpty(key)) return Masters.TextResourceTable.Get(key);

        // Large PvE enemy tables are skipped at startup to save ~71 MB RAM; fall back to a clean enemy label.
        return $"{ResourceStrings.BattleEnemy} #{r.UnitId}";
    }

    internal static string FormatShortNumber(long value)
    {
        var abs = Math.Abs(value);
        if (abs >= 1_000_000_000_000L) return $"{value / 1_000_000_000_000d:0.#}T";
        if (abs >= 1_000_000_000L) return $"{value / 1_000_000_000d:0.#}B";
        if (abs >= 1_000_000L) return $"{value / 1_000_000d:0.#}M";
        if (abs >= 1_000L) return $"{value / 1_000d:0.#}K";
        return value.ToString();
    }

    public enum BattleLogType
    {
        Main,
        TowerInfinite,
        TowerBlue,
        TowerRed,
        TowerGreen,
        TowerYellow,
        BattleLeague,
        LegendLeague
    }

    private static string BattleLogTypeName(BattleLogType type) => type switch
    {
        BattleLogType.Main => Tr("[CommonHeaderAutoBattleLabel]"),
        BattleLogType.TowerInfinite => Tr("[TowerTypeInfinite]"),
        BattleLogType.TowerBlue => Tr("[TowerTypeBlue]"),
        BattleLogType.TowerRed => Tr("[TowerTypeRed]"),
        BattleLogType.TowerGreen => Tr("[TowerTypeGreen]"),
        BattleLogType.TowerYellow => Tr("[TowerTypeYellow]"),
        BattleLogType.BattleLeague => Tr("[BattleTypeBattleLeague]"),
        BattleLogType.LegendLeague => Tr("[BattleTypeLegendLeague]"),
        _ => type.ToString()
    };

    private static readonly Dictionary<BattleLogType, string> LogTypePrefixes = new()
    {
        { BattleLogType.Main, "main-" },
        { BattleLogType.TowerInfinite, "tower-Infinite-" },
        { BattleLogType.TowerBlue, "tower-Blue-" },
        { BattleLogType.TowerRed, "tower-Red-" },
        { BattleLogType.TowerGreen, "tower-Green-" },
        { BattleLogType.TowerYellow, "tower-Yellow-" },
        { BattleLogType.BattleLeague, "battleleague-" },
        { BattleLogType.LegendLeague, "legendleague-" },
    };

    private BattleLogType _selectedBattleLogType = BattleLogType.Main;
}
