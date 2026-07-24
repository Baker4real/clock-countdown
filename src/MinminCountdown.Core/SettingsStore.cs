using System.Text;

namespace MinminCountdown.Core;

public sealed class SettingsStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly IGuidSource _guidSource;

    public SettingsStore(IGuidSource? guidSource = null, IClock? clock = null)
    {
        _guidSource = guidSource ?? SystemGuidSource.Instance;
        Clock = clock ?? SystemClock.Instance;
    }

    public IClock Clock { get; }

    public DateTimeOffset GetCurrentBeijingTime()
    {
        return ChinaStandardTime.GetCurrentBeijingTime(Clock);
    }

    public SettingsLoadResult Load(string settingsPath)
    {
        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            throw new ArgumentException("设置文件路径不能为空。", nameof(settingsPath));
        }

        return SettingsContract.Deserialize(File.ReadAllText(settingsPath, Utf8NoBom), _guidSource);
    }

    public CountdownSettings Copy(CountdownSettings settings)
    {
        return SettingsContract.Canonicalize(settings);
    }

    public string Serialize(CountdownSettings settings)
    {
        return SettingsContract.SerializeCanonical(settings);
    }

    public void Save(string settingsPath, CountdownSettings settings, int timeoutSeconds = 5)
    {
        if (string.IsNullOrWhiteSpace(settingsPath))
        {
            throw new ArgumentException("设置文件路径不能为空。", nameof(settingsPath));
        }

        // Validate and serialize before acquiring the shared write mutex, matching the existing host.
        var payload = SettingsContract.SerializeCanonical(settings);
        var temporaryPath = settingsPath + ".tmp";
        using var mutex = SettingsMutex.EnterWriteMutex(settingsPath, timeoutSeconds);
        File.WriteAllText(temporaryPath, payload, Utf8NoBom);
        File.Move(temporaryPath, settingsPath, overwrite: true);
    }
}
