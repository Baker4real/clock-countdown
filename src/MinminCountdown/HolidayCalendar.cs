using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using MinminCountdown.Core;

namespace MinminCountdown;

internal sealed class HolidayCalendar : IDisposable
{
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(5);
    private static readonly IReadOnlyDictionary<string, bool> EmptyWorkdayMap =
        new Dictionary<string, bool>();
    private readonly object _sync = new();
    private readonly HolidayCalendarSource _source;
    private IReadOnlyDictionary<string, bool>? _workdayMap;
    private CancellationTokenSource? _refreshCancellation;
    private int? _readyYear;
    private int? _refreshingYear;
    private int? _retryYear;
    private DateTimeOffset? _nextRetryUtc;
    private int _consecutiveFailureCount;
    private long _refreshGeneration;
    private bool _disposed;

    public HolidayCalendar(string applicationRoot)
        : this(new HolidayCalendarSource(applicationRoot))
    {
    }

    internal HolidayCalendar(HolidayCalendarSource source)
    {
        _source = source;
    }

    internal HolidayCalendar(
        string applicationRoot,
        Func<int, CancellationToken, Task<string>>? downloadOverride)
        : this(new HolidayCalendarSource(applicationRoot, downloadOverride))
    {
    }

    public event EventHandler? StateChanged;

    public string State { get; private set; } = "Pending";

    // 首次渲染早于首次刷新完成，初始文案必须是加载中而不是未就绪（#218）。
    public string StatusText { get; private set; } = "节假日日历正在加载";

