using JobOrchestrator.BuildingBlocks;

namespace JobOrchestrator.Tests.Unit;

/// <summary>"Apagar tudo" takes the trail with it: nothing from before stays in memory or on disk, and the trail keeps working after.</summary>
public sealed class TrailClearTests
{
    [Fact]
    public async Task Clear_leaves_only_what_comes_after()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"jo-trail-{Guid.NewGuid():N}");
        try
        {
            var trail = new Trail(TimeProvider.System, folder);
            trail.Info("state", "Acme Pagamentos", "Shortlisted → MaterialsReady");
            await Task.Delay(300);
            Assert.NotEmpty(Directory.GetFiles(folder, "trail-*.jsonl"));

            trail.Clear();
            trail.Info("http", "GET /api/jobs");
            await trail.DisposeAsync();

            Assert.DoesNotContain(trail.Recent(), e => e.Name == "Acme Pagamentos");
            Assert.Contains(trail.Recent(), e => e.Name == "Rastro apagado");
            var text = string.Concat(Directory.GetFiles(folder, "trail-*.jsonl").Select(File.ReadAllText));
            Assert.DoesNotContain("Acme Pagamentos", text);
            Assert.Contains("Rastro apagado", text);
            Assert.Contains("GET /api/jobs", text);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
