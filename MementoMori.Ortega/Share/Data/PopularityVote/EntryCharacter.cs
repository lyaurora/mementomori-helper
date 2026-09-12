using MessagePack;

namespace MementoMori.Ortega.Share.Data.PopularityVote
{
	[MessagePackObject(true)]
	public class EntryCharacter
	{
        public int GroupId { get; set; }


		public long EntryCharacterId { get; set; }

		public List<long> SubDisplayCharacterIdList { get; set; }
	}
}
