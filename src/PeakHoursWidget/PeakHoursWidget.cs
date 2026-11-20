using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Windows.Widgets.Providers;

namespace PeakHoursWidget;

/// <summary>
/// Shows the UTC peak-hours windows (Mon-Fri 01:00-04:00 and 06:00-10:00)
/// converted into the timezone configured in Windows.
/// </summary>
internal sealed class PeakHoursWidgetImpl : WidgetImplBase
{
    public const string DefinitionId = "PeakHoursWidget.Definition";

    // UTC windows, inclusive start / exclusive end.
    private static readonly (int StartHour, int EndHour)[] UtcWindows =
    {
        (1, 4),
        (6, 10),
    };

    private static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    private static readonly TimeZoneInfo ChinaZone = FindZone("China Standard Time", "Asia/Shanghai");
    private static readonly TimeZoneInfo NewYorkZone = FindZone("Eastern Standard Time", "America/New_York");

    private static string? _template;
    private static readonly object ScheduleLock = new();
    private static ScheduleSnapshot? _schedule;
    private static string? _scheduleZoneId;
    private static DateTime _scheduleDate;

    public PeakHoursWidgetImpl(string widgetId, string initialState)
        : base(widgetId, initialState)
    {
    }

    public override string GetTemplateForWidget()
    {
        return _template ??= ReadPackageFileFromUri("ms-appx:///Templates/PeakHoursTemplate.json");
    }

    public override void OnActionInvoked(WidgetActionInvokedArgs args)
    {
        if (string.Equals(args.Verb, "refresh", StringComparison.OrdinalIgnoreCase)
            || string.Equals(args.Verb, "china", StringComparison.OrdinalIgnoreCase)
            || string.Equals(args.Verb, "newyork", StringComparison.OrdinalIgnoreCase))
        {
            PushUpdate(includeTemplate: false);
        }
    }

