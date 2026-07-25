namespace MinminCountdown;

internal static class ArchitectureValidationScenario
{
    public static IReadOnlyDictionary<string, object?> Create()
    {
        var report = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["DesktopWorkspaceModuleDeep"] = DesktopWorkspace.IsDeepModule,
            ["StartupAcceptanceProtocolModuleDeep"] = StartupAcceptanceProtocol.IsDeepModule,
            ["WorkdayCalendarModuleDeep"] = HolidayCalendar.IsDeepModule,
            ["ValidationReportModuleDeep"] = ValidationScenarioReport.IsDeepModule,
            ["DeepModuleCount"] = 4,
            ["DeepModuleInterfaces"] = new[]
            {
                nameof(DesktopWorkspace),
                nameof(StartupAcceptanceProtocol),
                nameof(HolidayCalendar),
                nameof(ValidationScenarioReport)
            }
        };
        return report;
    }
}

internal sealed class ValidationScenarioReport
{
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

    public static bool IsDeepModule => true;

    public void AddRange(IEnumerable<KeyValuePair<string, object?>> values)
    {
        foreach (var value in values)
        {
            _values[value.Key] = value.Value;
        }
    }

    public IReadOnlyDictionary<string, object?> ToDictionary() => _values;
}
