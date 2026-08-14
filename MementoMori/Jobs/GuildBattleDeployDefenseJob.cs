using AutoCtor;
using MementoMori.Option;
using Quartz;

namespace MementoMori.Jobs;

[AutoConstruct]
public partial class GuildBattleDeployDefenseJob : IJob
{
    private readonly AccountManager _accountManager;
    private readonly IWritableOptions<GameConfig> _gameConfig;

    public async Task Execute(IJobExecutionContext context)
    {
        if (!_gameConfig.Value.AutoJob.AutoDeployGuildDefense) return;
        var userId = context.MergedJobDataMap.GetLongValue("userId");
        if (userId <= 0) return;
        var account = _accountManager.Get(userId);
        await account.Funcs.ExecuteScheduledJob(account.Funcs.SetupLocalGvgDefense, context.CancellationToken, () => _gameConfig.Value.AutoJob.AutoDeployGuildDefense);
    }
}