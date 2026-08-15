using System.Text.RegularExpressions;
using AutoCtor;
using Injectio.Attributes;
using MementoMori.Option;
using Microsoft.Extensions.Logging;
using Quartz;

namespace MementoMori.Jobs;

[AutoConstruct]
[RegisterSingleton<TimeZoneAwareJobRegister>]
public partial class TimeZoneAwareJobRegister
{
    private readonly AccountManager _accountManager;
    private readonly IWritableOptions<GameConfig> _gameConfig;
    private readonly ILogger<TimeZoneAwareJobRegister> _logger;
    private readonly ISchedulerFactory _schedulerFactory;

    public async Task RegisterAllJobs()
    {
        foreach (var account in _accountManager.GetAll())
        {
            await RegisterJobs(account.Key);
        }
    }

    public async Task DeregisterJobs(long userId)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await RemoveJob<DailyJob>(scheduler, userId);
        await RemoveJob<HourlyJob>(scheduler, userId);
        await RemoveJob<PvpJob>(scheduler, userId);
        await RemoveJob<LegendLeagueJob>(scheduler, userId);
        await RemoveJob<GuildRaidBossReleaseJob>(scheduler, userId);
        await RemoveJob<AutoBuyShopItemJob>(scheduler, userId);
        await RemoveJob<LocalRaidJob>(scheduler, userId);
        // await RemoveJob<GuildBattleDeployDefenseJob>(scheduler, userId);
        await RemoveJob<AutoChangeGachaRelicJob>(scheduler, userId);
        await RemoveJob<AutoDrawGachaRelicJob>(scheduler, userId);
    }

    public async Task RegisterJobs(long userId)
    {
        var account = _accountManager.Get(userId);
        if (!account.Funcs.LoginOk) return;

        var networkManager = account.NetworkManager;
        var scheduler = await _schedulerFactory.GetScheduler();
        if (_gameConfig.Value.AutoJob.DisableAll)
        {
            await DeregisterJobs(userId);
            return;
        }

        await AddJob<DailyJob>(scheduler, _gameConfig.Value.AutoJob.DailyJobCron, ResourceStrings.DailyJob, userId, networkManager.TimeManager.DiffFromUtc);
        await AddJob<HourlyJob>(scheduler, _gameConfig.Value.AutoJob.HourlyJobCron, ResourceStrings.RewardClaimJob, userId, networkManager.TimeManager.DiffFromUtc);
        await AddJob<PvpJob>(scheduler, NormalizeCron(_gameConfig.Value.AutoJob.PvpJobCron), TextResourceTable.Get("[CommonHeaderLocalPvpLabel]"), userId, networkManager.TimeManager.DiffFromUtc);
        await AddJob<LegendLeagueJob>(scheduler, NormalizeCron(_gameConfig.Value.AutoJob.LegendLeagueJobCron), TextResourceTable.Get("[CommonHeaderGlobalPvpLabel]"), userId,
            networkManager.TimeManager.DiffFromUtc);
        await AddJob<GuildRaidBossReleaseJob>(scheduler, _gameConfig.Value.AutoJob.GuildRaidBossReleaseCron, TextResourceTable.Get("[GuildRaidReleaseConfirmTitle]"), userId,
            networkManager.TimeManager.DiffFromUtc);
        await AddJob<AutoBuyShopItemJob>(scheduler, _gameConfig.Value.AutoJob.AutoBuyShopItemJobCron, ResourceStrings.ShopAutoBuyItems, userId, networkManager.TimeManager.DiffFromUtc);
        await AddJob<LocalRaidJob>(scheduler, _gameConfig.Value.AutoJob.AutoLocalRaidJobCron, TextResourceTable.Get("[CommonHeaderLocalRaidLabel]"), userId,
            networkManager.TimeManager.DiffFromUtc);
        // await AddJob<GuildBattleDeployDefenseJob>(scheduler, _gameConfig.Value.AutoJob.AutoDeployGuildDefenseJobCron, ResourceStrings.Deploy_defense, userId,
        //     networkManager.TimeManager.DiffFromUtc);
        await AddJob<AutoChangeGachaRelicJob>(scheduler, _gameConfig.Value.AutoJob.AutoChangeGachaRelicJobCron, TextResourceTable.Get("[GachaRelicChangeTitle]"), userId,
            networkManager.TimeManager.DiffFromUtc);
        await AddJob<AutoDrawGachaRelicJob>(scheduler, _gameConfig.Value.AutoJob.AutoDrawGachaRelicJobCron, ResourceStrings.Auto_draw_10_times__up_to_3_draws_, userId,
            networkManager.TimeManager.DiffFromUtc);
    }

    private string NormalizeCron(string cron)
    {
        return Regex.Replace(cron, @"^[\S]+", "0");
    }

    private async Task RemoveJob<T>(IScheduler scheduler, long userId) where T : IJob
    {
        var type = typeof(T);
        var jobKey = new JobKey($"{userId}-{type.FullName!}");
        await scheduler.DeleteJob(jobKey);
    }

    private async Task AddJob<T>(IScheduler scheduler, string cron, string description, long userId, TimeSpan offset) where T : IJob
    {
        try
        {
            await AddJobCore<T>(scheduler, cron, description, userId, offset);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to register {JobType} for user {UserId} with cron {Cron}", typeof(T).FullName, userId, cron);
        }
    }

    private async Task AddJobCore<T>(IScheduler scheduler, string cron, string description, long userId, TimeSpan offset) where T : IJob
    {
        var type = typeof(T);
        var jobKey = new JobKey($"{userId}-{type.FullName!}");
        var jobDetail = JobBuilder.Create<T>().WithIdentity(jobKey).WithDescription(description).UsingJobData("userId", userId).Build();

        var customTimeZone = TimeZoneInfo.CreateCustomTimeZone(offset.ToString(), offset, null, null);
        var trigger = TriggerBuilder.Create()
            .ForJob(jobKey)
            .WithIdentity($"{userId}-{type.FullName}-trigger")
            .WithCronSchedule(cron, builer => builer.InTimeZone(customTimeZone))
            .Build();
        var existingTrigger = await scheduler.GetTrigger(trigger.Key);
        if (existingTrigger is ICronTrigger existingCronTrigger
            && existingCronTrigger.CronExpressionString == cron
            && existingCronTrigger.TimeZone.HasSameRules(customTimeZone))
            return;

        if (existingTrigger != null)
            await scheduler.RescheduleJob(trigger.Key, trigger);
        else if (await scheduler.CheckExists(jobKey))
            await scheduler.ScheduleJob(trigger);
        else
            await scheduler.ScheduleJob(jobDetail, trigger);
    }
}

[AttributeUsage(AttributeTargets.Class)]
public class CronAttribute : Attribute
{
    public CronAttribute(string cron)
    {
        Cron = cron;
    }

    public string Cron { get; set; }
}
