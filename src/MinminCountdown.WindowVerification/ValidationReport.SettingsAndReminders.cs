using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MinminCountdown.Core;

namespace MinminCountdown;

internal static partial class ValidationReport
{
    private static bool ValidateRoundTrip(CountdownSettings settings)
    {
        var serialized = SettingsContract.SerializeCanonical(settings);
        var loaded = SettingsContract.Deserialize(serialized);
        return string.Equals(
            serialized,
            SettingsContract.SerializeCanonical(loaded.Settings),
            StringComparison.Ordinal);
    }

    private static bool ValidateLegacyMigration()
    {
        const string json =
            """
            {
              "schemaVersion": 1,
              "timeZoneId": "China Standard Time",
              "targetUtc": "2099-01-01T00:00:00.0000000+00:00"
            }
            """;
        var result = SettingsContract.Deserialize(json);
        return
            result.Migrated &&
            result.Settings.Countdowns.Count == 1 &&
            result.Settings.Countdowns[0].Name == "我的倒计时" &&
            result.Settings.Notes.Count == 0;
    }

    private static bool RejectsDuplicateCountdowns(CountdownSettings settings)
    {
        var candidate = settings with
        {
            Countdowns = new[]
            {
                new CountdownRecord("duplicate-a", "重复", DateTimeOffset.UtcNow.AddDays(1), Array.Empty<string>()),
                new CountdownRecord("duplicate-b", "重复", DateTimeOffset.UtcNow.AddDays(2), Array.Empty<string>())
            }
        };
        return Rejects(candidate);
    }

    private static bool RejectsInvalidNotes(CountdownSettings settings)
    {
        var candidate = settings with
        {
            Notes = new[]
            {
                new NoteRecord("invalid-note", " ", null, false, Array.Empty<string>(), null, null)
            }
        };
        return Rejects(candidate);
    }

    private static bool RejectsDanglingTag(CountdownSettings settings)
    {
        var candidate = settings with
        {
            Countdowns = new[]
            {
                new CountdownRecord(
                    "dangling-tag",
                    "悬空标签",
                    DateTimeOffset.UtcNow.AddDays(1),
                    new[] { "missing-tag" })
            }
        };
        return Rejects(candidate);
    }

    private static bool Rejects(CountdownSettings candidate)
    {
        try
        {
            SettingsContract.Canonicalize(candidate);
            return false;
        }
        catch (SettingsValidationException)
        {
            return true;
        }
    }

    private static bool ValidateReminderDispatch()
    {
        var start = new DateTimeOffset(2031, 4, 5, 0, 0, 0, TimeSpan.Zero);
        var note = new NoteRecord(
            "reminder-probe",
            "提醒探针",
            "2031-04-05",
            false,
            Array.Empty<string>(),
            30,
            start);
        var settings = CountdownSettings.Empty with { Notes = new[] { note } };
        var scheduler = new ReminderScheduler();
        scheduler.Initialize(settings, start);
        var first = scheduler.GetDue(settings, start.AddMinutes(30));
        var pendingRetry = scheduler.GetDue(settings, start.AddMinutes(30));
        scheduler.ConfirmDisplayed(note, start.AddMinutes(30));
        var duplicate = scheduler.GetDue(settings, start.AddMinutes(30));
        return first.Count == 1 && pendingRetry.Count == 1 && duplicate.Count == 0;
    }

