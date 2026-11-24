using System.Text.Json;
using MementoMori.Ortega.Share.Data;
using MementoMori.Ortega.Common.Utils;
using MementoMori.Ortega.Share.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace MementoMori.Funcs;

/// <summary>One growth-history line. Kind: Reward (Type=ItemType, Id=ItemId, To=count), CharacterLevel / CharacterRarity (Id=CharacterId),
/// EquipmentReinforce (Id=EquipmentId), EquipmentUpgrade (Id=new EquipmentId, From=old EquipmentId), PlayerRank, MainQuest, Tower (Type=TowerType), Snapshot (Values).</summary>
public record GrowthEvent(DateTimeOffset Time, string Kind, long Type = 0, long Id = 0, long From = 0, long To = 0, Dictionary<string, long>? Values = null);

public partial class MementoMoriFuncs
{
    // ponytail: one global lock for all accounts' files; per-file locks if many accounts write at once.
    private static readonly object GrowthFileLock = new();
    private static readonly JsonSerializerOptions GrowthJson = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault };
    // ponytail: caps checked only when the file passes 2 MB; then keep all Snapshots within 90 days plus the last 5000 detail events.
    private const long GrowthMaxBytes = 2 * 1024 * 1024;
    private const int GrowthMaxLines = 5000; // well under 2 MB, so a trim is not repeated on every append
    private static readonly TimeSpan GrowthMaxAge = TimeSpan.FromDays(90);
    private DateTimeOffset _lastGrowthSnapshot;

    // Per-account in-memory cache so GrowthCard does not re-read and deserialize a multi-MB jsonl file on every UI refresh.
    private readonly object _growthCacheLock = new();
    private List<GrowthEvent>? _growthCache;
    private string? _growthCachePath;
    private long _growthCacheFileLength = -1;
    private DateTime _growthCacheWriteTime;
    private int _growthPendingWrites;
    private long _growthWriteVersion;

    private string GrowthHistoryPath
    {
        get
        {
            var dir = _serviceProvider?.GetService<IFileProvider>()?.GetFileInfo("GrowthHistory").PhysicalPath ?? Path.Combine(Directory.GetCurrentDirectory(), "GrowthHistory");
            return Path.Combine(dir, $"{UserId}.jsonl");
        }
    }

    /// <summary>Growth history of this account, oldest first. Read API for the UI.</summary>
    public List<GrowthEvent> GetGrowthHistory(DateTimeOffset? since = null)
    {
        var path = GrowthHistoryPath;
        long version;
        lock (_growthCacheLock)
        {
            if (_growthCache != null && _growthCachePath == path)
            {
                if (_growthPendingWrites > 0)
                    return FilterGrowthEvents(_growthCache, since);

                var info = new FileInfo(path);
                if (!info.Exists && _growthCacheFileLength == 0)
                    return FilterGrowthEvents(_growthCache, since);
                if (info.Exists && info.Length == _growthCacheFileLength && info.LastWriteTimeUtc == _growthCacheWriteTime)
                    return FilterGrowthEvents(_growthCache, since);
            }
            version = _growthWriteVersion;
        }

        string[] lines;
        long fileLength;
        DateTime writeTime;
        lock (GrowthFileLock)
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                lock (_growthCacheLock)
                {
                    if (_growthPendingWrites == 0 && _growthWriteVersion == version)
                    {
                        _growthCache = [];
                        _growthCachePath = path;
                        _growthCacheFileLength = 0;
                        _growthCacheWriteTime = default;
                    }
                }
                return new();
            }
            lines = File.ReadAllLines(path);
            info.Refresh();
            fileLength = info.Length;
            writeTime = info.LastWriteTimeUtc;
        }
        // appends run on the thread pool and may land slightly out of order

        var result = new List<GrowthEvent>(lines.Length);
        foreach (var line in lines)
        {
            try
            {
                var e = JsonSerializer.Deserialize<GrowthEvent>(line, GrowthJson);
                if (e != null) result.Add(e);
            }
            catch (JsonException) { } // a torn last line after a crash
        }
        result.Sort((a, b) => a.Time.CompareTo(b.Time));

        lock (_growthCacheLock)
        {
            if (_growthPendingWrites == 0 && _growthWriteVersion == version)
            {
                _growthCache = result;
                _growthCachePath = path;
                _growthCacheFileLength = fileLength;
                _growthCacheWriteTime = writeTime;
            }
        }

        return FilterGrowthEvents(result, since);
    }

    private static List<GrowthEvent> FilterGrowthEvents(List<GrowthEvent> events, DateTimeOffset? since) =>
        since == null ? events.ToList() : events.Where(e => e.Time >= since).ToList();

    /// <summary>Diffs incoming UserSyncData against current state BEFORE merge. Never throws.</summary>
    private List<GrowthEvent> BuildGrowthDiffEvents(UserSyncData incoming)
    {
        var events = new List<GrowthEvent>();
        try
        {
            if (incoming == null || UserId == 0) return events;
            var now = DateTimeOffset.UtcNow;
            var current = UserSyncData;

            // gains only (spent currency comes back negative); one line per item per response even if the server lists it twice
            foreach (var g in (incoming.GivenItemCountInfoList ?? new()).Where(i => i.ItemCount > 0).GroupBy(i => (i.ItemType, i.ItemId)))
                events.Add(new(now, "Reward", (long) g.Key.ItemType, g.Key.ItemId, 0, g.Sum(i => i.ItemCount)));

            if (current.UserCharacterDtoInfos != null)
            {
                var old = current.UserCharacterDtoInfos.DistinctBy(d => d.Guid).ToDictionary(d => d.Guid);
                foreach (var c in incoming.UserCharacterDtoInfos ?? new())
                {
                    if (!old.TryGetValue(c.Guid, out var o)) continue;
                    if (c.Level > o.Level) events.Add(new(now, "CharacterLevel", 0, c.CharacterId, o.Level, c.Level));
                    if (c.RarityFlags > o.RarityFlags) events.Add(new(now, "CharacterRarity", 0, c.CharacterId, (long) o.RarityFlags, (long) c.RarityFlags));
                }
            }

            if (current.UserEquipmentDtoInfos != null)
            {
                var old = current.UserEquipmentDtoInfos.DistinctBy(d => d.Guid).ToDictionary(d => d.Guid);
                foreach (var e in incoming.UserEquipmentDtoInfos ?? new())
                {
                    if (!old.TryGetValue(e.Guid, out var o)) continue;
                    if (e.EquipmentId != o.EquipmentId) events.Add(new(now, "EquipmentUpgrade", 0, e.EquipmentId, o.EquipmentId, e.EquipmentId));
                    else if (e.ReinforcementLv > o.ReinforcementLv) events.Add(new(now, "EquipmentReinforce", 0, e.EquipmentId, o.ReinforcementLv, e.ReinforcementLv));
                }
            }

            if (current.UserStatusDtoInfo != null && incoming.UserStatusDtoInfo != null && incoming.UserStatusDtoInfo.Rank > current.UserStatusDtoInfo.Rank)
                events.Add(new(now, "PlayerRank", 0, 0, current.UserStatusDtoInfo.Rank, incoming.UserStatusDtoInfo.Rank));

            if (current.UserBattleBossDtoInfo != null && incoming.UserBattleBossDtoInfo != null && incoming.UserBattleBossDtoInfo.BossClearMaxQuestId > current.UserBattleBossDtoInfo.BossClearMaxQuestId)
                events.Add(new(now, "MainQuest", 0, 0, current.UserBattleBossDtoInfo.BossClearMaxQuestId, incoming.UserBattleBossDtoInfo.BossClearMaxQuestId));

            if (current.UserTowerBattleDtoInfos != null)
                foreach (var t in incoming.UserTowerBattleDtoInfos ?? new())
                {
                    var o = current.UserTowerBattleDtoInfos.FirstOrDefault(d => d.TowerType == t.TowerType);
                    if (o != null && t.MaxTowerBattleId > o.MaxTowerBattleId) events.Add(new(now, "Tower", (long) t.TowerType, 0, o.MaxTowerBattleId, t.MaxTowerBattleId));
                }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "growth history diff failed");
        }
        return events;
    }

    /// <summary>Appends diff events and, if due, a post-merge Snapshot. Never throws.</summary>
    private void FinishRecordGrowth(List<GrowthEvent> events)
    {
        try
        {
            if (UserId == 0) return;
            var now = DateTimeOffset.UtcNow;
            var current = UserSyncData;

            // ponytail: "hourly" = first response after an hour passed, so idle accounts get no snapshot; add a timer job if gaps matter.
            if (now - _lastGrowthSnapshot >= TimeSpan.FromHours(1) && current.UserStatusDtoInfo != null)
            {
                _lastGrowthSnapshot = now;
                var collectionInfo = (current.UserCharacterCollectionDtoInfos ?? []).CalcCharacterCollectionParameterInfo();
                var values = new Dictionary<string, long>
                {
                    ["Rank"] = current.UserStatusDtoInfo.Rank,
                    ["MainQuest"] = current.UserBattleBossDtoInfo?.BossClearMaxQuestId ?? 0,
                    ["LevelLink"] = current.UserLevelLinkDtoInfo?.PartyLevel ?? 0,
                    // ponytail: battle power = sum of the top 5 characters, not the in-game deck power.
                    ["BattlePower"] = (current.UserCharacterDtoInfos ?? new())
                        .Select(c => { try { return BattlePowerCalculatorUtil.GetUserCharacterBattlePower(current, c, collectionInfo); } catch { return 0; } })
                        .OrderByDescending(x => x).Take(5).Sum()
                };
                foreach (var t in current.UserTowerBattleDtoInfos ?? new()) values[$"Tower{t.TowerType}"] = t.MaxTowerBattleId;
                events.Add(new(now, "Snapshot", Values: values));
            }

            if (events.Count == 0) return;
            var path = GrowthHistoryPath;
            var lines = events.Select(e => JsonSerializer.Serialize(e, GrowthJson)).ToList();
            lock (_growthCacheLock)
            {
                _growthWriteVersion++;
                _growthPendingWrites++;
                if (_growthCache != null && _growthCachePath == path)
                    _growthCache.AddRange(events);
            }
            _ = Task.Run(() => AppendGrowth(path, lines, now));
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "growth history record failed");
        }
    }

    internal static List<string> TrimGrowthLines(IEnumerable<string> rawLines, DateTimeOffset now, int maxDetailLines = GrowthMaxLines)
    {
        var cutoff = now - GrowthMaxAge;
        var snapshots = new List<(DateTimeOffset Time, string Line)>();
        var details = new List<(DateTimeOffset Time, string Line)>();
        foreach (var line in rawLines)
        {
            try
            {
                var ev = JsonSerializer.Deserialize<GrowthEvent>(line, GrowthJson);
                if (ev == null || ev.Time < cutoff) continue;
                if (ev.Kind == "Snapshot") snapshots.Add((ev.Time, line));
                else details.Add((ev.Time, line));
            }
            catch (JsonException) { }
        }

        // Preserve all Snapshots within GrowthMaxAge so the 30-day trend chart is never truncated by high-frequency Reward events.
        var kept = snapshots.Concat(details.TakeLast(maxDetailLines))
            .OrderBy(x => x.Time)
            .Select(x => x.Line)
            .ToList();
        return kept;
    }

    private void AppendGrowth(string path, List<string> lines, DateTimeOffset now)
    {
        try
        {
            lock (GrowthFileLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllLines(path, lines);
                var info = new FileInfo(path);
                var trimmed = false;
                if (info.Length > GrowthMaxBytes)
                {
                    var kept = TrimGrowthLines(File.ReadAllLines(path), now);
                    File.WriteAllLines(path + ".tmp", kept);
                    File.Move(path + ".tmp", path, true);
                    info.Refresh();
                    trimmed = true;
                }

                lock (_growthCacheLock)
                {
                    _growthPendingWrites = Math.Max(0, _growthPendingWrites - 1);
                    if (_growthCachePath == path)
                    {
                        if (trimmed)
                        {
                            _growthCache = null;
                            _growthCacheFileLength = -1;
                            _growthCacheWriteTime = default;
                        }
                        else
                        {
                            _growthCacheFileLength = info.Length;
                            _growthCacheWriteTime = info.LastWriteTimeUtc;
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            lock (_growthCacheLock)
            {
                _growthPendingWrites = Math.Max(0, _growthPendingWrites - 1);
                if (_growthCachePath == path)
                {
                    _growthCache = null;
                    _growthCacheFileLength = -1;
                    _growthCacheWriteTime = default;
                }
            }
            _logger.LogWarning(e, "growth history write failed");
        }
    }
}
