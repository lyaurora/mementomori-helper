using AutoCtor;
using MementoMori.Option;
using Quartz;

namespace MementoMori.Jobs;

[AutoConstruct]
public partial class LegendLeagueJob : IJob
{
    private readonly AccountManager _accountManager;
    private readonly IWritableOptions<GameConfig> _gameConfig;

    public async Task Execute(IJobExecutionContext context)
    {
        if (!_gameConfig.Value.AutoJob.AutoLegendLeague) return;

        var userId = context.MergedJobDataMap.GetLongValue("userId");
        if (userId <= 0) return;
        if (!_accountManager.TryGet(userId, out var account)) return;
        await account.Funcs.ExecuteScheduledJob(account.Funcs.LegendLeagueAuto, context.CancellationToken, () => _gameConfig.Value.AutoJob.AutoLegendLeague, context.JobDetail.Key.Name);
    }
}