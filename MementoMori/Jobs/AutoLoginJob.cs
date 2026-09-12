using AutoCtor;
using Quartz;

namespace MementoMori.Jobs;

[AutoConstruct]
[DisallowConcurrentExecution]
public partial class AutoLoginJob : IJob
{
    private readonly AccountManager _accountManager;

    public Task Execute(IJobExecutionContext context) => _accountManager.AutoLogin(context.CancellationToken);
}
