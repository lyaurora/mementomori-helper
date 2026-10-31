namespace MementoMori.Ortega.Share.Master
{
	[AttributeUsage(AttributeTargets.Property)]
	public class MasterBookIdAttribute : Attribute
	{
		public Type Type
		{
			get;
		}

		public MasterBookIdAttribute(Type type)
		{
			Type = type;
		}
	}
}
