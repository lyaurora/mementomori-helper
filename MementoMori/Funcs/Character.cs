using MementoMori.Exceptions;
using MementoMori.Ortega.Custom;
using MementoMori.Ortega.Share.Data.ApiInterface.Character;
using MementoMori.Ortega.Share.Data.Character;
using MementoMori.Ortega.Share.Data.DtoInfo;
using MementoMori.Ortega.Share.Extensions;

namespace MementoMori.Funcs;

public partial class MementoMoriFuncs
{
    public async Task ReadAllMemories()
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            foreach (var userCharacterBook in UserSyncData.UserCharacterBookDtoInfos)
            {
                var stories = CharacterStoryTable.GetListByCharacterId(userCharacterBook.CharacterId);
                // var episodeIds = new List<long>();
                foreach (var storyMB in stories)
                {
                    if (storyMB.Level <= userCharacterBook.MaxCharacterLevel && storyMB.EpisodeId > userCharacterBook.MaxEpisodeId)
                    {
                        // episodeIds.Add(storyMB.Id);
                        var resp = await GetResponse<GetCharacterStoryRewardRequest, GetCharacterStoryRewardResponse>(new GetCharacterStoryRewardRequest
                        {
                            IsSkip = true,
                            CharacterStoryIdList = new List<long> {storyMB.Id}
                        });
                        resp.RewardItemList.PrintUserItems(log);
                    }
                }

                // if (episodeIds.Count > 0)
                // {
                //     var resp = await GetResponse<GetCharacterStoryRewardRequest, GetCharacterStoryRewardResponse>(new GetCharacterStoryRewardRequest()
                //     {
                //         IsSkip = true,
                //         CharacterStoryIdList = episodeIds
                //     });
                //     resp.RewardItemList.PrintUserItems(log);
                // }
            }
        });
    }

    public async Task AutoRankUpCharacter()
    {
        await ExecuteQuickAction(async (log, token) =>
        {
            await RankUp(CharacterRarityFlags.R, 3, log);
            await RankUp(CharacterRarityFlags.SR, 2, log);
            log($"{TextResourceTable.Get("[CharacterMenuTabCharacterRankUp]")} {ResourceStrings.Finished}");
        });

        async Task RankUp(CharacterRarityFlags rarityFlags, int count, Action<string> log)
        {
            var rGroup = UserSyncData.UserCharacterDtoInfos
                .Where(d => (d.RarityFlags & rarityFlags) != 0)
                .GroupBy(d => d.CharacterId)
                .ToList();
            // 锁定或在等级链接里的角色只能做目标，不能做素材
            bool IsProtected(UserCharacterDtoInfo d) => d.IsLocked || UserSyncData.IsLevelLinkMember(d.Guid);
            foreach (var grouping in rGroup.Where(d => d.Count() >= count))
            {
                var infos = grouping.OrderByDescending(IsProtected).ThenByDescending(d => d.Level).ToList();
                var main = infos.First();
                var materials = infos.Skip(1).Where(d => !IsProtected(d) && d.Level <= main.Level).OrderBy(d => d.Level).Take(count - 1).ToList();
                if (materials.Count < count - 1) continue;

                try
                {
                    await GetResponse<RankUpRequest, RankUpResponse>(new RankUpRequest
                    {
                        RankUpList = new List<CharacterRankUpMaterialInfo>
                        {
                            new() {TargetGuid = main.Guid, MaterialGuid1 = materials[0].Guid, MaterialGuid2 = materials.Count == 1 ? null : materials[1].Guid}
                        }
                    });
                }
                catch (ApiErrorException e) when (e.ErrorCode is not (ErrorCode.CommonNoSession or ErrorCode.CommonLoggedInAnotherDevice))
                {
                    // 一组失败不影响其他组；会话失效仍然向上抛，让任务停下
                    log(e.Message);
                    continue;
                }

                CharacterTable.GetCharacterName(main.CharacterId, out var name1, out var name2);
                if (!name2.IsNullOrEmpty()) name1 = $"[{name2}] {name1}";

                log(
                    $"{TextResourceTable.Get("[ItemBoxButtonUse]")} {TextResourceTable.Get("[CommonFooterCharacterButtonLabel]")} {name1} X {count}, {TextResourceTable.Get(main.RarityFlags)}");
            }
        }
    }
}