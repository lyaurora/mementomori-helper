using AutoCtor;
using Injectio.Attributes;
using MementoMori.Option;
using MementoMori.Ortega.Share.Data.Battle.Result;
using Microsoft.Extensions.Logging;

namespace MementoMori;

[AutoConstruct]
[RegisterSingleton<BattleLogManager>]
public partial class BattleLogManager
{
    private readonly IWritableOptions<GameConfig> _writableGameConfig;
    private readonly ILogger<BattleLogManager> _logger;

    public async Task SaveBattleLog(BattleResult battleResult, string prefix, string middle, string autoDeletePrefix = null, int autoDeletePreserveCount = 20)
    {
        if (!_writableGameConfig.Value.RecordBattleLog) return;
        // Logging a battle must never change battle-loop control flow.
        try
        {
            Directory.CreateDirectory(_writableGameConfig.Value.BattleLogDir);
            var res = battleResult.SimulationResult.BattleEndInfo.IsWinAttacker() ? "win" : "lose";
            var filename = $"{prefix}-{middle}-{res}-{battleResult.BattleTime.StartBattle}-{battleResult.SimulationResult.BattleToken}.json";
            filename = Path.GetInvalidFileNameChars().Aggregate(filename, (current, invalidFileNameChar) => current.Replace(invalidFileNameChar, '_'));
            var path = Path.Combine(_writableGameConfig.Value.BattleLogDir, filename);
            await File.WriteAllTextAsync(path, battleResult.ToJson(true));
            // keep only 100 lose logs
            var files = Directory.GetFiles(_writableGameConfig.Value.BattleLogDir, $"{autoDeletePrefix ?? prefix}-*.json").OrderByDescending(File.GetLastWriteTimeUtc);
            foreach (var file in files.Skip(autoDeletePreserveCount))
            {
                File.Delete(file);
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to save battle log {Prefix}-{Middle}", prefix, middle);
        }
    }
}