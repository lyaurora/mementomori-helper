using MementoMori.Ortega.Share.Data.ApiInterface.Equipment;

namespace MementoMori.Funcs;

public partial class MementoMoriFuncs
{
    public async Task AutoEquipmentTraning()
    {
        // 点击时固定目标：Funcs 上的属性是账号共享的，界面重新打开会改写它们
        var targetGuid = TrainingEquipmentGuid;
        var targetType = EquipmentTrainingTargetType;
        var targetPercent = EquipmentTrainingTargetPercent;
        await ExecuteQuickAction(async (log, token) =>
        {
            var totalCount = 0;
            while (!token.IsCancellationRequested)
            {
                // await UserGetUserData();
                var equipment = UserSyncData.UserEquipmentDtoInfos.First(d => d.Guid == targetGuid);
                var equipmentMb = EquipmentTable.GetById(equipment.EquipmentId);
                var currentParameter = equipment.GetAdditionalParameter(targetType);
                var m =
                    $"{TextResourceTable.Get("[CommonForgedLabel]")} {totalCount}, {TextResourceTable.Get(targetType)} {currentParameter} ({(double) currentParameter / equipmentMb.AdditionalParameterTotal:P})";
                log(m);
                var targetValue = equipmentMb.AdditionalParameterTotal * targetPercent;
                switch (targetType)
                {
                    case BaseParameterType.Health when equipment.AdditionalParameterHealth >= targetValue:
                        return;
                    case BaseParameterType.Energy when equipment.AdditionalParameterEnergy >= targetValue:
                        return;
                    case BaseParameterType.Intelligence when equipment.AdditionalParameterIntelligence >= targetValue:
                        return;
                    case BaseParameterType.Muscle when equipment.AdditionalParameterMuscle >= targetValue:
                        return;
                }

                var response = await GetResponse<TrainingRequest, TrainingResponse>(new TrainingRequest {EquipmentGuid = targetGuid, ParameterLockedList = new List<BaseParameterType>()});
                totalCount++;
            }
        });
    }

    public async Task ReinforcementEquipmentOneTime()
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            var equipmentDtoInfo = UserSyncData.UserEquipmentDtoInfos.OrderBy(d => d.ReinforcementLv)
                .FirstOrDefault(d => !string.IsNullOrEmpty(d.CharacterGuid) && EquipmentTable.GetById(d.EquipmentId).EquipmentLv > d.ReinforcementLv);
            if (equipmentDtoInfo != null)
            {
                var response = await GetResponse<ReinforcementRequest, ReinforcementResponse>(new ReinforcementRequest {EquipmentGuid = equipmentDtoInfo.Guid, NumberOfTimes = 1});
                log($"{TextResourceTable.Get("[CommonReinforceLabel]")} {ResourceStrings.Finished}");
            }
        });
    }
}