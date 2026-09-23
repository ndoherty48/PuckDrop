using Ical.Net;
using Ical.Net.CalendarComponents;
using PuckDrop.Application.Models;
using PuckDrop.Application.Services;
using PuckDrop.Application.Services.Abstractions;

namespace PuckDrop.Infrastructure.Fixtures;

/// <summary>
/// Fetches a public per-team ICS feed (e.g. the fixture calendars published at
/// eihl-calendars.nathandoherty.dev) and parses it into fixture candidates. Admin-only feature,
/// but the URL is still admin-supplied input, so this stays defensive: https-only, a request
/// timeout on the HttpClient, and a content-length cap before reading the body.
/// </summary>
public class IcsFixtureFeedFetcher(HttpClient httpClient) : IIcsFixtureFeedFetcher
{
    private const long MaxContentLengthBytes = 5_000_000;

    public async Task<IReadOnlyList<FixtureCandidate>> FetchAsync(
        string icsUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(icsUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("The calendar URL must be an absolute https:// URL.", nameof(icsUrl));

        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is { } contentLength && contentLength > MaxContentLengthBytes)
            throw new ArgumentException("The calendar file is too large to import.", nameof(icsUrl));

        var icsText = await response.Content.ReadAsStringAsync(cancellationToken);
        var calendar = Calendar.Load(icsText)
            ?? throw new ArgumentException("The URL didn't return a valid calendar.", nameof(icsUrl));

        // DTSTART is technically optional on a calendar component (e.g. VTODO); every real
        // fixture has one, so an event without it isn't a fixture we can import.
        return calendar.Events
            .Where(e => e.Start is not null)
            .Select(ToFixtureCandidate)
            .ToList();
    }

    // Start is asserted non-null by FetchAsync's filter before this is called.
    private static FixtureCandidate ToFixtureCandidate(CalendarEvent calendarEvent)
    {
        var parsed = FixtureDescriptionParser.Parse(calendarEvent.Description);

        var title = parsed is not null
            ? $"{parsed.Home} vs {parsed.Away}"
            : calendarEvent.Summary ?? "Fixture";
        var category = parsed?.Competition
            ?? calendarEvent.Categories?.FirstOrDefault()
            ?? "Uncategorized";

        var start = calendarEvent.Start!;
        return new FixtureCandidate(title, category, DateOnly.FromDateTime(start.Value), start.AsUtc);
    }
}
