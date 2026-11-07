using JobOrchestrator.Intelligence.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>The recommended models are ones Ollama will load on that machine.</summary>
public sealed class LocalRecommendTests
{
    private static Hardware Mac(int ramGb) =>
        new([new GpuInfo("apple", "Apple M3 (GPU integrada)", ramGb * 1024 * 2 / 3, 0, ramGb * 1024 * 2 / 3)], "Apple M3", 8, false, false, ramGb * 1024, 1600, 46, true, "0.40.15", true);

    [Fact]
    public void An_8_gb_mac_gets_models_that_fit_in_metal()
    {
        var r = LocalSetup.Recommend(Mac(8), LocalSetup.CatalogFor(apple: true), 0);

        Assert.Equal("qwen3:4b", r.Fast);
        Assert.Equal("qwen3.5:4b", r.Strong);
        // Ollama refused the 8.8 GB MLX build of gemma4:e4b there: "model requires 8.8 GiB but only 4.8 GiB are available".
        var limit = 8 * 1024 * 2 / 3 / 1024.0 - 0.5;
        Assert.All(new[] { r.Fast, r.Strong }, name => Assert.True(LocalSetup.CatalogFor(apple: true).Single(m => m.Name == name).VramGb <= limit));
    }

    [Fact]
    public void A_bigger_mac_gets_the_stronger_models()
    {
        var r = LocalSetup.Recommend(Mac(24), LocalSetup.CatalogFor(apple: true), 0);

        Assert.Equal("gemma4:e4b", r.Fast);
        Assert.Equal("qwen3.5:9b", r.Strong);
    }

    [Fact]
    public void On_apple_silicon_the_catalog_shows_the_mlx_download()
    {
        Assert.Equal(8.8, LocalSetup.CatalogFor(apple: true).Single(m => m.Name == "gemma4:e4b").DiskGb);
        Assert.Equal(6.6, LocalSetup.CatalogFor(apple: false).Single(m => m.Name == "gemma4:e4b").DiskGb);
    }

    [Fact]
    public void The_strong_model_never_ranks_below_the_fast_one()
    {
        var gpu = new GpuInfo("nvidia", "RTX", 8192, 1200, 6992);
        var hw = new Hardware([gpu], "x64", 16, true, false, 32768, 20000, 300, true, "0.40.14", true);
        var models = LocalSetup.CatalogFor(apple: false);

        var r = LocalSetup.Recommend(hw, models, 0);

        int Rank(string name) => models.Single(m => m.Name == name).Score ?? 0;
        Assert.True(Rank(r.Strong) >= Rank(r.Fast));
    }
}
