using MementoMori.Ortega.Share.Enums;
using MessagePack;

namespace MementoMori.Ortega.Share.Data.ApiInterface.Shop;

[MessagePackObject(true)]
[OrtegaApi("shop/getList", true, false)]
public class GetListRequest : ApiRequestBase
{
        public LanguageType LanguageType { get; set; }


}