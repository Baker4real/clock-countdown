using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using MinminCountdown.Core;

namespace MinminCountdown;

internal sealed class HolidayCalendar : IDisposable
{
    private readonly HolidayCalendarSource _source;
    private IReadOnlyDictionary<string, bool>? _workdayMap;

    public HolidayCalendar(string applicationRoot)
        : this(new HolidayCalendarSource(applicationRoot))
    {
    }

    private HolidayCalendar(HolidayCalendarSource source)
    {
        _source = source;
    }

    public event EventHandler? StateChanged;

    public string State { get; private set; } = "Pending";

    public string StatusText { get; private set; } = "节假日日历未就绪";

    public async Task InitializeAsync(int year, CancellationToken cancellationToken)
    {
        try
        {
            if (_source.TryReadCached(year, out var cached))
            {
                SetReady(cached, year);
                return;
            }

            StatusText = "节假日日历正在加载";
            StateChanged?.Invoke(this, EventArgs.Empty);
            var payload = await _source.DownloadAsync(year, cancellationToken).ConfigureAwait(false);
            if (!TryParse(payload, out var downloaded))
            {
                SetUnavailable();
                return;
            }

            _source.WriteCache(year, payload);
            SetReady(downloaded, year);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            SetUnavailable();
        }
    }

    public CountdownRecord? GetWorkdayEndCountdown(DateTimeOffset nowUtc, CountdownPreferences preferences)
    {
        if (_workdayMap is null)
        {
            return null;
        }

        var beijing = ChinaStandardTime.ToBeijingTime(nowUtc);
        var key = beijing.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!_workdayMap.TryGetValue(key, out var isWorkday) ||
            !isWorkday ||
            beijing.TimeOfDay >= preferences.WorkEnd)
        {
            return null;
        }

        var targetUtc = CountdownFormatting.ToUtc(
            beijing.Date,
            preferences.WorkEnd.Hours,
            preferences.WorkEnd.Minutes,
            0);
        return new CountdownRecord("__workday_end__", "距离下班", targetUtc, Array.Empty<string>());
    }

    public decimal? GetEarnings(DateTimeOffset nowUtc, CountdownPreferences preferences)
    {
        if (_workdayMap is null)
        {
            return null;
        }

        var beijing = ChinaStandardTime.ToBeijingTime(nowUtc);
        var workdays = 0;
        for (var day = new DateTime(beijing.Year, beijing.Month, 1);
             day.Month == beijing.Month;
             day = day.AddDays(1))
        {
            var key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (_workdayMap.TryGetValue(key, out var isWorkday) && isWorkday)
            {
                workdays++;
            }
        }

        if (workdays == 0)
        {
            return 0m;
        }

        var todayKey = beijing.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!_workdayMap.TryGetValue(todayKey, out var todayIsWorkday) || !todayIsWorkday)
        {
            return 0m;
        }

        var daySalary = preferences.MonthlySalary / workdays;
        if (beijing.TimeOfDay <= preferences.WorkStart)
        {
            return 0m;
        }

        if (beijing.TimeOfDay >= preferences.WorkEnd)
        {
            return Math.Round(daySalary, 2, MidpointRounding.AwayFromZero);
        }

        var progress =
            (decimal)((beijing.TimeOfDay - preferences.WorkStart).TotalSeconds /
                      (preferences.WorkEnd - preferences.WorkStart).TotalSeconds);
        return Math.Round(daySalary * progress, 2, MidpointRounding.AwayFromZero);
    }

    public void Dispose() => _source.Dispose();

    internal static bool TryParse(string json, out IReadOnlyDictionary<string, bool> workdayMap)
    {
        workdayMap = new Dictionary<string, bool>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("days", out var days) ||
            days.ValueKind != JsonValueKind.Array ||
            days.GetArrayLength() < 365)
        {
            return false;
        }

        var map = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var day in days.EnumerateArray())
        {
            if (!day.TryGetProperty("date", out var dateElement) ||
                !day.TryGetProperty("is_workday", out var workdayElement) ||
                dateElement.GetString() is not { Length: > 0 } date ||
                workdayElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }

            map[date] = workdayElement.GetBoolean();
        }

        workdayMap = map;
        return true;
    }

    private void SetReady(IReadOnlyDictionary<string, bool> map, int year)
    {
        _workdayMap = map;
        State = "Ready";
        StatusText = $"已同步 {year} 年法定节假日与调休";
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetUnavailable()
    {
        _workdayMap = null;
        State = "Unavailable";
        StatusText = "节假日日历未就绪";
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal sealed class HolidayCalendarSource : IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly string _applicationRoot;
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(10) };

    public HolidayCalendarSource(string applicationRoot)
    {
        _applicationRoot = applicationRoot;
    }

    public bool TryReadCached(int year, out IReadOnlyDictionary<string, bool> workdayMap)
    {
        workdayMap = new Dictionary<string, bool>();
        var cachePath = GetCachePath(year);
        return File.Exists(cachePath) &&
               HolidayCalendar.TryParse(File.ReadAllText(cachePath, Utf8NoBom), out workdayMap);
    }

    public async Task<string> DownloadAsync(int year, CancellationToken cancellationToken)
    {
        var uri =
            $"https://uapis.cn/api/v1/misc/holiday-calendar?year={year}&holiday_type=legal&timezone=Asia%2FShanghai";
        return await _client.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
    }

    public void WriteCache(int year, string payload)
    {
        var cachePath = GetCachePath(year);
        var directory = Path.GetDirectoryName(cachePath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(cachePath, payload, Utf8NoBom);
    }

    public void Dispose() => _client.Dispose();

    private string GetCachePath(int year) => Path.Combine(_applicationRoot, "holiday-cache", $"{year}.json");
}
