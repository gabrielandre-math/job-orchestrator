namespace JobOrchestrator.Materials.Domain;

/// <summary>Uma afirmação do currículo e de onde ela veio. Check: "ok" ou o motivo de ter saído.</summary>
public sealed record ProvenanceLine(string Section, string? Experience, string Text, IReadOnlyList<string> Facts, IReadOnlyList<string> Sources, string Check);

/// <summary>
/// Uma versão do currículo para uma candidatura: o PDF, o HTML, a versão do perfil usada, quem escreveu, quem revisou,
/// e a proveniência de cada linha. O hash do PDF é o que o Guardião confere antes do envio.
/// </summary>
internal sealed class ResumeVersion
{
    private ResumeVersion() { }

    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Guid JobId { get; private set; }
    public int ProfileVersion { get; private set; }
    public string ProfileHash { get; private set; } = "";
    public string Language { get; private set; } = "";
    public string Mode { get; private set; } = "";
    public string? WrittenBy { get; private set; }
    public string? ReviewedBy { get; private set; }
    public bool Reviewed { get; private set; }
    public string Html { get; private set; } = "";
    public string PdfPath { get; private set; } = "";
    public string Sha256 { get; private set; } = "";
    public int Pages { get; private set; }
    public string Provenance { get; private set; } = "[]";
    public Guid ThreadId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public static ResumeVersion Create(Guid applicationId, Guid jobId, ProfileSnapshot profile, string language, string mode, string? writtenBy, string? reviewedBy, bool reviewed,
        string html, string pdfPath, string sha256, int pages, IEnumerable<ProvenanceLine> provenance, Guid threadId, DateTime now) => new()
    {
        Id = Guid.CreateVersion7(), ApplicationId = applicationId, JobId = jobId, ProfileVersion = profile.Version, ProfileHash = profile.Hash, Language = language, Mode = mode,
        WrittenBy = writtenBy, ReviewedBy = reviewedBy, Reviewed = reviewed, Html = html, PdfPath = pdfPath, Sha256 = sha256, Pages = pages,
        Provenance = JsonSerializer.Serialize(provenance, JsonSerializerOptions.Web), ThreadId = threadId, CreatedAt = now
    };
}
