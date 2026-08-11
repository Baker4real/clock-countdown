namespace MinminCountdown.Verification;

internal static class SettingsFixtures
{
    public const string VersionOne = """
        {
          "schemaVersion": 1,
          "timeZoneId": "China Standard Time",
          "targetUtc": "2030-01-02T03:04:05.0000000+00:00"
        }
        """;

    public const string VersionTwo = """
        {
          "schemaVersion": 2,
          "timeZoneId": "China Standard Time",
          "countdowns": [
            {
              "id": "countdown-v2",
              "name": "版本二倒计时",
              "targetUtc": "2030-02-03T04:05:06.0000000+00:00"
            }
          ]
        }
        """;

    public const string VersionThree = """
        {
          "schemaVersion": 3,
          "timeZoneId": "China Standard Time",
          "displayPrecision": "Minute",
          "monthlySalary": 12345.67,
          "workStart": "08:30",
          "workEnd": "17:45",
          "countdowns": [
            {
              "id": "countdown-v3",
              "name": "版本三倒计时",
              "targetUtc": "2030-03-04T05:06:07.0000000+00:00"
            }
          ]
        }
        """;

    public const string VersionFour = """
        {
          "schemaVersion": 4,
          "timeZoneId": "China Standard Time",
          "displayPrecision": "Hour",
          "monthlySalary": 8800,
          "workStart": "09:00",
          "workEnd": "18:00",
          "countdowns": [
            {
              "id": "countdown-v4",
              "name": "版本四倒计时",
              "targetUtc": "2030-04-05T06:07:08.0000000+00:00"
            }
          ],
          "notes": [
            {
              "id": "note-v4",
              "text": "版本四便签",
              "isCompleted": true
            }
          ]
        }
        """;

    public const string VersionFive = """
        {
          "schemaVersion": 5,
          "timeZoneId": "China Standard Time",
          "displayPrecision": "Second",
          "monthlySalary": 10000,
          "workStart": "09:15",
          "workEnd": "18:15",
          "tags": [
            {
              "id": "tag-work",
              "name": "工作",
              "color": "#FF8A70"
            }
          ],
          "countdowns": [
            {
              "id": "countdown-v5",
              "name": "版本五倒计时",
              "targetUtc": "2030-05-06T07:08:09.0000000+00:00",
              "tagIds": ["tag-work"]
            }
          ],
          "notes": [
            {
              "id": "note-v5",
              "text": "版本五便签",
              "isCompleted": false,
              "tagIds": ["tag-work"]
            }
          ]
        }
        """;

    public const string VersionSix = """
        {
          "schemaVersion": 6,
          "timeZoneId": "China Standard Time",
          "displayPrecision": "Day",
          "monthlySalary": 15000,
          "workStart": "08:00",
          "workEnd": "17:00",
          "tags": [
            {
              "id": "tag-life",
              "name": "生活"
            }
          ],
          "countdowns": [
            {
              "id": "countdown-v6",
              "name": "版本六倒计时",
              "targetUtc": "2030-06-07T08:09:10.0000000+00:00",
              "tagIds": []
            }
          ],
          "notes": [
            {
              "id": "note-v6",
              "text": "版本六便签",
              "date": "2030-06-07",
              "isCompleted": false,
              "tagIds": ["tag-life"]
            }
          ]
        }
        """;

    public const string VersionSeven = """
        {
          "schemaVersion": 7,
          "timeZoneId": "China Standard Time",
          "displayPrecision": "Second",
          "monthlySalary": 20000.50,
          "workStart": "09:00",
          "workEnd": "18:30",
          "tags": [
            {
              "id": "tag-focus",
              "name": "专注",
              "color": "#FFD36B"
            }
          ],
          "countdowns": [
            {
              "id": "countdown-v7",
              "name": "版本七倒计时",
              "targetUtc": "2030-07-08T09:10:11.0000000+00:00",
              "tagIds": ["tag-focus"]
            }
          ],
          "notes": [
            {
              "id": "note-v7",
              "text": "版本七便签",
              "date": "2030-07-08",
              "isCompleted": false,
              "tagIds": ["tag-focus"],
              "reminderIntervalMinutes": 30,
              "reminderStartUtc": "2030-07-08T01:00:00.0000000+00:00"
            }
          ]
        }
        """;

    public const string VersionEight = """
        {
          "schemaVersion": 8,
          "timeZoneId": "China Standard Time",
          "displayPrecision": "Second",
          "monthlySalary": 20000.50,
          "workStart": "09:00",
          "workEnd": "18:30",
          "tags": [
            {
              "id": "tag-focus",
              "name": "专注",
              "color": "#FFD36B"
            }
          ],
          "countdowns": [
            {
              "id": "countdown-v8",
              "name": "版本八倒计时",
              "targetUtc": "2030-08-09T10:11:12.0000000+00:00",
              "tagIds": ["tag-focus"]
            }
          ],
          "notes": [
            {
              "id": "note-v8",
              "text": "版本八便签",
              "date": "2030-08-09",
              "isCompleted": false,
              "tagIds": ["tag-focus"],
              "reminder": {
                "type": "none"
              }
            }
          ]
        }
        """;

