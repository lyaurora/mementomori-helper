using MementoMori.Ortega.Share.Master.Data;

namespace MementoMori.Ortega.Share.Master.Table
{
	public class WorldGroupTable : TableBase<WorldGroupMB>
	{
		public WorldGroupMB GetByWorldIdLocalDateTime(long worldId, DateTime localDateTime)
		{
			foreach (var group in GetArray())
			{
				if (group?.WorldIdList == null || !group.WorldIdList.Contains(worldId))
				{
					continue;
				}
				if (group.IsActiveGroup(localDateTime))
				{
					return group;
				}
			}
			return null;
		}

		public WorldGroupMB GetByWorldIdOpenGrandBattle(long worldId, DateTime localDateTime)
		{
			foreach (var group in GetArray())
			{
				if (group?.WorldIdList == null || !group.WorldIdList.Contains(worldId))
				{
					continue;
				}
				if (group.IsActiveGroup(localDateTime) && group.IsOpenedGrandBattle(localDateTime))
				{
					return group;
				}
			}
			return null;
		}

		public WorldGroupTable()
		{
		}
	}
}
