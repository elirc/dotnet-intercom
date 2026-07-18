using System.Text.Json;

namespace NetIntercom.Domain.Entities;

/// <summary>
/// Weekly office hours in a fixed IANA/Windows time zone.
/// Serialized into <see cref="Team.OfficeHoursJson"/>.
/// </summary>
public class OfficeHours
{
    public string TimeZoneId { get; set; } = "UTC";
    public List<OfficeHoursWindow> Windows { get; set; } = new();

    public bool IsOpenAt(DateTime utcInstant)
    {
        if (Windows.Count == 0)
        {
            return true;
        }

        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            zone = TimeZoneInfo.Utc;
        }

        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc), zone);
        var time = TimeOnly.FromDateTime(local);
        return Windows.Any(w => w.Day == local.DayOfWeek && time >= w.Start && time < w.End);
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static OfficeHours? FromJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<OfficeHours>(json);
}

public class OfficeHoursWindow
{
    public DayOfWeek Day { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
}
