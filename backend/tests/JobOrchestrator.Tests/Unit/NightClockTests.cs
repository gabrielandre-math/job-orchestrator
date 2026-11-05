using JobOrchestrator.Operations.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>As rodadas da noite no próprio app: o horário cai uma vez, inclusive na virada da meia-noite, e a próxima data é a certa.</summary>
public sealed class NightClockTests
{
    [Fact]
    public void Fires_once_inside_the_window_and_across_midnight()
    {
        var at = new TimeOnly(1, 35);
        Assert.True(NightClock.Due(at, new DateTime(2026, 11, 6, 1, 34, 50), new DateTime(2026, 11, 6, 1, 35, 20)));
        Assert.False(NightClock.Due(at, new DateTime(2026, 11, 6, 1, 35, 20), new DateTime(2026, 11, 6, 1, 35, 50)));
        Assert.True(NightClock.Due(new TimeOnly(23, 5), new DateTime(2026, 11, 5, 23, 4, 50), new DateTime(2026, 11, 5, 23, 5, 10)));
        // Máquina que dormiu das 22h às 8h: os horários da madrugada caem na volta.
        Assert.True(NightClock.Due(new TimeOnly(4, 5), new DateTime(2026, 11, 5, 22, 0, 0), new DateTime(2026, 11, 6, 8, 0, 0)));
        Assert.True(NightClock.Due(new TimeOnly(23, 5), new DateTime(2026, 11, 5, 22, 0, 0), new DateTime(2026, 11, 6, 8, 0, 0)));
    }

    [Fact]
    public void Next_is_the_closest_future_time()
    {
        var now = new DateTime(2026, 11, 5, 23, 30, 0, DateTimeKind.Local);
        Assert.Equal(new DateTime(2026, 11, 6, 1, 35, 0, DateTimeKind.Local).ToUniversalTime(), NightClock.Next("job-orchestrator-night", now));
        Assert.Equal(new DateTime(2026, 11, 6, 7, 55, 0, DateTimeKind.Local).ToUniversalTime(), NightClock.Next("job-orchestrator-backup", now));
    }
}
