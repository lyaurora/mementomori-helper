using MementoMori.Ortega.Share.Data.Battle.Result;
using MementoMori.Common.Localization;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using static MementoMori.BlazorShared.Extensions.MasterResourceExtension;

namespace MementoMori.BlazorShared.Pages;

public partial class BattleLog
{
    private List<string> BattleResults { get; set; } = new();

    private BattleLogType selectedBattleLogType
    {
        get => _selectedBattleLogType;
        set
        {
            _selectedBattleLogType = value;
            this.GetLogs();
        }
    }

    private string SelectedBattleResult { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (!Directory.Exists(GameConfig.Value.BattleLogDir))
        {
            return;
        }

        GetLogs();
    }

    private void GetLogs()
    {
        var files = Directory.Exists(GameConfig.Value.BattleLogDir)
            ? Directory.GetFiles(GameConfig.Value.BattleLogDir) : [];
        var prefix = logTypePrefixes[selectedBattleLogType];
        BattleResults.Clear();
        foreach (var file in files.OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var fileName = Path.GetFileName(file);
            if (!fileName.StartsWith(prefix)) continue;
            BattleResults.Add(fileName);
        }

        SelectedBattleResult = BattleResults.FirstOrDefault();
    }

    private (string Name, string Result, DateTimeOffset Time)? GetLogDetails(string filename)
    {
        var parts = Path.GetFileNameWithoutExtension(filename).Split('-');
        var prefix = logTypePrefixes[selectedBattleLogType];
        var descriptor = parts.Length >= 4 ? string.Join("-", parts[..^3]) : string.Empty;
        if (!filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || !filename.StartsWith(prefix, StringComparison.Ordinal) || descriptor.Length <= prefix.Length
            || parts[^3] is not ("win" or "lose")
            || !long.TryParse(parts[^2], out var timestamp) || timestamp is < 0 or > 253402300799999
            || !Guid.TryParseExact(parts[^1], "N", out _)) return null;
        return (descriptor[prefix.Length..], parts[^3], DateTimeOffset.FromUnixTimeMilliseconds(timestamp));
    }

    private async Task DownloadBattleLog(string filename)
    {
        if (string.IsNullOrEmpty(filename)) return;
        if (!ReadLogContent(filename, out var json)) return;
        try
        {
            await FileSaver.SaveFile(json, filename);
        }
        catch (Exception e)
        {
            await DialogService.ShowMessageBox(Tr("[CommonErrorLabel]"), e.ToString(), yesText: Tr("[CommonOkLabel]"));
        }
    }

    private bool ReadLogContent(string filename, out string json)
    {
        try
        {
            json = null;
            if (string.IsNullOrEmpty(filename)) return false;
            var path = Path.Combine(GameConfig.Value.BattleLogDir, filename);
            var content = File.ReadAllText(path);
            var battleResult = JsonConvert.DeserializeObject<BattleResult>(content);
            if (battleResult == null)
            {
                return false;
            }

            var obj = new
            {
                BattleSimulationResult = battleResult.SimulationResult
            };
            json = JsonConvert.SerializeObject(obj);
            return true;
        }
        catch (Exception e)
        {
            DialogService.ShowMessageBox(Tr("[CommonErrorLabel]"), e.ToString(), yesText: Tr("[CommonOkLabel]"));
            json = null;
            return false;
        }
    }

    private async Task ViewBattleLog(string filename)
    {
        if (string.IsNullOrEmpty(filename)) return;
        if (!ReadLogContent(filename, out var json)) return;
        await JS.InvokeVoidAsync("showBattleLog", json);
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

    private Dictionary<BattleLogType, string> logTypePrefixes = new()
    {
        {BattleLogType.Main, "main-"},
        {BattleLogType.TowerInfinite, "tower-Infinite-"},
        {BattleLogType.TowerBlue, "tower-Blue-"},
        {BattleLogType.TowerRed, "tower-Red-"},
        {BattleLogType.TowerGreen, "tower-Green-"},
        {BattleLogType.TowerYellow, "tower-Yellow-"},
        {BattleLogType.BattleLeague, "battleleague-"},
        {BattleLogType.LegendLeague, "legendleague-"},
    };

    private BattleLogType _selectedBattleLogType = BattleLogType.Main;
    private int _selectedCleanRange = 1;

    private async Task CleanBattleLogs()
    {
        if (!Directory.Exists(GameConfig.Value.BattleLogDir)) return;

        var threshold = _selectedCleanRange switch
        {
            1 => DateTimeOffset.Now.AddDays(-1),
            7 => DateTimeOffset.Now.AddDays(-7),
            30 => DateTimeOffset.Now.AddDays(-30),
            _ => DateTimeOffset.MinValue
        };

        var filesToDelete = _selectedCleanRange == 0
            ? Directory.GetFiles(GameConfig.Value.BattleLogDir).ToList()
            : Directory.GetFiles(GameConfig.Value.BattleLogDir)
                .Where(f => File.GetLastWriteTimeUtc(f) < threshold.UtcDateTime)
                .ToList();

        if (!filesToDelete.Any())
        {
            await DialogService.ShowMessageBox(ResourceStrings.BattleLogCleanRange, ResourceStrings.BattleLogNoFilesToClean, yesText: Tr("[CommonOkLabel]"));
            return;
        }

        long totalSize = filesToDelete.Sum(f => new FileInfo(f).Length);

        string FormatFileSize(long bytes)
        {
            string[] sizes = {"B", "KB", "MB", "GB"};
            int order = 0;
            while (bytes >= 1024 && order < sizes.Length - 1)
            {
                order++;
                bytes /= 1024;
            }

            return $"{bytes:0.##} {sizes[order]}";
        }

        var result = await DialogService.ShowMessageBox(
            ResourceStrings.BattleLogCleanRange,
            string.Format(ResourceStrings.BattleLogCleanConfirm, filesToDelete.Count, FormatFileSize(totalSize)),
            yesText: ResourceStrings.Confirm, cancelText: ResourceStrings.Cancel);

        if (result != true) return;

        foreach (var file in filesToDelete)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                await DialogService.ShowMessageBox(Tr("[CommonErrorLabel]"),
                    string.Format(ResourceStrings.BattleLogCleanError, file, ex.Message), yesText: Tr("[CommonOkLabel]"));
            }
        }

        GetLogs();
        await DialogService.ShowMessageBox(ResourceStrings.Finished,
            string.Format(ResourceStrings.BattleLogCleanComplete, filesToDelete.Count), yesText: Tr("[CommonOkLabel]"));
    }
}
