namespace JobOrchestrator.Applications.Domain;

/// <summary>Os dias do app: "enviadas hoje" vira junto com o seu dia, não às 21h de Brasília.</summary>
internal static class Day
{
    /// <summary>Meia-noite de hoje no fuso da campanha, em UTC.</summary>
    public static DateTime Start(DateTime utcNow, string? timeZone)
    {
        var zone = timeZone is { Length: > 0 } && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var found) ? found : TimeZoneInfo.Utc;
        var midnight = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone).Date;
        // Fuso que pula a meia-noite no horário de verão: o dia começa na primeira hora que existe.
        if (zone.IsInvalidTime(midnight)) midnight = midnight.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(midnight, zone);
    }
}