    public Task<bool> RefreshAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var year = ChinaStandardTime.ToBeijingTime(nowUtc).Year;
        CancellationTokenSource? previousCancellation;
        CancellationTokenSource refreshCancellation;
        long generation;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_readyYear == year ||
                _refreshingYear == year ||
                (_retryYear == year && nowUtc < _nextRetryUtc))
            {
                return Task.FromResult(false);
            }

            if (_retryYear != year)
            {
                ClearRetryState();
            }

            _refreshingYear = year;
            generation = ++_refreshGeneration;
            previousCancellation = _refreshCancellation;
            refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _refreshCancellation = refreshCancellation;
        }

        previousCancellation?.Cancel();
        previousCancellation?.Dispose();
        return RefreshYearAsync(year, nowUtc, generation, refreshCancellation);
    }

    private async Task<bool> RefreshYearAsync(
        int year,
        DateTimeOffset attemptUtc,
        long generation,
        CancellationTokenSource refreshCancellation)
    {
        try
        {
            try
            {
                if (_source.TryReadCached(year, out var cached))
                {
                    return TrySetReady(cached, year, generation);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // A corrupt or unreadable mutable cache must not hide a valid
                // network or packaged source.
            }

            if (!TrySetLoading(generation))
            {
                return false;
            }

            IReadOnlyDictionary<string, bool>? downloaded = null;
            try
            {
                var payload = await _source
                    .DownloadAsync(year, refreshCancellation.Token)
                    .ConfigureAwait(false);
                if (TryParse(payload, year, out var parsed))
                {
                    downloaded = parsed;
                    // A valid network response is authoritative over packaged data.
                    try
                    {
                        _source.WriteCache(year, payload);
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException)
                    {
                        // Keep the valid network result for this process even if
                        // the optional mutable cache cannot be persisted.
                    }
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException or OperationCanceledException or IOException or JsonException)
            {
                // Offline data is attempted below. A missing or invalid packaged year
                // still leaves the calendar unavailable rather than estimating.
            }

            if (downloaded is not null)
            {
                return TrySetReady(downloaded, year, generation, "network");
            }

            try
            {
                if (_source.TryReadPackaged(year, out var packaged))
                {
                    return TrySetReady(packaged, year, generation, "packaged");
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // Missing, corrupt or inaccessible packaged data means this year
                // remains unavailable; never infer a weekday-only calendar.
            }

            return TrySetUnavailable(year, attemptUtc, generation);
        }
        catch (ObjectDisposedException) when (!IsCurrentRefresh(generation))
        {
            return false;
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_refreshCancellation, refreshCancellation))
                {
                    _refreshCancellation = null;
                }
            }
            refreshCancellation.Dispose();
        }
    }

    // 只给出今天的下班时刻：非工作日、已过下班或日历未就绪时为 null；距离下班行由悬浮窗倒计时投影组装。
    public DateTimeOffset? GetWorkdayEndUtc(DateTimeOffset nowUtc, CountdownPreferences preferences)
    {
        var beijing = ChinaStandardTime.ToBeijingTime(nowUtc);
        if (!TryGetWorkdayMap(beijing.Year, out var workdayMap))
        {
            return null;
        }

        var key = beijing.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!workdayMap.TryGetValue(key, out var isWorkday) ||
            !isWorkday ||
            beijing.TimeOfDay >= preferences.WorkEnd)
        {
            return null;
        }

        return CountdownFormatting.ToUtc(
            beijing.Date,
            preferences.WorkEnd.Hours,
            preferences.WorkEnd.Minutes,
            0);
    }

    public bool IsWorkdayComplete(DateTimeOffset nowUtc, CountdownPreferences preferences)
    {
        var beijing = ChinaStandardTime.ToBeijingTime(nowUtc);
        return beijing.TimeOfDay >= preferences.WorkEnd &&
            TryGetWorkdayMap(beijing.Year, out var workdayMap) &&
            workdayMap.TryGetValue(beijing.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out var isWorkday) &&
            isWorkday;
    }

    public decimal? GetEarnings(DateTimeOffset nowUtc, CountdownPreferences preferences)
    {
        if (preferences.MonthlySalary <= 0)
        {
            return null;
        }

        var beijing = ChinaStandardTime.ToBeijingTime(nowUtc);
        if (!TryGetWorkdayMap(beijing.Year, out var workdayMap))
        {
            return null;
        }

        var workdays = 0;
        for (var day = new DateTime(beijing.Year, beijing.Month, 1);
             day.Month == beijing.Month;
             day = day.AddDays(1))
        {
            var key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (workdayMap.TryGetValue(key, out var isWorkday) && isWorkday)
            {
                workdays++;
            }
        }

        if (workdays == 0)
        {
            return 0m;
        }

        var todayKey = beijing.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!workdayMap.TryGetValue(todayKey, out var todayIsWorkday) || !todayIsWorkday)
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

    public void Dispose()
    {
        CancellationTokenSource? refreshCancellation;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _refreshGeneration++;
            refreshCancellation = _refreshCancellation;
            _refreshCancellation = null;
            _workdayMap = null;
            _readyYear = null;
            _refreshingYear = null;
            ClearRetryState();
        }
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        _source.Dispose();
    }

    internal static bool TryParse(
        string json,
        int year,
        out IReadOnlyDictionary<string, bool> workdayMap)
    {
        workdayMap = EmptyWorkdayMap;
        using var document = JsonDocument.Parse(json);
        var expectedDayCount = DateTime.IsLeapYear(year) ? 366 : 365;
        if (!document.RootElement.TryGetProperty("days", out var days) ||
            days.ValueKind != JsonValueKind.Array ||
            days.GetArrayLength() != expectedDayCount)
        {
            return false;
        }

        var map = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var day in days.EnumerateArray())
        {
            if (!day.TryGetProperty("date", out var dateElement) ||
                !day.TryGetProperty("is_workday", out var workdayElement) ||
                dateElement.GetString() is not { Length: > 0 } date ||
                !DateOnly.TryParseExact(
                    date,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedDate) ||
                parsedDate.Year != year ||
                workdayElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }

            if (!map.TryAdd(date, workdayElement.GetBoolean()))
            {
                return false;
            }
        }

        if (map.Count != expectedDayCount)
        {
            return false;
        }

        workdayMap = map;
        return true;
    }

    private bool TryGetWorkdayMap(
        int year,
        out IReadOnlyDictionary<string, bool> workdayMap)
    {
        lock (_sync)
        {
            if (_workdayMap is not null && _readyYear == year)
            {
                workdayMap = _workdayMap;
                return true;
            }
        }

        workdayMap = EmptyWorkdayMap;
        return false;
    }

    private bool TrySetLoading(long generation)
    {
        lock (_sync)
        {
            if (_disposed || generation != _refreshGeneration)
            {
                return false;
            }

            _workdayMap = null;
            _readyYear = null;
            State = "Pending";
            StatusText = "节假日日历正在加载";
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool IsCurrentRefresh(long generation)
    {
        lock (_sync)
        {
            return !_disposed && generation == _refreshGeneration;
        }
    }

    private bool TrySetReady(
        IReadOnlyDictionary<string, bool> map,
        int year,
        long generation,
        string sourceKind = "cache")
    {
        lock (_sync)
        {
            if (_disposed || generation != _refreshGeneration)
            {
                return false;
            }

            _workdayMap = map;
            _readyYear = year;
            _refreshingYear = null;
            ClearRetryState();
            State = "Ready";
            StatusText = sourceKind switch
            {
                "packaged" => $"已加载 {year} 年随包法定节假日与调休",
                "network" => $"已同步 {year} 年法定节假日与调休",
                _ => $"已同步 {year} 年法定节假日与调休"
            };
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool TrySetUnavailable(int year, DateTimeOffset attemptUtc, long generation)
    {
        lock (_sync)
        {
            if (_disposed || generation != _refreshGeneration)
            {
                return false;
            }

            _workdayMap = null;
            _readyYear = null;
            _refreshingYear = null;
            if (_retryYear != year)
            {
                _consecutiveFailureCount = 0;
            }
            _retryYear = year;
            _consecutiveFailureCount = Math.Min(_consecutiveFailureCount + 1, 5);
            _nextRetryUtc = attemptUtc + GetRetryDelay(_consecutiveFailureCount);
            State = "Unavailable";
            StatusText = "节假日日历未就绪";
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private static TimeSpan GetRetryDelay(int consecutiveFailureCount)
    {
        var multiplier = 1 << Math.Min(consecutiveFailureCount - 1, 4);
        return TimeSpan.FromTicks(Math.Min(
            InitialRetryDelay.Ticks * multiplier,
            MaximumRetryDelay.Ticks));
    }

    private void ClearRetryState()
    {
        _retryYear = null;
        _nextRetryUtc = null;
        _consecutiveFailureCount = 0;
    }
}

internal sealed class HolidayCalendarSource : IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly string _applicationRoot;
    private readonly Func<int, CancellationToken, Task<string>>? _downloadOverride;
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(10) };

    public HolidayCalendarSource(
        string applicationRoot,
        Func<int, CancellationToken, Task<string>>? downloadOverride = null)
    {
        _applicationRoot = applicationRoot;
        _downloadOverride = downloadOverride;
    }

    public bool TryReadCached(int year, out IReadOnlyDictionary<string, bool> workdayMap)
    {
        workdayMap = new Dictionary<string, bool>();
        var cachePath = GetCachePath(year);
        return File.Exists(cachePath) &&
               HolidayCalendar.TryParse(
                   File.ReadAllText(cachePath, Utf8NoBom),
                   year,
                   out workdayMap);
    }

    public async Task<string> DownloadAsync(int year, CancellationToken cancellationToken)
    {
        if (_downloadOverride is not null)
        {
            return await _downloadOverride(year, cancellationToken).ConfigureAwait(false);
        }

        var uri =
            $"https://uapis.cn/api/v1/misc/holiday-calendar?year={year}&holiday_type=legal&timezone=Asia%2FShanghai";
        return await _client.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
    }

    public bool TryReadPackaged(int year, out IReadOnlyDictionary<string, bool> workdayMap)
    {
        workdayMap = new Dictionary<string, bool>();
        var packagedPath = GetPackagedPath(year);
        return File.Exists(packagedPath) &&
               HolidayCalendar.TryParse(
                   File.ReadAllText(packagedPath, Utf8NoBom),
                   year,
                   out workdayMap);
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

    private string GetPackagedPath(int year) => Path.Combine(_applicationRoot, "holiday-data", $"{year}.json");
}
