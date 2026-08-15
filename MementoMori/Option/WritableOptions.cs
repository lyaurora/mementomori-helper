using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MementoMori.Option;

internal static class WritableOptionsFileLock
{
    // ponytail: one global config lock; use per-path locks only if config write throughput ever matters.
    internal static readonly object SyncRoot = new();
}

public interface IWritableOptions<out T> : IOptions<T> where T : class, new()
{
    void Update(Action<T> applyChanges);
}

public class WritableOptions<T> : IWritableOptions<T> where T : class, new()
{
    private readonly IFileProvider _fileProvider;
    private readonly IOptionsMonitor<T> _options;
    private readonly IConfigurationRoot _configuration;
    private readonly string _section;
    private readonly string _file;

    public WritableOptions(
        IFileProvider fileProvider,
        IOptionsMonitor<T> options,
        IConfigurationRoot configuration,
        string section,
        string file)
    {
        _fileProvider = fileProvider;
        _options = options;
        _configuration = configuration;
        _configuration.GetReloadToken().RegisterChangeCallback(x => { _value = _options.CurrentValue; }, null);
        _section = section;
        _file = file;
        _options.OnChange(obj => _value = obj);
    }

    private T _value;

    public T Value => _value ??= _options.CurrentValue;

    public T Get(string name)
    {
        return _options.Get(name);
    }

    public void Update(Action<T> applyChanges)
    {
        var physicalPath = _fileProvider?.GetFileInfo(_file).PhysicalPath ?? Path.Combine(Directory.GetCurrentDirectory(), _file);

        lock (WritableOptionsFileLock.SyncRoot)
        {
            var jObject = File.Exists(physicalPath)
                ? JsonConvert.DeserializeObject<JObject>(File.ReadAllText(physicalPath)) ?? new JObject()
                : new JObject();
            var sectionObject = jObject.TryGetValue(_section, out var section) ? JsonConvert.DeserializeObject<T>(section.ToString()) ?? new T() : Value;
            applyChanges(sectionObject);
            jObject[_section] = JObject.FromObject(sectionObject);

            var tempPath = $"{physicalPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(tempPath, JsonConvert.SerializeObject(jObject, Formatting.Indented));
                try
                {
                    File.Move(tempPath, physicalPath, true);
                }
                catch (IOException) when (File.Exists(physicalPath))
                {
                    File.Copy(tempPath, physicalPath, true);
                }
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }

            _value = sectionObject;
        }
    }
}