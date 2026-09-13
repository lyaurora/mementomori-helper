using MementoMori.Ortega.Share.Master.Interfaces;
using MessagePack;

namespace MementoMori.Ortega.Share.Master.Table;

public abstract class TableBase<TM> : ITable where TM : MasterBookBase
{
    private sealed record Contents(TM[] Items, Dictionary<long, TM> ById);
    private Contents? _contents;
    protected TM[] _datas => Volatile.Read(ref _contents)?.Items;

    public TM GetById(long id) => Volatile.Read(ref _contents)?.ById.GetValueOrDefault(id);
    public string GetMasterBookName() => typeof(TM).Name;
    public TM[] GetArray() => _datas;
    public int Count() => _datas.Length;

    public bool Load()
    {
        using var stream = File.OpenRead(GetMasterDataPath(typeof(TM).Name));
        Publish(MessagePackSerializer.Deserialize<TM[]>(stream));
        return true;
    }

    public virtual bool Load(byte[] binaryData)
    {
        Publish(MessagePackSerializer.Deserialize<TM[]>(binaryData));
        return true;
    }

    private void Publish(TM[] items)
    {
        var byId = new Dictionary<long, TM>(items.Length);
        // Keep FirstOrDefault's first-match semantics and preserve source order.
        foreach (var item in items) byId.TryAdd(item.Id, item);
        // Publish a complete snapshot for readers during hourly refreshes.
        Volatile.Write(ref _contents, new Contents(items, byId));
    }

    protected static string GetMasterDataPath(string masterBookName) => "./Master/" + masterBookName;
}