    public const string VersionNine = """
        {
          "schemaVersion": 9,
          "timeZoneId": "China Standard Time",
          "displayPrecision": "Second",
          "monthlySalary": 20000.50,
          "workStart": "09:00",
          "workEnd": "18:30",
          "themePreference": "Night",
          "tags": [],
          "countdowns": [
            {
              "id": "countdown-v9",
              "name": "版本九倒计时",
              "targetUtc": "2031-09-10T11:12:13.0000000+00:00",
              "tagIds": []
            }
          ],
          "notes": [
            {
              "id": "note-v9-none",
              "text": "不提醒",
              "date": "2031-09-10",
              "isCompleted": false,
              "tagIds": [],
              "reminder": { "type": "none" }
            },
            {
              "id": "note-v9-pending",
              "text": "待送达一次提醒",
              "date": "2031-09-10",
              "isCompleted": false,
              "tagIds": [],
              "reminder": {
                "type": "once",
                "firstDueUtc": "2031-09-10T01:30:00.0000000+00:00",
                "delivery": "pending"
              }
            },
            {
              "id": "note-v9-delivered",
              "text": "已送达一次提醒",
              "date": "2031-09-10",
              "isCompleted": false,
              "tagIds": [],
              "reminder": {
                "type": "once",
                "firstDueUtc": "2031-09-10T02:30:00.0000000+00:00",
                "delivery": "delivered"
              }
            },
            {
              "id": "note-v9-repeat",
              "text": "已开始循环提醒",
              "date": "2031-09-10",
              "isCompleted": false,
              "tagIds": [],
              "reminder": {
                "type": "repeat",
                "firstDueUtc": "2031-09-10T01:00:00.0000000+00:00",
                "intervalMinutes": 30
              }
            }
          ]
        }
        """;

    public const string VersionTen = """
        {
          "schemaVersion": 10,
          "timeZoneId": "China Standard Time",
          "displayPrecision": "Minute",
          "monthlySalary": 18000,
          "workStart": "08:30",
          "workEnd": "17:30",
          "themePreference": "Day",
          "tags": [],
          "countdowns": [
            {
              "id": "countdown-v10",
              "name": "版本十倒计时",
              "targetUtc": "2032-10-11T12:13:14.0000000+00:00",
              "tagIds": [],
              "reminderRules": [
                {
                  "id": "rule-countdown-once",
                  "type": "absoluteOnce",
                  "scheduledUtc": "2032-10-11T10:00:00.0000000+00:00"
                },
                {
                  "id": "rule-countdown-repeat",
                  "type": "fixedInterval",
                  "firstDueUtc": "2032-10-10T10:00:00.0000000+00:00",
                  "intervalMinutes": 60
                }
              ]
            }
          ],
          "notes": [
            {
              "id": "note-v10",
              "text": "版本十便签",
              "date": "2032-10-11",
              "isCompleted": false,
              "tagIds": [],
              "reminderRules": [
                {
                  "id": "rule-note-once",
                  "type": "absoluteOnce",
                  "scheduledUtc": "2032-10-11T01:30:00.0000000+00:00"
                },
                {
                  "id": "rule-note-repeat",
                  "type": "fixedInterval",
                  "firstDueUtc": "2032-10-11T02:00:00.0000000+00:00",
                  "intervalMinutes": 30
                }
              ]
            }
          ],
          "reminderStates": {
            "rule-note-once": {
              "handledThroughUtc": "2032-10-11T01:30:00.0000000+00:00",
              "lastDeliveredOccurrenceKey": "rule-note-once|2032-10-11T01:30:00.0000000+00:00",
              "lastDispositionOccurrenceKey": "rule-note-once|2032-10-11T01:30:00.0000000+00:00",
              "activeSnooze": null
            },
            "rule-note-repeat": {
              "handledThroughUtc": "2032-10-11T03:00:00.0000000+00:00",
              "lastDeliveredOccurrenceKey": "rule-note-repeat|2032-10-11T03:00:00.0000000+00:00",
              "lastDispositionOccurrenceKey": null,
              "activeSnooze": null
            }
          }
        }
        """;

    public static IReadOnlyList<(int Version, string Json)> AllVersions { get; } =
    [
        (1, VersionOne),
        (2, VersionTwo),
        (3, VersionThree),
        (4, VersionFour),
        (5, VersionFive),
        (6, VersionSix),
        (7, VersionSeven),
        (8, VersionEight),
        (9, VersionNine)
    ];
}
