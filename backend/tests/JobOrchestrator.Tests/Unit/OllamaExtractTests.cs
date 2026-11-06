using System.Diagnostics;
using JobOrchestrator.Intelligence.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Ollama packages come out whole: nested folders, symbolic links and every file after them.</summary>
public sealed class OllamaExtractTests
{
    [Fact]
    public async Task A_tar_package_extracts_every_file()
    {
        var root = Directory.CreateTempSubdirectory("jo-ollama-test").FullName;
        try
        {
            var src = Path.Combine(root, "src");
            Directory.CreateDirectory(Path.Combine(src, "mlx_metal_v3"));
            await File.WriteAllTextAsync(Path.Combine(src, "ollama"), "binary");
            await File.WriteAllTextAsync(Path.Combine(src, "libggml.0.dylib"), "lib");
            File.CreateSymbolicLink(Path.Combine(src, "libggml.dylib"), "libggml.0.dylib");
            await File.WriteAllTextAsync(Path.Combine(src, "mlx_metal_v3", "mlx.metallib"), "metal");
            var archive = Path.Combine(root, "ollama-test.tgz");
            using (var tar = Process.Start(new ProcessStartInfo("tar", ["-C", src, "-czf", archive, "."]) { RedirectStandardError = true })!) await tar.WaitForExitAsync();

            var dest = Path.Combine(root, "out");
            await LocalSetup.ExtractAsync(archive, dest);

            Assert.Equal("metal", await File.ReadAllTextAsync(Path.Combine(dest, "mlx_metal_v3", "mlx.metallib")));
            Assert.Equal("libggml.0.dylib", new FileInfo(Path.Combine(dest, "libggml.dylib")).LinkTarget);
            Assert.True(File.Exists(Path.Combine(dest, "ollama")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