    public override string GetDataForWidget()
    {
        var zone = TimeZoneInfo.Local;
        var nowLocal = DateTime.Now;
        var schedule = GetSchedule(zone, nowLocal);
        var (isPeak, peakEnd, nextStart) = GetStatus(nowLocal, zone);

        var nowUtc = DateTime.UtcNow;
        string chinaTime = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, ChinaZone).ToString("ddd HH:mm");
        string newYorkTime = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, NewYorkZone).ToString("ddd HH:mm");

        string status;
        string statusColor;
        string badge;
        string badgeColor;

        if (isPeak && peakEnd.HasValue)
        {
            status = $"Peak now — ends {peakEnd.Value:HH:mm}";
            statusColor = "default";
            badge = "PEAK";
            badgeColor = "attention"; // Adaptive Cards: red
        }
        else if (nextStart.HasValue)
        {
            status = $"Next peak {FormatDuration(nextStart.Value - nowLocal)} · {nextStart.Value:ddd HH:mm}";
            statusColor = "default";
            badge = "NOT PEAK";
            badgeColor = "good"; // Adaptive Cards: green
        }
        else
        {
            status = "No upcoming peak window";
            statusColor = "default";
            badge = "NOT PEAK";
            badgeColor = "good";
        }

        // Flat payload: build it directly instead of pulling in System.Text.Json.
        var json = new StringBuilder(384);
        json.Append('{');
        bool first = true;
        AppendField(json, ref first, "timezone", schedule.TimezoneLine);
        AppendField(json, ref first, "badge", badge);
        AppendField(json, ref first, "badgeColor", badgeColor);
        AppendField(json, ref first, "status", status);
        AppendField(json, ref first, "statusColor", statusColor);
        AppendField(json, ref first, "window0Days", schedule.Days0);
        AppendField(json, ref first, "window0Time", schedule.Time0);
        AppendField(json, ref first, "window1Days", schedule.Days1);
        AppendField(json, ref first, "window1Time", schedule.Time1);
        AppendField(json, ref first, "chinaTime", chinaTime);
        AppendField(json, ref first, "newYorkTime", newYorkTime);
        json.Append('}');
        return json.ToString();
    }

    private static void AppendField(StringBuilder json, ref bool first, string key, string value)
    {
        if (!first)
        {
            json.Append(',');
        }
        first = false;

        json.Append('"').Append(key).Append("\":");
        json.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': json.Append("\\\""); break;
                case '\\': json.Append("\\\\"); break;
                case '\n': json.Append("\\n"); break;
                case '\r': json.Append("\\r"); break;
                case '\t': json.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        json.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        json.Append(c);
                    }
                    break;
            }
        }
        json.Append('"');
    }

    private static List<LocalWindow> BuildLocalWindows(TimeZoneInfo zone)
    {
        // A reference UTC week lets us derive the local weekday set of each window,
        // including windows that shift to the previous/next local day.
        var mondayUtc = StartOfWeekUtc(DateTime.UtcNow);
        var grouped = new Dictionary<(TimeSpan Start, TimeSpan End), SortedSet<int>>();

        for (int utcDay = 0; utcDay < 5; utcDay++)
        {
            var baseUtc = mondayUtc.AddDays(utcDay);
            foreach (var (startHour, endHour) in UtcWindows)
            {
                var utcStart = new DateTime(baseUtc.Year, baseUtc.Month, baseUtc.Day, startHour, 0, 0, DateTimeKind.Utc);
                var utcEnd = new DateTime(baseUtc.Year, baseUtc.Month, baseUtc.Day, endHour, 0, 0, DateTimeKind.Utc);
                var localStart = TimeZoneInfo.ConvertTimeFromUtc(utcStart, zone);
                var localEnd = TimeZoneInfo.ConvertTimeFromUtc(utcEnd, zone);

                var key = (localStart.TimeOfDay, localEnd.TimeOfDay);
                if (!grouped.TryGetValue(key, out var days))
                {
                    days = new SortedSet<int>();
                    grouped[key] = days;
                }
                days.Add(MondayBasedDayOfWeek(localStart.DayOfWeek));
            }
        }

        return grouped
            .Select(entry => new LocalWindow(entry.Key.Start, entry.Key.End, FormatDays(entry.Value)))
            .OrderBy(window => window.Start)
            .ToList();
    }

    private static (bool IsPeak, DateTime? PeakEnd, DateTime? NextStart) GetStatus(
        DateTime nowLocal,
        TimeZoneInfo zone)
    {
        bool isPeak = false;
        DateTime? peakEnd = null;
        DateTime? nextStart = null;

        var utcToday = DateTime.UtcNow.Date;
        for (int dayOffset = -2; dayOffset <= 8; dayOffset++)
        {
            var baseUtc = utcToday.AddDays(dayOffset);
            if (MondayBasedDayOfWeek(baseUtc.DayOfWeek) >= 5)
            {
                continue;
            }

            foreach (var (startHour, endHour) in UtcWindows)
            {
                var utcStart = new DateTime(baseUtc.Year, baseUtc.Month, baseUtc.Day, startHour, 0, 0, DateTimeKind.Utc);
                var utcEnd = new DateTime(baseUtc.Year, baseUtc.Month, baseUtc.Day, endHour, 0, 0, DateTimeKind.Utc);
                var localStart = TimeZoneInfo.ConvertTimeFromUtc(utcStart, zone);
                var localEnd = TimeZoneInfo.ConvertTimeFromUtc(utcEnd, zone);

                if (nowLocal >= localStart && nowLocal < localEnd)
                {
                    isPeak = true;
                    if (!peakEnd.HasValue || localEnd < peakEnd.Value)
                    {
                        peakEnd = localEnd;
                    }
                }

                if (localStart > nowLocal && (!nextStart.HasValue || localStart < nextStart.Value))
                {
                    nextStart = localStart;
                }
            }
        }

        return (isPeak, peakEnd, nextStart);
    }

    private static DateTime StartOfWeekUtc(DateTime utcNow)
    {
        var date = utcNow.Date;
        return date.AddDays(-MondayBasedDayOfWeek(date.DayOfWeek));
    }

    private static int MondayBasedDayOfWeek(DayOfWeek day) => ((int)day + 6) % 7;

    private static string FormatDays(SortedSet<int> days)
    {
        if (days.Count == 7)
        {
            return "Daily";
        }

        if (days.Count == 5 && days.SetEquals(new[] { 0, 1, 2, 3, 4 }))
        {
            return "Weekdays";
        }

        var present = new bool[7];
        foreach (var day in days)
        {
            present[day] = true;
        }

        // Find maximal runs on the circular week (so Sun + Mon-Thu becomes "Sun-Thu").
        var parts = new List<string>();
        for (int start = 0; start < 7; start++)
        {
            if (!present[start] || present[(start + 6) % 7])
            {
                continue;
            }

            int length = 0;
            int cursor = start;
            while (present[cursor] && length < 7)
            {
                length++;
                cursor = (cursor + 1) % 7;
            }

            int end = (start + length - 1) % 7;
            parts.Add(length == 1 ? DayNames[start] : $"{DayNames[start]}–{DayNames[end]}");
        }

        return string.Join(", ", parts);
    }

    private static string FormatOffset(TimeSpan offset)
    {
        string sign = offset < TimeSpan.Zero ? "-" : "+";
        return $"{sign}{Math.Abs(offset.Hours):00}:{Math.Abs(offset.Minutes):00}";
    }

    private static string FormatDuration(TimeSpan delta)
    {
        int totalMinutes = Math.Max(0, (int)Math.Ceiling(delta.TotalMinutes));
        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;

        if (hours == 0)
        {
            return $"in {minutes}m";
        }
        return minutes == 0 ? $"in {hours}h" : $"in {hours}h {minutes}m";
    }

    private static TimeZoneInfo FindZone(string windowsId, string ianaId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
        }
        catch
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
            }
            catch
            {
                return TimeZoneInfo.Utc;
            }
        }
    }

    private static ScheduleSnapshot GetSchedule(TimeZoneInfo zone, DateTime nowLocal)
    {
        lock (ScheduleLock)
        {
            if (_schedule != null && _scheduleDate == nowLocal.Date && _scheduleZoneId == zone.Id)
            {
                return _schedule;
            }

            var windows = BuildLocalWindows(zone);
            string zoneName = string.IsNullOrWhiteSpace(zone.StandardName) ? zone.Id : zone.StandardName;
            string timezoneLine = $"{zoneName} · UTC{FormatOffset(zone.GetUtcOffset(nowLocal))}";

            _schedule = new ScheduleSnapshot(
                timezoneLine,
                windows.Count > 0 ? windows[0].Days : string.Empty,
                windows.Count > 0 ? FormatWindowTime(windows[0]) : string.Empty,
                windows.Count > 1 ? windows[1].Days : string.Empty,
                windows.Count > 1 ? FormatWindowTime(windows[1]) : string.Empty);
            _scheduleDate = nowLocal.Date;
            _scheduleZoneId = zone.Id;
            return _schedule;
        }
    }

    private static string FormatWindowTime(LocalWindow window)
    {
        return $"{window.Start:hh\\:mm}–{window.End:hh\\:mm}";
    }

    private sealed record LocalWindow(TimeSpan Start, TimeSpan End, string Days);

    private sealed record ScheduleSnapshot(
        string TimezoneLine,
        string Days0,
        string Time0,
        string Days1,
        string Time1);
}
