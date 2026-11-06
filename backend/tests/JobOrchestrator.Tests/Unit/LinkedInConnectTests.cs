using System.IO.Compression;
using System.Text;
using JobOrchestrator.BuildingBlocks;
using JobOrchestrator.Profile.Contracts;
using JobOrchestrator.Profile.Domain;
using JobOrchestrator.Profile.Import;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Conectar o LinkedIn: o export se soma ao currículo sem repetir, nada some quando um dos dois é trocado, e do ZIP só os CSVs do perfil ficam.</summary>
public sealed class LinkedInConnectTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Identity Nobody = new("", null, null, null, null, null, null);

    private static VerifiedFact Fact(CandidateProfile p, SourceDocument d, string kind, string statement, Guid? parent = null, string? org = null, string? role = null, params string[] tech) =>
        VerifiedFact.Create(p.Id, d.Id, kind, statement, FactStatus.Verified, 0, Now, parent, org, role, technologies: tech, quote: statement);

    [Fact]
    public void Excluir_documento_tira_so_o_que_vinha_so_dele()
    {
        var p = CandidateProfile.Create(Now);
        var cv = p.AddDocument("cv.pdf", "pdf", "a1", "/tmp/cv.pdf", "cv", 1, Now);
        var job = Fact(p, cv, "experience", "Software Engineer, BOATLUX", org: "BOATLUX", role: "Software Engineer");
        p.ReplaceFacts(cv, [job, Fact(p, cv, "skill", "C#"), Fact(p, cv, "skill", "Kotlin")], Nobody, "teste", Now);
        var li = p.AddDocument("Basic_LinkedInDataExport.zip", "linkedin", "b2", "/tmp/li.zip", "li", 1, Now);
        p.ReplaceFacts(li, [Fact(p, li, "experience", "Software Engineer, BOATLUX", org: "Boatlux", role: "Software engineer"), Fact(p, li, "skill", "C#")], Nobody, "teste", Now);
        var before = p.Version;

        var (file, removed) = p.RemoveDocument(cv.Id, Now);

        Assert.Equal(("/tmp/cv.pdf", 1), (file, removed));
        Assert.Equal([li.Id], p.Documents.Select(d => d.Id));
        Assert.Equal(["C#"], p.Facts.Where(f => f.Kind == "skill").Select(f => f.Statement));
        Assert.All(p.Facts, f => Assert.Equal(li.Id, f.DocumentId));
        Assert.True(p.Version > before);
        Assert.Throws<DomainRuleException>(() => p.RemoveDocument(cv.Id, Now));
    }

    [Fact]
    public void Curriculo_e_LinkedIn_se_somam_sem_repetir()
    {
        var p = CandidateProfile.Create(Now);
        var cv = p.AddDocument("cv.pdf", "pdf", "a1", "/tmp/cv.pdf", "cv", 1, Now);
        var job = Fact(p, cv, "experience", "Software Engineer, BOATLUX", org: "BOATLUX", role: "Software Engineer", tech: "C#");
        p.ReplaceFacts(cv, [job, Fact(p, cv, "achievement", "Shipped the Workdeck module in waves from November.", job.Id), Fact(p, cv, "skill", "C#"),
            Fact(p, cv, "language", "Inglês (avançado)")], Nobody, "teste", Now);

        var li = p.AddDocument("Basic_LinkedInDataExport_11-05-2026.zip", "linkedin", "b2", "/tmp/li.zip", "li", 1, Now);
        var same = Fact(p, li, "experience", "Software Engineer, BOATLUX", org: "Boatlux", role: "Software engineer", tech: "Terraform");
        p.ReplaceFacts(li, [same, Fact(p, li, "achievement", "Shipped the Workdeck module in waves from November.", same.Id),
            Fact(p, li, "achievement", "Led the migration of the Hub API to .NET 10 in production.", same.Id), Fact(p, li, "skill", "C#"), Fact(p, li, "skill", "Terraform"),
            Fact(p, li, "education", "Bachelor degree, Computer science, UERJ", org: "UERJ"), Fact(p, li, "language", "Inglês (avançado)")], Nobody, "teste", Now);

        var experience = Assert.Single(p.Facts, f => f.Kind == "experience");
        Assert.Equal(job.Id, experience.Id);
        Assert.Equal(["C#", "Terraform"], experience.Technologies);
        Assert.Equal([li.Id], experience.AlsoIn);
        Assert.Equal(2, p.Facts.Count(f => f.Kind == "achievement" && f.ParentId == job.Id));
        Assert.Equal(["C#", "Terraform"], p.Facts.Where(f => f.Kind == "skill").Select(f => f.Statement).Order());
        Assert.Single(p.Facts, f => f.Kind == "education");
        Assert.Single(p.Facts, f => f.Kind == "language");
    }

    [Fact]
    public void Trocar_o_curriculo_mantem_o_que_o_LinkedIn_tambem_traz()
    {
        var p = CandidateProfile.Create(Now);
        var cv = p.AddDocument("cv.pdf", "pdf", "a1", "/tmp/cv.pdf", "cv", 1, Now);
        var job = Fact(p, cv, "experience", "Software Engineer, BOATLUX", org: "BOATLUX", role: "Software Engineer");
        p.ReplaceFacts(cv, [job, Fact(p, cv, "achievement", "Shipped the Workdeck module in waves from November.", job.Id), Fact(p, cv, "skill", "Flutter")], Nobody, "teste", Now);
        var li = p.AddDocument("Basic_LinkedInDataExport.zip", "linkedin", "b2", "/tmp/li.zip", "li", 1, Now);
        var same = Fact(p, li, "experience", "Software Engineer, BOATLUX", org: "BOATLUX", role: "Software Engineer");
        p.ReplaceFacts(li, [same, Fact(p, li, "achievement", "Shipped the Workdeck module in waves from November.", same.Id),
            Fact(p, li, "achievement", "Led the migration of the Hub API to .NET 10 in production.", same.Id)], Nobody, "teste", Now);

        // Um currículo novo que não fala mais da BOATLUX: a experiência e as duas conquistas ficam, porque o LinkedIn também as traz.
        var cv2 = p.AddDocument("cv-novo.pdf", "pdf", "c3", "/tmp/cv2.pdf", "cv2", 1, Now);
        p.ReplaceFacts(cv2, [Fact(p, cv2, "skill", "Kotlin")], Nobody, "teste", Now);

        var experience = Assert.Single(p.Facts, f => f.Kind == "experience");
        Assert.Equal(li.Id, experience.DocumentId);
        Assert.Empty(experience.AlsoIn);
        Assert.Equal(2, p.Facts.Count(f => f.Kind == "achievement" && f.ParentId == experience.Id && f.DocumentId == li.Id));
        // A skill que só o currículo antigo trazia sai; a do novo entra.
        Assert.Equal(["Kotlin"], p.Facts.Where(f => f.Kind == "skill").Select(f => f.Statement));
    }

    [Fact]
    public void Reler_o_LinkedIn_troca_so_o_que_veio_dele_e_respeita_o_descarte()
    {
        var p = CandidateProfile.Create(Now);
        var li = p.AddDocument("Basic_LinkedInDataExport.zip", "linkedin", "b2", "/tmp/li.zip", "li", 1, Now);
        var doubt = VerifiedFact.Create(p.Id, li.Id, "skill", "Excel", FactStatus.Inferred, 0, Now);
        p.ReplaceFacts(li, [doubt, Fact(p, li, "skill", "C#")], Nobody, "teste", Now);
        p.Confirm(Confirmations.Empty, new Dictionary<Guid, bool> { [doubt.Id] = false }, Now);
        var again = p.AddDocument("Basic_LinkedInDataExport (1).zip", "linkedin", "b3", "/tmp/li2.zip", "li2", 1, Now);
        p.ReplaceFacts(again, [Fact(p, again, "skill", "Excel"), Fact(p, again, "skill", "C#"), Fact(p, again, "skill", "Go")], Nobody, "teste", Now);
        Assert.Equal(["C#", "Go"], p.Facts.Where(f => f.Kind == "skill" && f.Usable).Select(f => f.Statement).Order());
        Assert.Single(p.Facts, f => f.Statement == "Excel" && f.Status == FactStatus.Rejected);
    }

    [Fact]
    public void Do_zip_do_LinkedIn_so_os_csvs_do_perfil_ficam()
    {
        static byte[] Zip(params (string Name, string Text)[] files)
        {
            using var ms = new MemoryStream();
            using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                foreach (var (name, text) in files)
                {
                    var e = z.CreateEntry(name);
                    e.LastWriteTime = new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);
                    using var w = new StreamWriter(e.Open(), Encoding.UTF8);
                    w.Write(text);
                }
            return ms.ToArray();
        }
        var full = Zip(("Profile.csv", "First Name,Last Name\nGabriel,André"), ("Positions.csv", "Company Name,Title\nBOATLUX,Software Engineer"),
            ("messages.csv", "FROM,CONTENT\nAlguém,conversa privada"), ("Connections.csv", "First Name,Email Address\nFulano,fulano@example.com"), ("Ad_Targeting.csv", "x\ny"));
        var slim = LinkedInExport.Slim(new MemoryStream(full));
        using var z = new ZipArchive(new MemoryStream(slim));
        Assert.Equal(["Profile.csv", "Positions.csv"], z.Entries.Select(e => e.FullName));
        Assert.Equal(slim, LinkedInExport.Slim(new MemoryStream(full)));
    }

    [Fact]
    public void Na_pasta_de_downloads_so_aparece_o_export_do_LinkedIn()
    {
        var dir = Directory.CreateTempSubdirectory("jo-dl-").FullName;
        foreach (var (name, age) in new[] { ("Basic_LinkedInDataExport_11-05-2026.zip", 1), ("Complete_LinkedInDataExport_10-01-2026.zip", 30), ("fotos.zip", 0), ("cv.pdf", 0) })
        {
            var path = Path.Combine(dir, name);
            File.WriteAllText(path, "x");
            File.SetLastWriteTimeUtc(path, Now.AddDays(-age));
        }
        Assert.Equal(["Basic_LinkedInDataExport_11-05-2026.zip", "Complete_LinkedInDataExport_10-01-2026.zip"], LinkedInExport.Find(dir).Select(f => f.Name));
        Assert.True(LinkedInExport.Looks("Basic_LinkedInDataExport_11-05-2026 (1).zip"));
        Assert.False(LinkedInExport.Looks("LinkedInDataExport.zip.exe"));
    }
}
