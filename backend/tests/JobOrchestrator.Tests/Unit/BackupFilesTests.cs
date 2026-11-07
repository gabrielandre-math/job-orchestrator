using System.IO.Compression;
using JobOrchestrator.BuildingBlocks.Data;

namespace JobOrchestrator.Tests.Unit;

/// <summary>A backup brings back the files the database points to, and says what it does not carry.</summary>
public sealed class BackupFilesTests
{
    [Fact]
    public async Task Files_go_into_the_backup_and_come_back_on_restore()
    {
        var root = Directory.CreateTempSubdirectory("jo-backup-test").FullName;
        try
        {
            var data = Path.Combine(root, "data");
            var config = Path.Combine(root, "config");
            Directory.CreateDirectory(Path.Combine(data, "profile", "documents"));
            Directory.CreateDirectory(Path.Combine(data, "resumes", "acme"));
            Directory.CreateDirectory(config);
            await File.WriteAllTextAsync(Path.Combine(data, "profile", "documents", "cv.pdf"), "cv");
            await File.WriteAllTextAsync(Path.Combine(data, "resumes", "acme", "resume.pdf"), "pdf");
            await File.WriteAllTextAsync(Path.Combine(config, "settings.json"), "{\"a\":1}");
            var zipPath = Path.Combine(root, "backup.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create)) await PortablePostgres.AddAppFilesAsync(zip, data, config, CancellationToken.None);

            using (var zip = ZipFile.OpenRead(zipPath))
            {
                Assert.Contains(zip.Entries, e => e.FullName == "files/profile/documents/cv.pdf");
                Assert.Contains(zip.Entries, e => e.FullName == "files/settings.json");
                using var manifest = new StreamReader(zip.GetEntry("manifest.json")!.Open());
                Assert.Contains("chaveiro", await manifest.ReadToEndAsync());
            }

            await File.WriteAllTextAsync(Path.Combine(data, "resumes", "acme", "resume.pdf"), "mudou");
            File.Delete(Path.Combine(data, "profile", "documents", "cv.pdf"));
            PortablePostgres.RestoreAppFiles(zipPath, data, config, "teste");

            Assert.Equal("cv", await File.ReadAllTextAsync(Path.Combine(data, "profile", "documents", "cv.pdf")));
            Assert.Equal("pdf", await File.ReadAllTextAsync(Path.Combine(data, "resumes", "acme", "resume.pdf")));
            Assert.Equal("mudou", await File.ReadAllTextAsync(Path.Combine(data, "resumes.antes-teste", "acme", "resume.pdf")));
            Assert.Equal("{\"a\":1}", await File.ReadAllTextAsync(Path.Combine(config, "settings.json")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
