using System.Text.Json;
using MementoMori.Ortega.Share.Data;
using MementoMori.Ortega.Common.Utils;
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
    // ponytail: caps checked only when the file passes 2 MB; then keep the last 90 days, at most 10000 lines.
    private const long GrowthMaxBytes = 2 * 1024 * 1024;
    private const int GrowthMaxLines = 5000; // well under 2 MB, so a trim is not repeated on every append
    private static readonly TimeSpan GrowthMaxAge = TimeSpan.FromDays(90);
    private DateTimeOffset _lastGrowthSnapshot;

    private string GrowthHistoryPath
    {
        get
        {
            var dir = _serviceProvider.GetService<IFileProvider>()?.GetFileInfo("GrowthHistory").PhysicalPath ?? Path.Combine(Directory.GetCurrentDirectory(), "GrowthHistory");
            return Path.Combine(dir, $"{UserId}.jsonl");
        }
    }

    /// <summary>Growth history of this account, oldest first. Read API for the UI.</summary>
    public List<GrowthEvent> GetGrowthHistory(DateTimeOffset? since = null)
    {
        var path = GrowthHistoryPath;
        string[] lines;
        lock (GrowthFileLock)
        {
            if (!File.Exists(path)) return new();
            lines = File.ReadAllLines(path);
        }
        // appends run on the thread pool and may land slightly out of order

        var result = new List<GrowthEvent>();
        foreach (var line in lines)
        {
            try
            {
                var e = JsonSerializer.Deserialize<GrowthEvent>(line, GrowthJson);
                if (e != null && (since == null || e.Time >= since)) result.Add(e);
            }
            catch (JsonException) { } // a torn last line after a crash
        }
        return result.OrderBy(e => e.Time).ToList();
    }

    /// <summary>Called with each response's UserSyncData before it is merged; diffs it against the current state. Never throws.</summary>
    private void RecordGrowth(UserSyncData incoming)
    {
        try
        {
            if (incoming == null || UserId == 0) return;
            var now = DateTimeOffset.UtcNow;
            var current = UserSyncData;
            var events = new List<GrowthEvent>();

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

            // ponytail: "hourly" = first response after an hour passed, so idle accounts get no snapshot; add a timer job if gaps matter.
            if (now - _lastGrowthSnapshot >= TimeSpan.FromHours(1) && current.UserStatusDtoInfo != null)
            {
                _lastGrowthSnapshot = now;
                var values = new Dictionary<string, long>
                {
                    ["Rank"] = current.UserStatusDtoInfo.Rank,
                    ["MainQuest"] = current.UserBattleBossDtoInfo?.BossClearMaxQuestId ?? 0,
                    ["LevelLink"] = current.UserLevelLinkDtoInfo?.PartyLevel ?? 0,
                    // ponytail: battle power = sum of the top 5 characters, not the in-game deck power.
                    ["BattlePower"] = (current.UserCharacterDtoInfos ?? new())
                        .Select(c => { try { return BattlePowerCalculatorUtil.GetUserCharacterBattlePower(UserId, c); } catch { return 0; } })
                        .OrderByDescending(x => x).Take(5).Sum()
                };
                foreach (var t in current.UserTowerBattleDtoInfos ?? new()) values[$"Tower{t.TowerType}"] = t.MaxTowerBattleId;
                events.Add(new(now, "Snapshot", Values: values));
            }

            if (events.Count == 0) return;
            var path = GrowthHistoryPath;
            var lines = events.Select(e => JsonSerializer.Serialize(e, GrowthJson)).ToList();
            _ = Task.Run(() => AppendGrowth(path, lines, now));
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "growth history record failed");
        }
    }

    private void AppendGrowth(string path, List<string> lines, DateTimeOffset now)
    {
        try
        {
            lock (GrowthFileLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllLines(path, lines);
                if (new FileInfo(path).Length <= GrowthMaxBytes) return;

                var kept = File.ReadAllLines(path)
                    .Where(l => { try { return JsonSerializer.Deserialize<GrowthEvent>(l, GrowthJson)!.Time >= now - GrowthMaxAge; } catch { return false; } })
                    .TakeLast(GrowthMaxLines).ToList();
                File.WriteAllLines(path + ".tmp", kept);
                File.Move(path + ".tmp", path, true);
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "growth history write failed");
        }
    }
}
