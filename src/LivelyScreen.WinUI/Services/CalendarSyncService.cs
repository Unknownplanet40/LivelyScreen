using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel.Appointments;
using LivelyScreen.Models;

namespace LivelyScreen.Services;

public record CalendarEventInfo(string Title, string TimeDescription, bool IsOngoing);

public static class CalendarSyncService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(6) };
    private static DateTime _lastFetchTime = DateTime.MinValue;
    private static string _cachedIcsData = "";
    private static string _lastUrlOrPath = "";
    private static CalendarEventInfo? _lastResult = null;
    private static DateTime _lastResultTime = DateTime.MinValue;

    public static async Task<CalendarEventInfo?> GetNextUpcomingEventAsync(bool useLiveEvents, string? icsUrlOrPath)
    {
        if (!useLiveEvents) return null;
        return await GetNextUpcomingEventAsync(new AppSettings { CalendarUseLiveEvents = useLiveEvents, CalendarIcsUrlOrPath = icsUrlOrPath ?? "" });
    }

    public static async Task<CalendarEventInfo?> GetNextUpcomingEventAsync(AppSettings settings)
    {
        if (!settings.CalendarUseLiveEvents)
            return null;

        // Cache result for 2 minutes to avoid re-evaluating on every UI tick
        if (_lastResult != null && DateTime.UtcNow - _lastResultTime < TimeSpan.FromMinutes(2))
        {
            return _lastResult;
        }

        try
        {
            // 1. Try custom iCal feed or local .ics file if configured
            if (!string.IsNullOrWhiteSpace(settings.CalendarIcsUrlOrPath))
            {
                var eventFromIcs = await GetNextEventFromIcsAsync(settings.CalendarIcsUrlOrPath.Trim());
                if (eventFromIcs != null)
                {
                    _lastResult = eventFromIcs;
                    _lastResultTime = DateTime.UtcNow;
                    return eventFromIcs;
                }
            }

            // 2. Try Windows native AppointmentManager store
            var winEvent = await GetNextWindowsCalendarEventAsync();
            if (winEvent != null)
            {
                _lastResult = winEvent;
                _lastResultTime = DateTime.UtcNow;
                return winEvent;
            }
        }
        catch (Exception ex)
        {
            LivelyScreen_WinUI.App.Log($"CalendarSyncService error: {ex.Message}");
        }

        return null;
    }

    private static async Task<CalendarEventInfo?> GetNextWindowsCalendarEventAsync()
    {
        try
        {
            var store = await AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly);
            if (store == null) return null;

            var now = DateTimeOffset.Now;
            var rangeEnd = now.AddDays(2);
            var options = new FindAppointmentsOptions { MaxCount = 10 };
            options.FetchProperties.Add(AppointmentProperties.Subject);
            options.FetchProperties.Add(AppointmentProperties.StartTime);
            options.FetchProperties.Add(AppointmentProperties.Duration);
            options.FetchProperties.Add(AppointmentProperties.AllDay);

            var appointments = await store.FindAppointmentsAsync(now.AddHours(-1), TimeSpan.FromHours(48), options);
            if (appointments == null || appointments.Count == 0) return null;

            var localNow = DateTime.Now;

            foreach (var appt in appointments.OrderBy(a => a.StartTime))
            {
                var start = appt.StartTime.LocalDateTime;
                var end = start.Add(appt.Duration);
                string title = string.IsNullOrWhiteSpace(appt.Subject) ? "Untitled Event" : appt.Subject;

                // Currently ongoing event
                if (localNow >= start && localNow < end)
                {
                    var rem = end - localNow;
                    string timeDesc = rem.TotalMinutes > 60
                        ? $"Ongoing • Ends in {Math.Round(rem.TotalHours)}h"
                        : $"Ongoing • Ends in {Math.Max(1, (int)rem.TotalMinutes)}m";
                    return new CalendarEventInfo(title, timeDesc, true);
                }

                // Upcoming event
                if (start > localNow)
                {
                    var until = start - localNow;
                    string timeDesc = until.TotalMinutes < 60
                        ? $"{start:t} • In {Math.Max(1, (int)until.TotalMinutes)}m"
                        : start.Date == localNow.Date
                            ? $"{start:t} • In {Math.Round(until.TotalHours, 1):0.#}h"
                            : $"{start:ddd h:mm tt}";
                    return new CalendarEventInfo(title, timeDesc, false);
                }
            }
        }
        catch
        {
            // Fallback gracefully on unpackaged WinUI or environments without calendar permissions
        }

        return null;
    }

    private static async Task<CalendarEventInfo?> GetNextEventFromIcsAsync(string source)
    {
        try
        {
            string icsContent = "";

            if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
            {
                string url = source.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)
                    ? "https://" + source[9..]
                    : source;

                if (!string.Equals(url, _lastUrlOrPath, StringComparison.OrdinalIgnoreCase) ||
                    DateTime.UtcNow - _lastFetchTime > TimeSpan.FromMinutes(10) ||
                    string.IsNullOrEmpty(_cachedIcsData))
                {
                    icsContent = await _httpClient.GetStringAsync(url);
                    _cachedIcsData = icsContent;
                    _lastUrlOrPath = url;
                    _lastFetchTime = DateTime.UtcNow;
                }
                else
                {
                    icsContent = _cachedIcsData;
                }
            }
            else if (File.Exists(source))
            {
                icsContent = await File.ReadAllTextAsync(source);
            }

            if (string.IsNullOrWhiteSpace(icsContent))
                return null;

            return ParseNextIcsEvent(icsContent);
        }
        catch (Exception ex)
        {
            LivelyScreen_WinUI.App.Log($"GetNextEventFromIcsAsync error: {ex.Message}");
            return null;
        }
    }

    private static CalendarEventInfo? ParseNextIcsEvent(string icsText)
    {
        var eventMatches = Regex.Matches(icsText, @"BEGIN:VEVENT([\s\S]*?)END:VEVENT", RegexOptions.IgnoreCase);
        if (eventMatches.Count == 0) return null;

        var now = DateTime.Now;
        var parsedEvents = new List<(string summary, DateTime start, DateTime end)>();

        foreach (Match m in eventMatches)
        {
            var body = m.Groups[1].Value;

            var sumMatch = Regex.Match(body, @"(?:^|\r?\n)SUMMARY.*?:([^\r\n]+)", RegexOptions.IgnoreCase);
            var dtStartMatch = Regex.Match(body, @"(?:^|\r?\n)DTSTART[^:]*:([^\r\n]+)", RegexOptions.IgnoreCase);
            var dtEndMatch = Regex.Match(body, @"(?:^|\r?\n)DTEND[^:]*:([^\r\n]+)", RegexOptions.IgnoreCase);

            if (!dtStartMatch.Success) continue;

            string summary = sumMatch.Success ? UnescapeIcs(sumMatch.Groups[1].Value.Trim()) : "Calendar Event";
            if (!TryParseIcsDateTime(dtStartMatch.Groups[1].Value.Trim(), out var start)) continue;

            DateTime end = start.AddHours(1);
            if (dtEndMatch.Success && TryParseIcsDateTime(dtEndMatch.Groups[1].Value.Trim(), out var parsedEnd))
            {
                end = parsedEnd;
            }

            // Exclude past events older than 1 hour
            if (end >= now.AddMinutes(-5))
            {
                parsedEvents.Add((summary, start, end));
            }
        }

        if (parsedEvents.Count == 0) return null;

        foreach (var ev in parsedEvents.OrderBy(e => e.start))
        {
            // Ongoing
            if (now >= ev.start && now < ev.end)
            {
                var rem = ev.end - now;
                string timeDesc = rem.TotalMinutes > 60
                    ? $"Ongoing • Ends in {Math.Round(rem.TotalHours)}h"
                    : $"Ongoing • Ends in {Math.Max(1, (int)rem.TotalMinutes)}m";
                return new CalendarEventInfo(ev.summary, timeDesc, true);
            }

            // Upcoming
            if (ev.start > now && (ev.start - now).TotalDays <= 2)
            {
                var until = ev.start - now;
                string timeDesc = until.TotalMinutes < 60
                    ? $"{ev.start:t} • In {Math.Max(1, (int)until.TotalMinutes)}m"
                    : ev.start.Date == now.Date
                        ? $"{ev.start:t} • In {Math.Round(until.TotalHours, 1):0.#}h"
                        : $"{ev.start:ddd h:mm tt}";
                return new CalendarEventInfo(ev.summary, timeDesc, false);
            }
        }

        return null;
    }

    private static bool TryParseIcsDateTime(string raw, out DateTime result)
    {
        result = DateTime.MinValue;
        raw = raw.Trim();

        // Format: 20261019T143000Z or 20261019T143000
        if (DateTime.TryParseExact(raw, new[] { "yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd" },
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeLocal,
            out var dt))
        {
            result = raw.EndsWith('Z') ? dt.ToLocalTime() : dt;
            return true;
        }

        return DateTime.TryParse(raw, out result);
    }

    private static string UnescapeIcs(string text)
    {
        return text.Replace(@"\,", ",")
                   .Replace(@"\;", ";")
                   .Replace(@"\n", " ")
                   .Replace(@"\N", " ")
                   .Replace(@"\\", @"\");
    }
}
