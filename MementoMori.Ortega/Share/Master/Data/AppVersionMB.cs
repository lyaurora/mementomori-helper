using System.ComponentModel;
using MementoMori.Ortega.Share.Enums;
using MementoMori.Ortega.Share.Master.Attributes;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Data
{
	[Description("アプリバージョン")]
	[MessagePackObject(true)]
	public class AppVersionMB : MasterBookBase
	{
		[PropertyOrder(1)]
		[Description("デバイス種別")]
		public DeviceType DeviceType
		{
			get;
		}

		[PropertyOrder(2)]
		[Description("アプリバージョン")]
		public string AppVersion
		{
			get;
		}

		[SerializationConstructor]
		public AppVersionMB(long id, bool? isIgnore, string memo, DeviceType deviceType, string appVersion):base(id, isIgnore, memo)
		{
			DeviceType = deviceType;
			AppVersion = appVersion;
		}

		public AppVersionMB():base(0L, null, null)
		{
		}
	}
}
