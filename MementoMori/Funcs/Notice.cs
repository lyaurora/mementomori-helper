using MementoMori.Ortega.Share.Data.ApiInterface.Notice;

namespace MementoMori.Funcs;

public partial class MementoMoriFuncs
{
    public Task GetNoticeInfoList() => GetNoticeInfoList(CancellationToken.None);

    public async Task GetNoticeInfoList(CancellationToken cancellationToken)
    {
        var countryCode = OrtegaConst.Addressable.LanguageNameDictionary[NetworkManager.LanguageType];

        var response = await GetResponse<GetNoticeInfoListRequest, GetNoticeInfoListResponse>(new GetNoticeInfoListRequest
        {
            AccessType = NoticeAccessType.Title,
            CountryCode = countryCode,
            LanguageType = NetworkManager.LanguageType,
            UserId = this.UserId
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        NoticeInfoList = response.NoticeInfoList.Where(d => d.Id % 10 != 6).ToList();

        EventInfoList = response.EventInfoList
            .GroupBy(n => n.Title)
            .Select(g => g.First())
            .ToList();
    }
}
