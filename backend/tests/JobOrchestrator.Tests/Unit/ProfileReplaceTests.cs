using JobOrchestrator.Profile.Contracts;
using JobOrchestrator.Profile.Domain;

namespace JobOrchestrator.Tests.Unit;

/// <summary>A new resume replaces the whole profile only after it was read; a file that fails leaves the old profile as it was.</summary>
public sealed class ProfileReplaceTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc);

    private static VerifiedFact Fact(CandidateProfile p, SourceDocument d, string kind, string statement) =>
        VerifiedFact.Create(p.Id, d.Id, kind, statement, FactStatus.Verified, 0, Now, quote: statement);

    private static CandidateProfile Old(out SourceDocument cv, out SourceDocument li)
    {
        var p = CandidateProfile.Create(Now);
        cv = p.AddDocument("ana.pdf", "pdf", "a1", "/tmp/ana.pdf", "cv", 1, Now);
        p.ReplaceFacts(cv, [Fact(p, cv, "skill", "C#")], new Identity("Ana Antiga", "ana@example.com", null, null, null, null, null), "teste", Now);
        li = p.AddDocument("Basic_LinkedInDataExport.zip", "linkedin", "b2", "/tmp/li.zip", "li", 1, Now);
        p.ReplaceFacts(li, [Fact(p, li, "skill", "Terraform")], new Identity("", null, null, null, null, null, null), "teste", Now);
        return p;
    }

    [Fact]
    public void A_replacement_that_was_read_takes_the_whole_profile()
    {
        var p = Old(out _, out _);
        var bruno = p.AddDocument("bruno.pdf", "pdf", "c3", "/tmp/bruno.pdf", "cv", 1, Now);
        bruno.MarkReplacement();

        var gone = p.Supersede(bruno, Now);
        p.ReplaceFacts(bruno, [Fact(p, bruno, "skill", "Go")], new Identity("Bruno Novo", "bruno@example.com", null, null, null, null, null), "teste", Now);

        Assert.Equal(["/tmp/ana.pdf", "/tmp/li.zip"], gone);
        Assert.Equal([bruno.Id], p.Documents.Select(d => d.Id));
        Assert.Equal(["Go"], p.Facts.Select(f => f.Statement));
        Assert.Equal("Bruno Novo", p.Identity.FullName);
        Assert.Equal("bruno@example.com", p.Identity.Email);
    }

    [Fact]
    public void A_replacement_that_failed_leaves_the_old_profile()
    {
        var p = Old(out _, out _);
        var broken = p.AddDocument("quebrado.pdf", "pdf", "d4", "/tmp/quebrado.pdf", "", 0, Now);
        broken.MarkReplacement();

        p.ExtractionFailed(broken, "sem texto", Now);

        Assert.Equal(["C#", "Terraform"], p.Facts.Select(f => f.Statement).Order());
        Assert.Equal("Ana Antiga", p.Identity.FullName);
        Assert.Equal(3, p.Documents.Count);
    }
}
