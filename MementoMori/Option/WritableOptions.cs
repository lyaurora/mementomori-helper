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
        _section = section;
        _file = file;
        // The bound object merges appsettings.json arrays by index; keep the user file (what Update writes) as the one source of truth.
        // assign under the file lock so a reload can't overwrite a newer value written by Update (the lock is reentrant)
        _configuration.GetReloadToken().RegisterChangeCallback(_ => { lock (WritableOptionsFileLock.SyncRoot) _value = ReadFile(_options.CurrentValue); }, null);
        _options.OnChange(obj => { lock (WritableOptionsFileLock.SyncRoot) _value = ReadFile(obj); });
    }

    private string PhysicalPath => _fileProvider?.GetFileInfo(_file).PhysicalPath ?? Path.Combine(Directory.GetCurrentDirectory(), _file);

    private T ReadFile(T fallback)
    {
        lock (WritableOptionsFileLock.SyncRoot)
        {
            try
            {
                if (File.Exists(PhysicalPath) && JsonConvert.DeserializeObject<JObject>(File.ReadAllText(PhysicalPath)) is { } jObject
                    && jObject.TryGetValue(_section, out var section))
                    return JsonConvert.DeserializeObject<T>(section.ToString()) ?? fallback;
            }
            catch (Exception e) when (e is IOException or JsonException) { }
            return fallback;
        }
    }

    private T _value;

    public T Value => _value ??= ReadFile(_options.CurrentValue);

    public T Get(string name)
    {
        return _options.Get(name);
    }

    public void Update(Action<T> applyChanges)
    {
        var physicalPath = PhysicalPath;

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
                var fileOptions = new FileStreamOptions {Mode = FileMode.CreateNew, Access = FileAccess.Write};
                if (!OperatingSystem.IsWindows())
                    fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (var writer = new StreamWriter(new FileStream(tempPath, fileOptions)))
                    writer.Write(JsonConvert.SerializeObject(jObject, Formatting.Indented));
                if (!OperatingSystem.IsWindows() && File.Exists(physicalPath))
                    File.SetUnixFileMode(tempPath, File.GetUnixFileMode(physicalPath));
                if (File.Exists(physicalPath)) File.Copy(physicalPath, physicalPath + ".bak", true);
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
