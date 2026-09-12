using MementoMori.Ortega.Share.Enums;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.DtoInfo
{
	[MessagePackObject(true)]
	public class UserRecruitGuildMemberSettingDtoInfo
	{
        public PlayerRecruitType PlayerRecruitType { get; set; }

        public PlayerCommunicationPolicyType CommunicationPolicyType { get; set; }

        public PlayerEventPolicyType EventPolicyType { get; set; }

        public PlayerGuildBattlePolicyType GuildBattlePolicyType { get; set; }

        public long UpdateLocalTime { get; set; }


		public long GuildPowerLowerLimit { get; set; }

		public long GuildLvLowerLimit { get; set; }

		public bool IsRecruit { get; set; }
	}
}