    private static bool ValidateOneTimeReminderLifecycle()
    {
        var firstDueUtc = new DateTimeOffset(2031, 4, 6, 1, 30, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-once-{Guid.NewGuid():N}.settings.json");
        try
        {
            var beforeStore = new SettingsStore(
                clock: new ValidationClock(firstDueUtc.AddSeconds(-1)));
            var before = DesktopWorkspace.Load(settingsPath, beforeStore);
            if (!before.Apply(new SaveNoteChange(
                    null,
                    "一次性提醒生命周期探针",
                    new DateTime(2031, 4, 6),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(firstDueUtc))).Succeeded)
            {
                return false;
            }

            var early = before.GetDueReminders(firstDueUtc.AddSeconds(-1));
            var afterStore = new SettingsStore(
                clock: new ValidationClock(firstDueUtc.AddMinutes(5)));
            var after = DesktopWorkspace.Load(settingsPath, afterStore);
            var overdue = after.GetDueReminders(firstDueUtc.AddMinutes(5));
            var dueNote = overdue.SingleOrDefault();
            if (dueNote?.Reminder is not OnceReminder)
            {
                return false;
            }

            Directory.CreateDirectory(settingsPath + ".tmp");
            var failedConfirmation = after.ConfirmReminderDisplayed(
                dueNote,
                firstDueUtc.AddMinutes(5));
            var retryAfterFailure = after.GetDueReminders(firstDueUtc.AddMinutes(5));
            var pendingSettings = after.Snapshot.Settings;
            var pendingAfterFailure = ReminderRuleCompatibility.ToLegacy(
                pendingSettings,
                pendingSettings.Notes.Single()) is OnceReminder
            {
                Delivery: OnceDeliveryState.Pending
            };
            Directory.Delete(settingsPath + ".tmp");
            var successfulConfirmation = after.ConfirmReminderDisplayed(
                dueNote,
                firstDueUtc.AddMinutes(5));
            if (failedConfirmation.Succeeded ||
                retryAfterFailure.Count != 1 ||
                !pendingAfterFailure ||
                !successfulConfirmation.Succeeded)
            {
                return false;
            }

            var unresolvedInProcess = after.GetDueReminderOccurrences(firstDueUtc.AddMinutes(5));
            var reloaded = DesktopWorkspace.Load(settingsPath, afterStore);
            var unresolvedAfterRestart = reloaded.GetDueReminderOccurrences(firstDueUtc.AddMinutes(5));
            var reloadedOccurrence = unresolvedAfterRestart.SingleOrDefault();
            var disposition = reloadedOccurrence is null
                ? new DesktopWorkspaceChangeResult(false, "没有可处置的 occurrence。")
                : reloaded.AcknowledgeReminder(
                    reloadedOccurrence.Note,
                    reloadedOccurrence.Rule.Id,
                    reloadedOccurrence.ScheduledUtc);
            var disposedAfterRestart = DesktopWorkspace.Load(settingsPath, afterStore)
                .GetDueReminderOccurrences(firstDueUtc.AddMinutes(5));
            return
                early.Count == 0 &&
                overdue.Count == 1 &&
                unresolvedInProcess.Count == 1 &&
                unresolvedInProcess[0].ScheduledUtc == firstDueUtc &&
                unresolvedAfterRestart.Count == 1 &&
                reloadedOccurrence?.ScheduledUtc == firstDueUtc &&
                disposition.Succeeded &&
                disposedAfterRestart.Count == 0 &&
                ReminderRuleCompatibility.ToLegacy(
                    reloaded.Snapshot.Settings,
                    reloaded.Snapshot.Settings.Notes.Single()) is OnceReminder
                {
                    Delivery: OnceDeliveryState.Delivered
                };
        }
        finally
        {
            TryDeleteSettingsFiles(settingsPath);
            File.Delete(settingsPath + ".tmp");
            if (Directory.Exists(settingsPath + ".tmp"))
            {
                Directory.Delete(settingsPath + ".tmp");
            }
        }
    }

    private static RepeatingReminderDeliveryExerciseResult ValidateRepeatingReminderDeliveryCoordinator()
    {
        var firstDueUtc = new DateTimeOffset(2031, 4, 7, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-repeat-render-{Guid.NewGuid():N}.settings.json");
        var migrationPath = settingsPath + ".v9.json";
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new MutableValidationClock(firstDueUtc.AddSeconds(-1));
            var workspace = DesktopWorkspace.Load(settingsPath, new SettingsStore(clock: clock));
            if (!workspace.Apply(new SaveNoteChange(
                    null,
                    "重复提醒真实渲染探针",
                    new DateTime(2031, 4, 7),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(firstDueUtc, 30))).Succeeded)
            {
                return RepeatingReminderDeliveryExerciseResult.Failed;
            }

            var repeatingId = workspace.Snapshot.Settings.Notes.Single().Id;
            var rule = workspace.Snapshot.Settings.Notes.Single().ReminderRules.Single();
            coordinator = new ReminderDeliveryCoordinator(workspace, logger: null);
            clock.UtcNow = firstDueUtc;
            var dueBeforeShow = workspace.GetDueReminders(firstDueUtc)
                .Any(note => note.Id == repeatingId);
            coordinator.Tick(firstDueUtc);
            var window = coordinator.ActiveWindow;
            var windowOpened = window?.IsVisible == true && coordinator.ActiveWindowCount == 1;
            window?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var contentRendered =
                coordinator.ActiveOccurrenceRendered &&
                coordinator.ActiveOccurrenceConfirmed &&
                workspace.GetDueReminderOccurrences(firstDueUtc)
                    .SingleOrDefault() is { } unresolvedOccurrence &&
                unresolvedOccurrence.Note.Id == repeatingId &&
                unresolvedOccurrence.Rule.Id == rule.Id &&
                unresolvedOccurrence.ScheduledUtc == firstDueUtc;
            var nextDueUtc = firstDueUtc.AddMinutes(30);
            clock.UtcNow = nextDueUtc;
            coordinator.Tick(nextDueUtc);
            var nextWindow = coordinator.ActiveWindow;
            var firstKey = new ActiveReminderOccurrenceKey(rule.Id, firstDueUtc);
            var secondKey = new ActiveReminderOccurrenceKey(rule.Id, nextDueUtc);
            var singleUnresolvedOccurrenceWorks =
                window?.IsVisible == true &&
                ReferenceEquals(nextWindow, window) &&
                coordinator.ActiveWindowCount == 1 &&
                coordinator.ActiveEntryCount == 1 &&
                window.EntryCount == 1 &&
                window.OrderedKeys.SequenceEqual([firstKey]) &&
                !window.OrderedKeys.Contains(secondKey) &&
                Application.Current.Windows
                    .OfType<ReminderCenterWindow>()
                    .Count(candidate => candidate.IsVisible) == 1;
            var unresolvedDue = workspace.GetDueReminderOccurrences(nextDueUtc);
            singleUnresolvedOccurrenceWorks &=
                unresolvedDue.Count == 1 &&
                unresolvedDue[0].ScheduledUtc == firstDueUtc;

            var firstAcknowledged = window?.RequestAcknowledgement(firstKey) == true;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            coordinator.Tick(nextDueUtc);
            var secondWindow = coordinator.ActiveWindow;
            secondWindow?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var secondOccurrenceKey = MinminCountdown.Core.ReminderOccurrenceKey.Create(
                rule,
                nextDueUtc).ToString();
            var secondCycleOpenedAfterDisposition =
                firstAcknowledged &&
                window?.IsVisible == false &&
                secondWindow?.IsVisible == true &&
                !ReferenceEquals(secondWindow, window) &&
                coordinator.ActiveWindowCount == 1 &&
                coordinator.ActiveEntryCount == 1 &&
                secondWindow.EntryCount == 1 &&
                secondWindow.RenderedEntryCount == 1 &&
                secondWindow.OrderedKeys.SequenceEqual([secondKey]) &&
                workspace.Snapshot.Settings.ReminderStates[rule.Id].LastDeliveredOccurrenceKey ==
                    secondOccurrenceKey;
            var secondAcknowledged = secondWindow?.RequestAcknowledgement(secondKey) == true;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var acknowledgementAdvancedToSecond =
                workspace.Snapshot.Settings.ReminderStates[rule.Id].LastDispositionOccurrenceKey ==
                    secondOccurrenceKey;
            var staleFirstAcknowledgement = workspace.AcknowledgeReminder(
                workspace.Snapshot.Settings.Notes.Single(note => note.Id == repeatingId),
                rule.Id,
                firstDueUtc);
            var persistedAfterReverseAcknowledgement = new SettingsStore(clock: clock)
                .Load(settingsPath)
                .Settings
                .ReminderStates[rule.Id];
            coordinator.Tick(nextDueUtc);
            var acknowledgementMonotonicAcrossOccurrences =
                secondAcknowledged &&
                acknowledgementAdvancedToSecond &&
                staleFirstAcknowledgement.Succeeded &&
                workspace.Snapshot.Settings.ReminderStates[rule.Id].LastDispositionOccurrenceKey ==
                    secondOccurrenceKey &&
                persistedAfterReverseAcknowledgement.LastDispositionOccurrenceKey ==
                    secondOccurrenceKey;
            var noDuplicate =
                firstAcknowledged &&
                secondAcknowledged &&
                secondCycleOpenedAfterDisposition &&
                coordinator.ActiveWindowCount == 0 &&
                coordinator.ActiveEntryCount == 0 &&
                workspace.GetDueReminderOccurrences(nextDueUtc).Count == 0;

            coordinator.Dispose();
            coordinator = null;
            var latestMissedUtc = firstDueUtc.AddHours(2).AddMinutes(5);
            clock.UtcNow = latestMissedUtc;
            var restarted = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: clock));
            var latestDue = restarted.GetDueReminders(latestMissedUtc);
            var latestNote = latestDue.SingleOrDefault();
            var latestRule = latestNote?.ReminderRules.SingleOrDefault();
            var latestScheduledUtc = latestRule is null
                ? DateTimeOffset.MinValue
                : ReminderSchedule.GetScheduledUtc(latestRule, latestMissedUtc);
            var beforeFailure = SettingsContract.SerializeCanonical(restarted.Snapshot.Settings);
            Directory.CreateDirectory(settingsPath + ".tmp");
            var failedLatestConfirmation = latestNote is null
                ? new DesktopWorkspaceChangeResult(false, "没有最新 occurrence。")
                : restarted.ConfirmReminderDisplayed(
                    latestNote,
                    latestMissedUtc);
            var failureStayedAtomic =
                !failedLatestConfirmation.Succeeded &&
                string.Equals(beforeFailure, SettingsContract.SerializeCanonical(restarted.Snapshot.Settings), StringComparison.Ordinal) &&
                restarted.GetDueReminders(latestMissedUtc).Count == 1;
            Directory.Delete(settingsPath + ".tmp");
            var crossedRetryUtc = latestMissedUtc.AddMinutes(31);
            var successfulLatestConfirmation = latestNote is null
                ? new DesktopWorkspaceChangeResult(false, "没有最新 occurrence。")
                : restarted.ConfirmReminderDisplayed(
                    latestNote,
                    latestRule!.Id,
                    latestScheduledUtc);
            var unresolvedAtConfirmation = restarted.GetDueReminderOccurrences(latestMissedUtc)
                .SingleOrDefault();
            var unresolvedAfterLaterCycles = restarted.GetDueReminderOccurrences(crossedRetryUtc)
                .SingleOrDefault();
            var latestOnlyAcrossRestart =
                latestDue.Count == 1 &&
                latestScheduledUtc == firstDueUtc.AddHours(2) &&
                failureStayedAtomic &&
                successfulLatestConfirmation.Succeeded &&
                unresolvedAtConfirmation?.ScheduledUtc == latestScheduledUtc &&
                unresolvedAfterLaterCycles?.ScheduledUtc == latestScheduledUtc &&
                latestRule is not null &&
                restarted.Snapshot.Settings.ReminderStates[latestRule.Id].LastDeliveredOccurrenceKey ==
                    MinminCountdown.Core.ReminderOccurrenceKey.Create(
                        latestRule,
                        latestScheduledUtc).ToString();

            File.WriteAllText(
                migrationPath,
                """
                {
                  "schemaVersion": 9,
                  "timeZoneId": "China Standard Time",
                  "displayPrecision": "Second",
                  "monthlySalary": 0,
                  "workStart": "09:00",
                  "workEnd": "18:00",
                  "themePreference": "Auto",
                  "tags": [],
                  "countdowns": [],
                  "notes": [
                    {
                      "id": "repeat-migration-note",
                      "text": "迁移不复活历史周期",
                      "date": "2031-04-07",
                      "isCompleted": false,
                      "tagIds": [],
                      "reminder": {
                        "type": "repeat",
                        "firstDueUtc": "2031-04-07T01:00:00.0000000+00:00",
                        "intervalMinutes": 30
                      }
                    }
                  ]
                }
                """,
                new UTF8Encoding(false));
            var migrated = DesktopWorkspace.Load(
                migrationPath,
                new SettingsStore(clock: clock));
            var migrationRule = migrated.Snapshot.Settings.Notes.Single().ReminderRules.Single();
            var migrationDoesNotRevive =
                migrated.Snapshot.SettingsWereMigrated &&
                migrated.GetDueReminders(latestMissedUtc).Count == 0 &&
                migrated.Snapshot.Settings.ReminderStates[migrationRule.Id].HandledThroughUtc ==
                    firstDueUtc.AddHours(2);
            var retryKeepsCapturedOccurrence =
                ValidateRepeatingReminderRetryKeepsCapturedOccurrence();
            var renderedLatchWorks =
                dueBeforeShow &&
                windowOpened &&
                contentRendered &&
                noDuplicate &&
                latestOnlyAcrossRestart &&
                migrationDoesNotRevive &&
                retryKeepsCapturedOccurrence;
            return new RepeatingReminderDeliveryExerciseResult(
                renderedLatchWorks,
                singleUnresolvedOccurrenceWorks,
                dueBeforeShow,
                windowOpened,
                contentRendered && secondCycleOpenedAfterDisposition,
                noDuplicate,
                acknowledgementMonotonicAcrossOccurrences);
        }
        finally
        {
            coordinator?.Dispose();
            TryDeleteSettingsFiles(settingsPath);
            File.Delete(settingsPath + ".tmp");
            File.Delete(migrationPath);
        }
    }

    private static bool ValidateRepeatingReminderRetryKeepsCapturedOccurrence()
    {
        var firstDueUtc = new DateTimeOffset(2031, 4, 8, 1, 0, 0, TimeSpan.Zero);
        var nextDueUtc = firstDueUtc.AddMinutes(1);
        var staleRetryUtc = nextDueUtc.AddSeconds(10);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-repeat-retry-{Guid.NewGuid():N}.settings.json");
        var blockedTemporaryPath = settingsPath + ".tmp";
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new MutableValidationClock(firstDueUtc.AddSeconds(-1));
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: clock));
            if (!workspace.Apply(new SaveNoteChange(
                    null,
                    "跨周期持久化重试 occurrence 探针",
                    new DateTime(2031, 4, 8),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(firstDueUtc, 1))).Succeeded)
            {
                return false;
            }

            var note = workspace.Snapshot.Settings.Notes.Single();
            var rule = (FixedIntervalReminderRule)note.ReminderRules.Single();
            Directory.CreateDirectory(blockedTemporaryPath);
            coordinator = new ReminderDeliveryCoordinator(
                workspace,
                new DiagnosticLogger(diagnosticPath));
            clock.UtcNow = firstDueUtc;
            coordinator.Tick(firstDueUtc);
            var firstWindow = coordinator.ActiveWindow;
            firstWindow?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var capturedOccurrenceFailed =
                firstWindow?.IsVisible == true &&
                coordinator.ActiveOccurrenceRendered &&
                !coordinator.ActiveOccurrenceConfirmed &&
                coordinator.PersistenceFailureCount == 1 &&
                coordinator.NextPersistenceRetryUtc == firstDueUtc.AddSeconds(5) &&
                !workspace.Snapshot.Settings.ReminderStates.ContainsKey(rule.Id);

            clock.UtcNow = nextDueUtc;
            coordinator.Tick(nextDueUtc);
            var nextWindow = coordinator.ActiveWindow;
            var failureEventsBeforeBarrierRemoved = File.ReadLines(diagnosticPath).Count(line =>
                line.Contains("OneTimeReminderDeliveryPersistFailed", StringComparison.Ordinal));
            var nextCycleSuppressedWhileOldRetryFailed =
                firstWindow?.IsVisible == true &&
                ReferenceEquals(nextWindow, firstWindow) &&
                coordinator.ActiveWindowCount == 1 &&
                coordinator.ActiveEntryCount == 1 &&
                firstWindow.EntryCount == 1 &&
                firstWindow.OrderedKeys.SequenceEqual([
                    new ActiveReminderOccurrenceKey(rule.Id, firstDueUtc)
                ]) &&
                failureEventsBeforeBarrierRemoved == 2 &&
                !workspace.Snapshot.Settings.ReminderStates.ContainsKey(rule.Id);

            Directory.Delete(blockedTemporaryPath);
            clock.UtcNow = staleRetryUtc;
            coordinator.Tick(staleRetryUtc);
            var stateAfterStaleRetry = workspace.Snapshot.Settings.ReminderStates.GetValueOrDefault(rule.Id);
            var persistedAfterStaleRetry = new SettingsStore(clock: clock)
                .Load(settingsPath)
                .Settings
                .ReminderStates
                .GetValueOrDefault(rule.Id);
            var firstOccurrenceKey =
                MinminCountdown.Core.ReminderOccurrenceKey.Create(rule, firstDueUtc).ToString();
            var retryKeptCapturedOccurrence =
                stateAfterStaleRetry?.HandledThroughUtc == firstDueUtc &&
                stateAfterStaleRetry.LastDeliveredOccurrenceKey == firstOccurrenceKey &&
                persistedAfterStaleRetry?.HandledThroughUtc == firstDueUtc &&
                persistedAfterStaleRetry.LastDeliveredOccurrenceKey == firstOccurrenceKey &&
                firstWindow?.PersistenceFailureVisible == false &&
                coordinator.ActiveEntryCount == 1 &&
                workspace.GetDueReminderOccurrences(staleRetryUtc)
                    .SingleOrDefault() is { } unresolvedOccurrence &&
                unresolvedOccurrence.Note.Id == note.Id &&
                unresolvedOccurrence.Rule.Id == rule.Id &&
                unresolvedOccurrence.ScheduledUtc == firstDueUtc;

            var firstAcknowledged = firstWindow?.RequestAcknowledgement(
                new ActiveReminderOccurrenceKey(rule.Id, firstDueUtc)) == true;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            coordinator.Tick(staleRetryUtc);
            var secondWindow = coordinator.ActiveWindow;
            secondWindow?.UpdateLayout();
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            var nextOccurrenceKey =
                MinminCountdown.Core.ReminderOccurrenceKey.Create(rule, nextDueUtc).ToString();
            var nextCycleAppearedAfterDisposition =
                firstAcknowledged &&
                firstWindow?.IsVisible == false &&
                secondWindow?.IsVisible == true &&
                coordinator.ActiveWindowCount == 1 &&
                coordinator.ActiveEntryCount == 1 &&
                secondWindow.OrderedKeys.SequenceEqual([
                    new ActiveReminderOccurrenceKey(rule.Id, nextDueUtc)
                ]) &&
                workspace.Snapshot.Settings.ReminderStates[rule.Id].LastDeliveredOccurrenceKey ==
                    nextOccurrenceKey;
            var nextAcknowledged = secondWindow?.RequestAcknowledgement(
                new ActiveReminderOccurrenceKey(rule.Id, nextDueUtc)) == true;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            coordinator.Tick(staleRetryUtc);
            var nextOccurrenceDidNotReopen =
                nextAcknowledged &&
                coordinator.ActiveWindowCount == 0 &&
                coordinator.ActiveEntryCount == 0 &&
                !workspace.GetDueReminders(staleRetryUtc)
                    .Any(candidate => candidate.Id == note.Id);
            return
                capturedOccurrenceFailed &&
                nextCycleSuppressedWhileOldRetryFailed &&
                retryKeptCapturedOccurrence &&
                nextCycleAppearedAfterDisposition &&
                nextOccurrenceDidNotReopen;
        }
        finally
        {
            coordinator?.Dispose();
            TryDeleteSettingsFiles(settingsPath);
            File.Delete(diagnosticPath);
            if (Directory.Exists(blockedTemporaryPath))
            {
                Directory.Delete(blockedTemporaryPath);
            }
            else
            {
                File.Delete(blockedTemporaryPath);
            }
        }
    }

    private static bool ValidateReminderPersistenceFailureDispose()
    {
        var firstDueUtc = new DateTimeOffset(2031, 4, 7, 2, 30, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-delivery-dispose-{Guid.NewGuid():N}.settings.json");
        var blockedTemporaryPath = settingsPath + ".tmp";
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new MutableValidationClock(firstDueUtc.AddSeconds(-1));
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: clock));
            if (!workspace.Apply(new SaveNoteChange(
                    null,
                    "退出时强制关闭持久化失败提醒探针",
                    new DateTime(2031, 4, 7),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(firstDueUtc))).Succeeded)
            {
                return false;
            }

            var noteId = workspace.Snapshot.Settings.Notes.Single().Id;
            var noteRule = workspace.Snapshot.Settings.Notes.Single().ReminderRules.Single();
            var beforeFailure = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
            Directory.CreateDirectory(blockedTemporaryPath);
            coordinator = new ReminderDeliveryCoordinator(
                workspace,
                new DiagnosticLogger(diagnosticPath));
            clock.UtcNow = firstDueUtc;
            coordinator.Tick(firstDueUtc);
            var window = coordinator.ActiveWindow;
            PumpDispatcher();
            var firstRetryUtc = coordinator.NextPersistenceRetryUtc;
            var firstFailureCount = coordinator.PersistenceFailureCount;
            window?.Close();
            PumpDispatcher();
            var ordinaryCloseGuarded =
                window?.IsVisible == true &&
                coordinator.ActiveWindowCount == 1 &&
                firstFailureCount == 1 &&
                firstRetryUtc == firstDueUtc.AddSeconds(5);

            coordinator.Dispose();
            PumpDispatcher();
            var failureEvents = File.ReadLines(diagnosticPath).Count(line =>
                line.Contains("OneTimeReminderDeliveryPersistFailed", StringComparison.Ordinal));
            var stillPending = ReminderRuleCompatibility.ToLegacy(
                workspace.Snapshot.Settings,
                workspace.Snapshot.Settings.Notes.Single(note => note.Id == noteId)) is
                OnceReminder { Delivery: OnceDeliveryState.Pending };
            Directory.Delete(blockedTemporaryPath);
            var restarted = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: clock));
            var restartedOccurrence = restarted.GetDueReminderOccurrences(firstDueUtc)
                .SingleOrDefault();
            var sameOccurrenceRestarts =
                restartedOccurrence?.Note.Id == noteId &&
                restartedOccurrence.Rule.Id == noteRule.Id &&
                restartedOccurrence.ScheduledUtc == firstDueUtc &&
                !restarted.Snapshot.Settings.ReminderStates.ContainsKey(noteRule.Id);
            var disposeBehavior =
                ordinaryCloseGuarded &&
                window?.IsVisible == false &&
                coordinator.ActiveWindowCount == 0 &&
                failureEvents >= 1 &&
                stillPending &&
                string.Equals(beforeFailure, SettingsContract.SerializeCanonical(workspace.Snapshot.Settings), StringComparison.Ordinal) &&
                sameOccurrenceRestarts;
            return disposeBehavior;
        }
        finally
        {
            coordinator?.Dispose();
            TryDeleteSettingsFiles(settingsPath);
            File.Delete(diagnosticPath);
            if (Directory.Exists(blockedTemporaryPath))
            {
                Directory.Delete(blockedTemporaryPath);
            }
            else
            {
                File.Delete(blockedTemporaryPath);
            }
        }
    }

    private static bool ValidateReminderCenterAppendsAfterOpen()
    {
        var firstDueUtc = new DateTimeOffset(2031, 5, 5, 1, 0, 0, TimeSpan.Zero);
        var secondDueUtc = firstDueUtc.AddMinutes(1);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-center-append-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new MutableValidationClock(firstDueUtc.AddSeconds(-1));
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: clock));
            if (!workspace.Apply(new SaveNoteChange(
                    null,
                    "先到提醒追加探针",
                    new DateTime(2031, 5, 5),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(firstDueUtc))).Succeeded)
            {
                return false;
            }

            var firstNote = workspace.Snapshot.Settings.Notes.Single();
            var firstRule = firstNote.ReminderRules.Single();
            var firstKey = new ActiveReminderOccurrenceKey(firstRule.Id, firstDueUtc);
            coordinator = new ReminderDeliveryCoordinator(workspace, null);
            clock.UtcNow = firstDueUtc;
            coordinator.Tick(clock.UtcNow);
            var center = coordinator.ActiveWindow;
            PumpDispatcher();
            var firstRendered =
                center?.IsVisible == true &&
                center.EntryCount == 1 &&
                center.RenderedEntryCount == 1 &&
                center.OrderedKeys.SequenceEqual([firstKey]);

            if (!workspace.Apply(new SaveNoteChange(
                    null,
                    "后到提醒追加探针",
                    new DateTime(2031, 5, 5),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(secondDueUtc))).Succeeded)
            {
                return false;
            }

            var secondNote = workspace.Snapshot.Settings.Notes.Single(note =>
                string.Equals(note.Text, "后到提醒追加探针", StringComparison.Ordinal));
            var secondRule = secondNote.ReminderRules.Single();
            var secondKey = new ActiveReminderOccurrenceKey(secondRule.Id, secondDueUtc);
            clock.UtcNow = secondDueUtc;
            coordinator.Tick(clock.UtcNow);
            var sameCenter = coordinator.ActiveWindow;
            PumpDispatcher();
            return
                firstRendered &&
                ReferenceEquals(sameCenter, center) &&
                sameCenter?.IsVisible == true &&
                coordinator.ActiveWindowCount == 1 &&
                coordinator.ActiveEntryCount == 2 &&
                sameCenter.EntryCount == 2 &&
                sameCenter.RenderedEntryCount == 2 &&
                sameCenter.OrderedKeys.SequenceEqual([firstKey, secondKey]) &&
                workspace.Snapshot.Settings.ReminderStates[firstRule.Id].LastDeliveredOccurrenceKey ==
                    MinminCountdown.Core.ReminderOccurrenceKey.Create(firstRule, firstDueUtc).ToString() &&
                workspace.Snapshot.Settings.ReminderStates[secondRule.Id].LastDeliveredOccurrenceKey ==
                    MinminCountdown.Core.ReminderOccurrenceKey.Create(secondRule, secondDueUtc).ToString();
        }
        finally
        {
            coordinator?.Dispose();
            TryDeleteSettingsFiles(settingsPath);
            File.Delete(settingsPath + ".tmp");
        }
    }

    private static ReminderCenterExerciseResult ValidateReminderCenterBehavior(
        ApplicationTheme theme,
        string assetRoot)
    {
        var dueUtc = new DateTimeOffset(2031, 5, 6, 1, 30, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-center-{Guid.NewGuid():N}.settings.json");
        var blockedTemporaryPath = settingsPath + ".tmp";
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        ReminderDeliveryCoordinator? coordinator = null;
        MainWindow? sourceWindow = null;
        HolidayCalendar? calendar = null;
        try
        {
            var clock = new MutableValidationClock(dueUtc.AddSeconds(-1));
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: clock));
            if (!workspace.Apply(new SaveNoteChange(
                    null,
                    "批量提醒甲",
                    new DateTime(2031, 5, 6),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(dueUtc))).Succeeded ||
                !workspace.Apply(new SaveNoteChange(
                    null,
                    "批量提醒乙",
                    new DateTime(2031, 5, 6),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(dueUtc))).Succeeded)
            {
                return ReminderCenterExerciseResult.Failed;
            }

            var firstNote = workspace.Snapshot.Settings.Notes[0];
            var secondNote = workspace.Snapshot.Settings.Notes[1];
            var firstRule = firstNote.ReminderRules.Single();
            var secondRule = secondNote.ReminderRules.Single();
            var firstKey = new ActiveReminderOccurrenceKey(firstRule.Id, dueUtc);
            var secondKey = new ActiveReminderOccurrenceKey(secondRule.Id, dueUtc);
            workspace.ApplyFilter(DesktopWorkspaceFilter.Empty with
            {
                NoteSearch = "不会命中任何便签的筛选"
            });
            calendar = new HolidayCalendar(assetRoot);
            sourceWindow = new MainWindow(
                workspace,
                calendar,
                new DiagnosticLogger(diagnosticPath),
                assetRoot)
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            sourceWindow.ShowWorkspace(WorkspaceKind.Countdowns);
            var draftText = Required<TextBox>(sourceWindow, "NoteText");
            var draftDate = Required<DatePicker>(sourceWindow, "NoteDate");
            draftText.Text = "必须保留的便签草稿";
            draftDate.SelectedDate = new DateTime(2031, 5, 9);
            sourceWindow.Show();
            PumpDispatcher();
            sourceWindow.WindowState = WindowState.Minimized;
            PumpDispatcher();
            var sourceWasVisibleAndMinimized =
                sourceWindow.IsVisible &&
                sourceWindow.WindowState == WindowState.Minimized;
            var openedSourceIds = new List<string>();
            coordinator = new ReminderDeliveryCoordinator(
                workspace,
                new DiagnosticLogger(diagnosticPath),
                (sourceKind, noteId) =>
                {
                    if (sourceKind != ReminderSourceKind.Note)
                    {
                        return;
                    }

                    openedSourceIds.Add(noteId);
                    sourceWindow.OpenReminderSource(sourceKind, noteId);
                });

            clock.UtcNow = dueUtc;
            coordinator.Tick(dueUtc);
            var center = coordinator.ActiveWindow;
            var singleWindowBeforeRender =
                center?.IsVisible == true &&
                coordinator.ActiveWindowCount == 1 &&
                Application.Current.Windows
                    .OfType<ReminderCenterWindow>()
                    .Count(window => window.IsVisible) == 1;
            var sameTickBatch =
                center?.EntryCount == 2 &&
                center.RenderedEntryCount == 0 &&
                workspace.Snapshot.Settings.ReminderStates.Count == 0;
            var stableOrdering = center?.OrderedKeys.SequenceEqual([firstKey, secondKey]) == true;
            PumpDispatcher();
            var entriesRendered =
                center?.RenderedEntryCount == 2 &&
                workspace.Snapshot.Settings.ReminderStates[firstRule.Id].LastDeliveredOccurrenceKey ==
                    MinminCountdown.Core.ReminderOccurrenceKey.Create(firstRule, dueUtc).ToString() &&
                workspace.Snapshot.Settings.ReminderStates[secondRule.Id].LastDeliveredOccurrenceKey ==
                    MinminCountdown.Core.ReminderOccurrenceKey.Create(secondRule, dueUtc).ToString();
            var acknowledgementDistinctFromDelivery =
                workspace.Snapshot.Settings.ReminderStates.Values.All(state =>
                    state.LastDeliveredOccurrenceKey is not null &&
                    state.LastAcknowledgedOccurrenceKey is null) &&
                center?.EntryCount == 2;

            var visibleText = center is null
                ? Array.Empty<string>()
                : Descendants<TextBlock>(center).Select(text => text.Text).ToArray();
            var buttons = center is null
                ? Array.Empty<Button>()
                : Descendants<Button>(center).ToArray();
            var displaysSourceContentAndTime =
                visibleText.Count(text => string.Equals(text, "便签", StringComparison.Ordinal)) == 2 &&
                visibleText.Contains(firstNote.Text, StringComparer.Ordinal) &&
                visibleText.Contains(secondNote.Text, StringComparer.Ordinal) &&
                visibleText.Count(text => text.Contains("2031 年 5 月 6 日 09:30", StringComparison.Ordinal) &&
                                          text.Contains("北京时间", StringComparison.Ordinal)) == 2;
            static bool NameStartsWith(Button button, string prefix) =>
                AutomationProperties.GetName(button).StartsWith(prefix, StringComparison.Ordinal);
            var hasOnlySafeActions =
                buttons.Count(button => NameStartsWith(button, "知道了：")) == 2 &&
                buttons.Count(button => NameStartsWith(button, "稍后提醒：")) == 2 &&
                buttons.Count(button => NameStartsWith(button, "完成便签：")) == 2 &&
                buttons.Count(button => NameStartsWith(button, "打开便签来源：")) == 2 &&
                buttons.Count(button => string.Equals(
                    AutomationProperties.GetAutomationId(button),
                    "ReminderCenterAcknowledgeAll",
                    StringComparison.Ordinal)) == 1 &&
                buttons.Count(button => string.Equals(
                    AutomationProperties.GetAutomationId(button),
                    "ReminderCenterMinimizeToTaskbar",
                    StringComparison.Ordinal)) == 1 &&
                buttons.Length == 10;
            var accessible =
                center is not null &&
                string.Equals(AutomationProperties.GetName(center), "提醒中心", StringComparison.Ordinal) &&
                buttons.All(button => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(button))) &&
                Descendants<ScrollViewer>(center).All(scroller =>
                    !string.IsNullOrWhiteSpace(AutomationProperties.GetName(scroller)));

            var beforeOpenSource = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
            var filterBeforeOpen = workspace.Snapshot.Filter;
            var openSourceRaised = center?.RequestOpenSource(firstKey) == true;
            PumpDispatcher();
            var filterAfterOpen = workspace.Snapshot.Filter;
            var openSourcePreservesState =
                openSourceRaised &&
                string.Equals(beforeOpenSource, SettingsContract.SerializeCanonical(workspace.Snapshot.Settings), StringComparison.Ordinal) &&
                FiltersEqual(filterBeforeOpen, filterAfterOpen) &&
                string.Equals(draftText.Text, "必须保留的便签草稿", StringComparison.Ordinal) &&
                draftDate.SelectedDate == new DateTime(2031, 5, 9) &&
                sourceWindow.CurrentWorkspace == WorkspaceKind.Notes &&
                center?.EntryCount == 2;
            var openSourceRestoresMinimizedWindow =
                sourceWasVisibleAndMinimized &&
                sourceWindow.IsVisible &&
                sourceWindow.WindowState == WindowState.Normal &&
                sourceWindow.IsActive;
            var sourceWasFocused =
                openedSourceIds.SequenceEqual([firstNote.Id]) &&
                Descendants<Border>(sourceWindow).Any(border =>
                    AutomationProperties.GetName(border).Contains(
                        $"当前提醒来源便签：{firstNote.Text}",
                        StringComparison.Ordinal)) &&
                Keyboard.FocusedElement is Button { Tag: string focusedNoteId } &&
                string.Equals(focusedNoteId, firstNote.Id, StringComparison.OrdinalIgnoreCase);
            sourceWindow.UpdateTime(ChinaStandardTime.ToBeijingTime(dueUtc.AddMinutes(1)));
            PumpDispatcher();
            var openSourceFindsExactNote =
                sourceWasFocused &&
                !Descendants<Border>(sourceWindow).Any(border =>
                    AutomationProperties.GetName(border).Contains(
                        $"当前提醒来源便签：{firstNote.Text}",
                        StringComparison.Ordinal));

            Directory.CreateDirectory(blockedTemporaryPath);
            var beforeAcknowledgementFailure = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
            var acknowledgementRequested = center?.RequestAcknowledgement(firstKey) == true;
            var acknowledgementFailureRetainsEntry =
                acknowledgementRequested &&
                center?.EntryCount == 2 &&
                center.IsFailureVisible(firstKey) &&
                !center.IsAcknowledgeEnabled(firstKey) &&
                center.RequestOpenSource(firstKey) &&
                workspace.Snapshot.Settings.ReminderStates[firstRule.Id].LastAcknowledgedOccurrenceKey is null;
            var acknowledgementFailureIsAtomic =
                string.Equals(
                    beforeAcknowledgementFailure,
                    SettingsContract.SerializeCanonical(workspace.Snapshot.Settings),
                    StringComparison.Ordinal);

            Directory.Delete(blockedTemporaryPath);
            clock.UtcNow = dueUtc.AddSeconds(5);
            coordinator.Tick(clock.UtcNow);
            PumpDispatcher();
            var firstOccurrenceKey = MinminCountdown.Core.ReminderOccurrenceKey.Create(
                firstRule,
                dueUtc).ToString();
            var persistedAfterRetry = new SettingsStore(clock: clock)
                .Load(settingsPath)
                .Settings
                .ReminderStates[firstRule.Id];
            var singleItemDisposition =
                center?.EntryCount == 1 &&
                !center.OrderedKeys.Contains(firstKey) &&
                center.OrderedKeys.Contains(secondKey) &&
                workspace.Snapshot.Settings.ReminderStates[firstRule.Id].LastAcknowledgedOccurrenceKey == firstOccurrenceKey &&
                persistedAfterRetry.LastAcknowledgedOccurrenceKey == firstOccurrenceKey;

            var secondAcknowledged = center?.RequestAcknowledgement(secondKey) == true;
            PumpDispatcher();
            var closesWhenEmpty =
                secondAcknowledged &&
                center?.IsVisible == false &&
                coordinator.ActiveWindowCount == 0 &&
                coordinator.ActiveEntryCount == 0;

            var reopenedDueUtc = dueUtc.AddMinutes(1);
            var reopenedSaved = workspace.Apply(new SaveNoteChange(
                null,
                "中心关闭后重新提醒",
                new DateTime(2031, 5, 6),
                false,
                Array.Empty<string>(),
                ReminderSchedule.CreateOnce(reopenedDueUtc))).Succeeded;
            clock.UtcNow = reopenedDueUtc;
            coordinator.Tick(reopenedDueUtc);
            var reopenedCenter = coordinator.ActiveWindow;
            PumpDispatcher();
            var reopens =
                reopenedSaved &&
                reopenedCenter?.IsVisible == true &&
                !ReferenceEquals(reopenedCenter, center) &&
                reopenedCenter.EntryCount == 1 &&
                reopenedCenter.RenderedEntryCount == 1 &&
                coordinator.ActiveWindowCount == 1;

            coordinator.Dispose();
            coordinator = null;
            PumpDispatcher();

            var layoutWindow = new ReminderCenterWindow
            {
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20_000,
                Top = -20_000,
                Opacity = 0
            };
            for (var index = 11; index >= 0; index--)
            {
                var scheduledUtc = dueUtc.AddMinutes(index / 3);
                layoutWindow.AddEntry(new ReminderCenterItem(
                    new ActiveReminderOccurrenceKey($"layout-rule-{index:D2}", scheduledUtc),
                    new NoteRecord(
                        $"layout-note-{index:D2}",
                        $"长内容提醒 {index:D2}：{new string('测', 96)}",
                        "2031-05-06",
                        false,
                        Array.Empty<string>(),
                        NoReminder.Instance),
                    index / 3,
                    index % 3));
            }

            try
            {
                layoutWindow.Show();
                PumpDispatcher();
                layoutWindow.UpdateLayout();
                var scroller = Descendants<ScrollViewer>(layoutWindow).Single();
                var longBodies = Descendants<TextBlock>(layoutWindow)
                    .Where(text => text.Text.StartsWith("长内容提醒", StringComparison.Ordinal))
                    .ToArray();
                var scrollsLongContent =
                    scroller.Content is StackPanel &&
                    scroller.VerticalScrollBarVisibility == ScrollBarVisibility.Auto &&
                    scroller.ScrollableHeight > 0 &&
                    longBodies.Length == 12 &&
                    longBodies.All(text => text.TextWrapping == TextWrapping.Wrap && text.ActualHeight > 0);
                theme.Apply();
                PumpDispatcher();
                var surface = ((Border)layoutWindow.Content).Background;
                var followsDayTheme =
                    surface is SolidColorBrush brush &&
                    brush.Color == ((SolidColorBrush)Application.Current.FindResource("ReminderPageBrush")).Color;

                return new ReminderCenterExerciseResult(
                    singleWindowBeforeRender,
                    sameTickBatch,
                    stableOrdering && layoutWindow.OrderedKeys.SequenceEqual(
                        layoutWindow.OrderedKeys
                            .OrderBy(key => key.ScheduledUtc)
                            .ThenBy(key => key.RuleId, StringComparer.Ordinal)),
                    entriesRendered,
                    acknowledgementDistinctFromDelivery,
                    acknowledgementFailureRetainsEntry,
                    acknowledgementFailureIsAtomic,
                    singleItemDisposition,
                    openSourcePreservesState,
                    openSourceFindsExactNote,
                    openSourceRestoresMinimizedWindow,
                    closesWhenEmpty,
                    reopens,
                    scrollsLongContent,
                    followsDayTheme,
                    hasOnlySafeActions,
                    displaysSourceContentAndTime,
                    accessible);
            }
            finally
            {
                layoutWindow.ForceClose();
            }
        }
        finally
        {
            coordinator?.Dispose();
            if (sourceWindow?.IsVisible == true)
            {
                sourceWindow.Close();
            }

            calendar?.Dispose();
            TryDeleteSettingsFiles(settingsPath);
            File.Delete(diagnosticPath);
            if (Directory.Exists(blockedTemporaryPath))
            {
                Directory.Delete(blockedTemporaryPath);
            }
            else
            {
                File.Delete(blockedTemporaryPath);
            }
        }
    }

    // 提醒中心代表态真实帧：与 AI 目标图同一画面（单条便签条目、计划 2026-09-10 00:36
    // 北京时间、真实 render probe 与送达写盘确认后处置动作启用）。像素只证明白底页面、
    // 暖黄卡、待处理徽章、主行动按钮与便签来源图标都在场，不钉逐像素基线。
    private static ReminderCenterCaptureValidation ValidateReminderCenterRepresentativeCapture()
    {
        const int captureWidth = 460;
        const int captureHeight = 520;
        var dueUtc = new DateTimeOffset(2026, 9, 9, 16, 36, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-center-capture-{Guid.NewGuid():N}.settings.json");
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new MutableValidationClock(dueUtc.AddSeconds(-1));
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: clock));
            if (!workspace.Apply(new SaveNoteChange(
                    null,
                    "验证点击添加便签",
                    new DateTime(2026, 9, 10),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(dueUtc))).Succeeded)
            {
                return ReminderCenterCaptureValidation.Failed;
            }

            clock.UtcNow = dueUtc;
            coordinator = new ReminderDeliveryCoordinator(
                workspace,
                logger: null);
            coordinator.Tick(dueUtc);
            var center = coordinator.ActiveWindow;
            if (center is null)
            {
                return ReminderCenterCaptureValidation.Failed;
            }

            var rule = workspace.Snapshot.Settings.Notes.Single().ReminderRules.Single();
            var key = new ActiveReminderOccurrenceKey(rule.Id, dueUtc);
            for (var attempt = 0; attempt < 40 && !center.IsAcknowledgeEnabled(key); attempt++)
            {
                PumpDispatcher();
            }

            if (center.EntryCount != 1 ||
                !center.IsAcknowledgeEnabled(key) ||
                !center.IsAcknowledgeAllEnabled ||
                center.Content is not FrameworkElement root)
            {
                return ReminderCenterCaptureValidation.Failed;
            }

            center.UpdateLayout();
            var capture = CaptureOpaqueMainSurface(root, captureWidth, captureHeight);
            var pngBytes = EncodePng(capture);
            var (pageWhite, warmCard, pendingBadge, mainAction, sourceIcon) =
                CountReminderCenterCapturePixels(capture);
            var evidencePath = string.Empty;
            var evidenceDirectory = Environment.GetEnvironmentVariable(
                "MINMIN_COUNTDOWN_VISUAL_EVIDENCE_DIR");
            if (!string.IsNullOrWhiteSpace(evidenceDirectory))
            {
                Directory.CreateDirectory(evidenceDirectory);
                var captureName = Environment.GetEnvironmentVariable(
                    "MINMIN_COUNTDOWN_VISUAL_CAPTURE_NAME");
                captureName = string.Equals(captureName, "after", StringComparison.OrdinalIgnoreCase)
                    ? "after"
                    : "before";
                evidencePath = Path.Combine(evidenceDirectory, $"{captureName}-reminder-center.png");
                File.WriteAllBytes(evidencePath, pngBytes);
            }

            var rendered =
                center.IsVisible &&
                capture.PixelWidth == captureWidth &&
                capture.PixelHeight == captureHeight &&
                pageWhite >= 60_000 &&
                warmCard >= 20_000 &&
                pendingBadge >= 100 &&
                mainAction >= 400 &&
                sourceIcon >= 400;
            return new ReminderCenterCaptureValidation(
                rendered,
                capture.PixelWidth,
                capture.PixelHeight,
                Convert.ToHexString(SHA256.HashData(pngBytes)),
                pageWhite,
                warmCard,
                pendingBadge,
                mainAction,
                sourceIcon,
                evidencePath);
        }
        finally
        {
            coordinator?.Dispose();
            PumpDispatcher();
            TryDeleteSettingsFiles(settingsPath);
            if (Directory.Exists(settingsPath + ".tmp"))
            {
                Directory.Delete(settingsPath + ".tmp");
            }
        }
    }

    private static (int PageWhite, int WarmCard, int PendingBadge, int MainAction, int SourceIcon)
        CountReminderCenterCapturePixels(BitmapSource capture)
    {
        var (pixels, _) = GetBgra32Pixels(capture);
        var pageWhite = 0;
        var warmCard = 0;
        var pendingBadge = 0;
        var mainAction = 0;
        var sourceIcon = 0;
        for (var index = 0; index + 3 < pixels.Length; index += 4)
        {
            var blue = pixels[index];
            var green = pixels[index + 1];
            var red = pixels[index + 2];
            if (Matches(red, green, blue, 0xFE, 0xFE, 0xFE))
            {
                pageWhite++;
            }
            else if (Matches(red, green, blue, 0xFE, 0xF9, 0xE8))
            {
                warmCard++;
            }
            else if (Matches(red, green, blue, 0xFD, 0xED, 0xD8))
            {
                pendingBadge++;
            }
            else if (Matches(red, green, blue, 0xC2, 0x3E, 0x68))
            {
                mainAction++;
            }
            else if (Matches(red, green, blue, 0xFE, 0xD6, 0x7F))
            {
                sourceIcon++;
            }
        }

        return (pageWhite, warmCard, pendingBadge, mainAction, sourceIcon);

        static bool Matches(
            int red,
            int green,
            int blue,
            int expectedRed,
            int expectedGreen,
            int expectedBlue) =>
            Math.Abs(red - expectedRed) <= 4 &&
            Math.Abs(green - expectedGreen) <= 4 &&
            Math.Abs(blue - expectedBlue) <= 4;
    }

    private static bool FiltersEqual(
        DesktopWorkspaceFilter left,
        DesktopWorkspaceFilter right) =>
        left.SelectedTagIds.SequenceEqual(right.SelectedTagIds, StringComparer.OrdinalIgnoreCase) &&
        left.MatchMode == right.MatchMode &&
        left.UntaggedOnly == right.UntaggedOnly &&
        left.CountdownStatus == right.CountdownStatus &&
        left.NoteStatus == right.NoteStatus &&
        string.Equals(left.NoteSearch, right.NoteSearch, StringComparison.Ordinal) &&
        string.Equals(left.NoteDate, right.NoteDate, StringComparison.Ordinal) &&
        left.UndatedNotesOnly == right.UndatedNotesOnly;

    private static ReminderCenterSourceLifecycleExerciseResult ValidateReminderCenterSourceLifecycle()
    {
        var firstDueUtc = new DateTimeOffset(2031, 6, 1, 1, 0, 0, TimeSpan.Zero);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-center-source-{Guid.NewGuid():N}.settings.json");
        var blockedTemporaryPath = settingsPath + ".tmp";
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new MutableValidationClock(firstDueUtc.AddSeconds(-1));
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: clock));

            bool TryCreateDueNote(string text, DateTimeOffset dueUtc, out NoteRecord note)
            {
                if (!workspace.Apply(new SaveNoteChange(
                        null,
                        text,
                        new DateTime(2031, 6, 1),
                        false,
                        Array.Empty<string>(),
                        ReminderSchedule.CreateOnce(dueUtc))).Succeeded)
                {
                    note = null!;
                    return false;
                }

                note = workspace.Snapshot.Settings.Notes.Single(candidate =>
                    string.Equals(candidate.Text, text, StringComparison.Ordinal));
                return true;
            }

            coordinator = new ReminderDeliveryCoordinator(workspace, null);
            if (!TryCreateDueNote("删除来源生命周期探针", firstDueUtc, out var deletedNote))
            {
                return ReminderCenterSourceLifecycleExerciseResult.Failed;
            }

            clock.UtcNow = firstDueUtc;
            coordinator.Tick(clock.UtcNow);
            PumpDispatcher();
            var deletedRule = deletedNote.ReminderRules.Single();
            var deletedKey = new ActiveReminderOccurrenceKey(deletedRule.Id, firstDueUtc);
            var deletedWindow = coordinator.ActiveWindow;
            Directory.CreateDirectory(blockedTemporaryPath);
            var acknowledgementFailed = deletedWindow?.RequestAcknowledgement(deletedKey) == true;
            var persistenceFailureRetainsCurrentSource =
                acknowledgementFailed &&
                coordinator.ActiveEntryCount == 1 &&
                coordinator.HasPendingPersistenceRetry &&
                deletedWindow?.IsVisible == true &&
                deletedWindow.IsFailureVisible(deletedKey);
            Directory.Delete(blockedTemporaryPath);
            var sourceDeleted = workspace.Apply(new DeleteNoteChange(deletedNote.Id)).Succeeded;
            clock.UtcNow = firstDueUtc.AddSeconds(1);
            coordinator.Tick(clock.UtcNow);
            PumpDispatcher();
            var removesDeletedSource =
                sourceDeleted &&
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0 &&
                deletedWindow?.IsVisible == false;
            var cancelsDeletedSourceAcknowledgementRetry =
                removesDeletedSource &&
                !coordinator.HasPendingPersistenceRetry;

            coordinator.Dispose();
            coordinator = new ReminderDeliveryCoordinator(workspace, null);
            var disabledDueUtc = firstDueUtc.AddMinutes(1);
            if (!TryCreateDueNote("停用来源生命周期探针", disabledDueUtc, out var disabledNote))
            {
                return ReminderCenterSourceLifecycleExerciseResult.Failed;
            }

            clock.UtcNow = disabledDueUtc;
            coordinator.Tick(clock.UtcNow);
            PumpDispatcher();
            var disabledWindow = coordinator.ActiveWindow;
            var sourceDisabled = workspace.Apply(new SaveNoteChange(
                disabledNote.Id,
                disabledNote.Text,
                new DateTime(2031, 6, 1),
                false,
                disabledNote.TagIds,
                NoReminder.Instance)).Succeeded;
            clock.UtcNow = disabledDueUtc.AddSeconds(1);
            coordinator.Tick(clock.UtcNow);
            PumpDispatcher();
            var removesDisabledSource =
                sourceDisabled &&
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0 &&
                disabledWindow?.IsVisible == false;

            coordinator.Dispose();
            coordinator = new ReminderDeliveryCoordinator(workspace, null);
            var completedDueUtc = firstDueUtc.AddMinutes(2);
            if (!TryCreateDueNote("完成来源生命周期探针", completedDueUtc, out var completedNote))
            {
                return ReminderCenterSourceLifecycleExerciseResult.Failed;
            }

            clock.UtcNow = completedDueUtc;
            coordinator.Tick(clock.UtcNow);
            PumpDispatcher();
            var completedWindow = coordinator.ActiveWindow;
            var sourceCompleted = workspace.Apply(new SetNoteCompletionChange(completedNote.Id, true)).Succeeded;
            clock.UtcNow = completedDueUtc.AddSeconds(1);
            coordinator.Tick(clock.UtcNow);
            PumpDispatcher();
            var removesCompletedSource =
                sourceCompleted &&
                coordinator.ActiveEntryCount == 0 &&
                coordinator.ActiveWindowCount == 0 &&
                completedWindow?.IsVisible == false;

            var reopenedDueUtc = firstDueUtc.AddMinutes(3);
            if (!TryCreateDueNote("来源失效后重开探针", reopenedDueUtc, out _))
            {
                return ReminderCenterSourceLifecycleExerciseResult.Failed;
            }

            clock.UtcNow = reopenedDueUtc;
            coordinator.Tick(clock.UtcNow);
            PumpDispatcher();
            var reopensAfterSourceRemoval =
                removesCompletedSource &&
                coordinator.ActiveEntryCount == 1 &&
                coordinator.ActiveWindowCount == 1 &&
                coordinator.ActiveWindow?.IsVisible == true;

            return new ReminderCenterSourceLifecycleExerciseResult(
                persistenceFailureRetainsCurrentSource,
                removesDeletedSource,
                cancelsDeletedSourceAcknowledgementRetry,
                removesDisabledSource,
                removesCompletedSource,
                reopensAfterSourceRemoval);
        }
        finally
        {
            coordinator?.Dispose();
            if (Directory.Exists(blockedTemporaryPath))
            {
                Directory.Delete(blockedTemporaryPath);
            }
            else
            {
                File.Delete(blockedTemporaryPath);
            }
            TryDeleteSettingsFiles(settingsPath);
        }
    }

    private static ReminderDeliveryExerciseResult ValidateReminderDeliveryCoordinator()
    {
        var firstDueUtc = new DateTimeOffset(2031, 4, 7, 1, 30, 0, TimeSpan.Zero);
        var repeatingDueUtc = firstDueUtc.AddMinutes(-30);
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"MinminCountdown-delivery-{Guid.NewGuid():N}.settings.json");
        var blockedTemporaryPath = settingsPath + ".tmp";
        var diagnosticPath = settingsPath + ".diagnostic.jsonl";
        const string reminderText = "真实渲染与持久化重试探针";
        const string repeatingReminderText = "独立重复提醒探针";
        ReminderDeliveryCoordinator? coordinator = null;
        try
        {
            var clock = new MutableValidationClock(repeatingDueUtc.AddSeconds(-1));
            var workspace = DesktopWorkspace.Load(
                settingsPath,
                new SettingsStore(clock: clock));
            if (!workspace.Apply(new SaveNoteChange(
                    null,
                    reminderText,
                    new DateTime(2031, 4, 7),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateOnce(firstDueUtc))).Succeeded)
            {
                return ReminderDeliveryExerciseResult.Failed;
            }

            var onceId = workspace.Snapshot.Settings.Notes.Single().Id;
            if (!workspace.Apply(new SaveNoteChange(
                    null,
                    repeatingReminderText,
                    new DateTime(2031, 4, 7),
                    false,
                    Array.Empty<string>(),
                    ReminderSchedule.CreateRepeating(repeatingDueUtc, 60))).Succeeded)
            {
                return ReminderDeliveryExerciseResult.Failed;
            }

            var repeatingId = workspace.Snapshot.Settings.Notes.Single(note =>
                string.Equals(note.Text, repeatingReminderText, StringComparison.Ordinal)).Id;
            coordinator = new ReminderDeliveryCoordinator(
                workspace,
                new DiagnosticLogger(diagnosticPath));
            clock.UtcNow = repeatingDueUtc;
            coordinator.Tick(repeatingDueUtc);
            var repeatingWindow = Application.Current.Windows
                .OfType<ReminderCenterWindow>()
                .FirstOrDefault(window => Descendants<TextBlock>(window).Any(text =>
                    string.Equals(text.Text, repeatingReminderText, StringComparison.Ordinal)));
            PumpDispatcher();
            var repeatingNote = workspace.Snapshot.Settings.Notes.Single(note => note.Id == repeatingId);
            var repeatingRule = repeatingNote.ReminderRules.Single();
            var repeatingStatePersisted =
                workspace.Snapshot.Settings.ReminderStates.TryGetValue(repeatingRule.Id, out var repeatingState) &&
                repeatingState.HandledThroughUtc == repeatingDueUtc &&
                repeatingState.LastDeliveredOccurrenceKey ==
                    MinminCountdown.Core.ReminderOccurrenceKey.Create(
                        repeatingRule,
                        repeatingDueUtc).ToString();
            var repeatingRemainsUnresolved = workspace
                .GetDueReminderOccurrences(repeatingDueUtc)
                .SingleOrDefault() is { } repeatingOccurrence &&
                repeatingOccurrence.Note.Id == repeatingId &&
                repeatingOccurrence.Rule.Id == repeatingRule.Id &&
                repeatingOccurrence.ScheduledUtc == repeatingDueUtc;

            var repeatingKey = new ActiveReminderOccurrenceKey(repeatingRule.Id, repeatingDueUtc);
            var repeatingAcknowledged = repeatingWindow?.RequestAcknowledgement(repeatingKey) == true;
            PumpDispatcher();
            var beforeFailure = SettingsContract.SerializeCanonical(workspace.Snapshot.Settings);
            Directory.CreateDirectory(blockedTemporaryPath);
            clock.UtcNow = firstDueUtc;
            coordinator.Tick(firstDueUtc);
            var onceWindow = coordinator.ActiveWindow;
            var onceRule = workspace.Snapshot.Settings.Notes
                .Single(note => note.Id == onceId)
                .ReminderRules.Single();
            var onceKey = new ActiveReminderOccurrenceKey(onceRule.Id, firstDueUtc);
            var onceEntryRendered = false;
            if (onceWindow is not null)
            {
                onceWindow.EntryRendered += (_, args) =>
                    onceEntryRendered |= args.Key == onceKey;
            }
            var pendingBeforeRender = ReminderRuleCompatibility.ToLegacy(
                workspace.Snapshot.Settings,
                workspace.Snapshot.Settings.Notes.Single(note => note.Id == onceId)) is OnceReminder
            {
                Delivery: OnceDeliveryState.Pending
            };
            PumpDispatcher();
            var independentOccurrencesWork =
                repeatingStatePersisted &&
                repeatingRemainsUnresolved &&
                repeatingAcknowledged &&
                coordinator.ActiveWindowCount == 1 &&
                coordinator.ActiveEntryCount == 1 &&
                onceWindow?.IsVisible == true &&
                !ReferenceEquals(onceWindow, repeatingWindow) &&
                onceEntryRendered &&
                !workspace.GetDueReminders(firstDueUtc)
                    .Any(note => note.Id == repeatingId);
            var dismiss = onceWindow is null
                ? null
                : Descendants<Button>(onceWindow).Single(button =>
                    AutomationProperties.GetName(button).StartsWith("知道了：", StringComparison.Ordinal) &&
                    AutomationProperties.GetName(button).Contains(reminderText, StringComparison.Ordinal));
            var openSource = onceWindow is null
                ? null
                : Descendants<Button>(onceWindow).Single(button =>
                    AutomationProperties.GetName(button).StartsWith("打开便签来源：", StringComparison.Ordinal) &&
                    AutomationProperties.GetName(button).Contains(reminderText, StringComparison.Ordinal));
            var failureStatusVisible =
                onceWindow?.PersistenceFailureVisible == true &&
                onceWindow.IsVisible &&
                dismiss?.IsEnabled == false &&
                openSource?.IsEnabled == true;
            var failureKeyHandled = dismiss is not null &&
                                    !WindowValidationInput.RaisePreviewKey(dismiss, Key.Escape);
            PumpDispatcher();
            var failureWindowStayedVisible = onceWindow?.IsVisible == true;
            var firstRetryUtc = coordinator.NextPersistenceRetryUtc;
            var firstFailureCount = coordinator.PersistenceFailureCount;
            onceWindow?.Close();
            PumpDispatcher();
            var persistenceFailureCloseGuardWorks =
                onceWindow?.IsVisible == true &&
                coordinator.ActiveWindowCount == 1 &&
                coordinator.ActiveEntryCount == 1 &&
                coordinator.PersistenceFailureCount == firstFailureCount &&
                coordinator.NextPersistenceRetryUtc == firstRetryUtc;
            var firstFailureSingleFlight =
                coordinator.ActiveWindowCount == 1 &&
                coordinator.ActiveEntryCount == 1 &&
                coordinator.PersistenceFailureCount == 1 &&
                coordinator.HasPendingPersistenceRetry &&
                firstRetryUtc == firstDueUtc.AddSeconds(5) &&
                string.Equals(beforeFailure, SettingsContract.SerializeCanonical(workspace.Snapshot.Settings), StringComparison.Ordinal);

            coordinator.Tick(firstDueUtc.AddSeconds(4));
            var noEarlyRetry = coordinator.PersistenceFailureCount == 1 &&
                               coordinator.ActiveWindowCount == 1;
            coordinator.Tick(firstDueUtc.AddSeconds(5));
            var secondRetryUtc = coordinator.NextPersistenceRetryUtc;
            coordinator.Tick(firstDueUtc.AddSeconds(15));
            var thirdRetryUtc = coordinator.NextPersistenceRetryUtc;
            coordinator.Tick(firstDueUtc.AddSeconds(35));
            var fourthRetryUtc = coordinator.NextPersistenceRetryUtc;
            coordinator.Tick(firstDueUtc.AddSeconds(65));
            var fifthRetryUtc = coordinator.NextPersistenceRetryUtc;
            var diagnostics = File.ReadAllText(diagnosticPath);
            var diagnosticsExcludePrivatePayload =
                diagnostics.Contains(
                    "OneTimeReminderDeliveryPersistFailed",
                    StringComparison.Ordinal) &&
                !diagnostics.Contains(reminderText, StringComparison.Ordinal) &&
                !diagnostics.Contains(beforeFailure, StringComparison.Ordinal) &&
                !diagnostics.Contains("\"notes\"", StringComparison.Ordinal);
            var backoffWorks =
                secondRetryUtc == firstDueUtc.AddSeconds(15) &&
                thirdRetryUtc == firstDueUtc.AddSeconds(35) &&
                fourthRetryUtc == firstDueUtc.AddSeconds(65) &&
                fifthRetryUtc == firstDueUtc.AddSeconds(95) &&
                coordinator.PersistenceFailureCount == 5 &&
                coordinator.ActiveWindowCount == 1;

            Directory.Delete(blockedTemporaryPath);
            coordinator.Tick(firstDueUtc.AddSeconds(95));
            var delivered = ReminderRuleCompatibility.ToLegacy(
                workspace.Snapshot.Settings,
                workspace.Snapshot.Settings.Notes.Single(note => note.Id == onceId)) is OnceReminder
            {
                Delivery: OnceDeliveryState.Delivered
            };
            var successSingleFlight =
                delivered &&
                coordinator.ActiveOccurrenceConfirmed &&
                !coordinator.HasPendingPersistenceRetry &&
                coordinator.ActiveWindowCount == 1 &&
                onceWindow?.PersistenceFailureVisible == false &&
                dismiss?.IsEnabled == true;
            coordinator.Tick(firstDueUtc.AddSeconds(96));
            successSingleFlight &= coordinator.ActiveWindowCount == 1;
            var acknowledged = onceWindow?.RequestAcknowledgement(onceKey) == true;
            WindowValidationInput.Pump(Dispatcher.CurrentDispatcher);
            persistenceFailureCloseGuardWorks &=
                acknowledged &&
                onceWindow?.IsVisible == false &&
                coordinator.ActiveWindowCount == 0;

            return new ReminderDeliveryExerciseResult(
                pendingBeforeRender && onceEntryRendered,
                failureStatusVisible && failureKeyHandled && failureWindowStayedVisible,
                firstFailureSingleFlight && noEarlyRetry && backoffWorks,
                successSingleFlight,
                independentOccurrencesWork,
                persistenceFailureCloseGuardWorks,
                diagnosticsExcludePrivatePayload);
        }
        finally
        {
            coordinator?.Dispose();
            TryDeleteSettingsFiles(settingsPath);
            File.Delete(blockedTemporaryPath);
            File.Delete(diagnosticPath);
            if (Directory.Exists(blockedTemporaryPath))
            {
                Directory.Delete(blockedTemporaryPath);
            }
        }
    }
}
