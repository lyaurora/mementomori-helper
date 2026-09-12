using AutoCtor;

using Quartz;

namespace MementoMori.Jobs;

[AutoConstruct]
public partial class DailyJob: IJob
{
    private readonly AccountManager _accountManager;

    public async Task Execute(IJobExecutionContext context)
    {
        var userId = context.MergedJobDataMap.GetLongValue("userId");
        if (userId <= 0) return;
        if (!_accountManager.TryGet(userId, out var account)) return;
        await account.Funcs.ExecuteScheduledJob(account.Funcs.ExecuteAllQuickAction, context.CancellationToken, jobName: context.JobDetail.Key.Name);
    }
}