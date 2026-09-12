using System.Runtime.CompilerServices;
using MementoMori.Ortega.Share.Data.Player;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.Guild
{
	[MessagePackObject(true)]
	public class SearchGuildResultInfo
	{
        public bool IsRecruit { get; set; }


		public GuildInfo GuildInfo{ get; set; }

		public bool IsApplying { get; set; }

		public List<PlayerInfo> PlayerInfoList{ get; set; }
	}
}
