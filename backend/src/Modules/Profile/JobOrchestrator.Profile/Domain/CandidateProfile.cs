using System.Security.Cryptography;
using System.Text;

namespace JobOrchestrator.Profile.Domain;

public enum FactStatus { Verified, Inferred, Confirmed, Rejected }
public enum DocumentStatus { Stored, Extracting, Ready, Failed }
public enum ProfileStatus { Empty, Extracting, NeedsConfirmation, Ready }

/// <summary>
/// Documento de onde os fatos saem: CV em PDF ou DOCX, texto, ou o export do LinkedIn. O arquivo fica no diretório de dados,
/// fora do git; o texto extraído fica no banco para a evidência de cada fato poder ser conferida.
/// </summary>
internal sealed class SourceDocument
{
    private SourceDocument() { }

    public Guid Id { get; private set; }
    public Guid ProfileId { get; private set; }
    public string FileName { get; private set; } = "";
    public string Kind { get; private set; } = "";
    public string Sha256 { get; private set; } = "";
    public string StoredPath { get; private set; } = "";
    public int Pages { get; private set; }
    public string Text { get; private set; } = "";
    public DocumentStatus Status { get; private set; }
    public string? Error { get; private set; }
    public string? ExtractedBy { get; private set; }
    public DateTime ImportedAt { get; private set; }
    public DateTime? ExtractedAt { get; private set; }

    internal static SourceDocument Create(Guid profileId, string fileName, string kind, string sha256, string path, string text, int pages, DateTime now) =>
        new() { Id = Guid.CreateVersion7(), ProfileId = profileId, FileName = fileName, Kind = kind, Sha256 = sha256, StoredPath = path, Text = text, Pages = pages, Status = DocumentStatus.Stored, ImportedAt = now };

    internal void Extracting() { Status = DocumentStatus.Extracting; Error = null; }
    internal void Ready(string by, DateTime now) { Status = DocumentStatus.Ready; ExtractedBy = by; ExtractedAt = now; Error = null; }
    internal void Failed(string error, DateTime now) { Status = DocumentStatus.Failed; Error = error.Length > 300 ? error[..300] : error; ExtractedAt = now; }

    /// <summary>Páginas do texto: o pdftotext separa com form feed.</summary>
    public IReadOnlyList<string> PageTexts => Text.Split('\f');
}

/// <summary>
/// Um fato do perfil. Verified: o trecho citado foi achado no documento. Inferred: o modelo disse, mas o trecho não bate;
/// vai para a tela única. Confirmed: você confirmou. Rejected: você descartou (fica guardado para não voltar).
/// </summary>
internal sealed class VerifiedFact
{
    private VerifiedFact() { }

    public Guid Id { get; private set; }
    public Guid ProfileId { get; private set; }
    public Guid? DocumentId { get; private set; }
    public string Kind { get; private set; } = "";
    public string Statement { get; private set; } = "";
    public Guid? ParentId { get; private set; }
    public string? Organization { get; private set; }
    public string? Role { get; private set; }
    public string? Start { get; private set; }
    public string? End { get; private set; }
    public List<string> Technologies { get; private set; } = [];
    public string? Level { get; private set; }
    public string? Quote { get; private set; }
    public int? Page { get; private set; }
    public string? Note { get; private set; }
    public FactStatus Status { get; private set; }
    public int Position { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? DecidedAt { get; private set; }

    internal static VerifiedFact Create(Guid profileId, Guid? documentId, string kind, string statement, FactStatus status, int position, DateTime now,
        Guid? parentId = null, string? organization = null, string? role = null, string? start = null, string? end = null,
        IEnumerable<string>? technologies = null, string? level = null, string? quote = null, int? page = null, string? note = null) =>
        new()
        {
            Id = Guid.CreateVersion7(), ProfileId = profileId, DocumentId = documentId, Kind = kind, Statement = Cut(statement, 600)!, Status = status,
            Position = position, CreatedAt = now, ParentId = parentId, Organization = Cut(organization, 200), Role = Cut(role, 200), Start = start, End = end,
            Technologies = [.. (technologies ?? []).Select(t => Cut(t, 60)!).Distinct(StringComparer.OrdinalIgnoreCase)], Level = level,
            Quote = Cut(quote, 600), Page = page, Note = Cut(note, 300)
        };

    internal void Decide(bool accept, DateTime now)
    {
        Status = accept ? FactStatus.Confirmed : FactStatus.Rejected;
        DecidedAt = now;
    }

    public bool Usable => Status is FactStatus.Verified or FactStatus.Confirmed;

    private static string? Cut(string? s, int max) => s is null ? null : s.Length > max ? s[..max] : s;
}

/// <summary>
/// O perfil verdadeiro (agregado). Só um por instalação. Muda por import de documento, pela tela única de confirmação e
/// pela edição dos dados pessoais; cada mudança que altera o conteúdo gera uma versão nova com hash próprio.
/// </summary>
internal sealed class CandidateProfile : AggregateRoot
{
    private readonly List<VerifiedFact> _facts = [];
    private readonly List<SourceDocument> _documents = [];

    private CandidateProfile() { }

    public Guid Id { get; private set; }
    public int Version { get; private set; }
    public string Hash { get; private set; } = "";
    public bool Synthetic { get; private set; }
    public ProfileStatus Status { get; private set; }
    public string FullName { get; private set; } = "";
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Location { get; private set; }
    public string? LinkedIn { get; private set; }
    public string? GitHub { get; private set; }
    public string? Website { get; private set; }
    public string ConfirmationsJson { get; private set; } = "{}";
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? ConfirmedAt { get; private set; }
    public uint RowVersion { get; private set; }

    public IReadOnlyList<VerifiedFact> Facts => _facts;
    public IReadOnlyList<SourceDocument> Documents => _documents;

    public Identity Identity => new(FullName, Email, Phone, Location, LinkedIn, GitHub, Website);

    public Confirmations Confirmations =>
        ConfirmationsJson is { Length: > 2 } j ? JsonSerializer.Deserialize<Confirmations>(j, JsonSerializerOptions.Web) ?? Confirmations.Empty : Confirmations.Empty;

    public bool Pending => _facts.Any(f => f.Status == FactStatus.Inferred) || ConfirmedAt is null || FullName.Length == 0;

    public static CandidateProfile Create(DateTime now, bool synthetic = false) =>
        new() { Id = Guid.CreateVersion7(), CreatedAt = now, UpdatedAt = now, Status = ProfileStatus.Empty, Synthetic = synthetic };

    /// <summary>Guarda o documento. O mesmo arquivo (mesmo hash) não entra duas vezes.</summary>
    public SourceDocument AddDocument(string fileName, string kind, string sha256, string path, string text, int pages, DateTime now)
    {
        if (_documents.FirstOrDefault(d => d.Sha256 == sha256) is { } same) return same;
        if (Synthetic)
        {
            // O primeiro documento de verdade apaga o perfil de teste inteiro: nada sintético se mistura com fatos reais.
            _facts.Clear();
            FullName = "";
            Email = Phone = Location = LinkedIn = GitHub = Website = null;
            ConfirmationsJson = "{}";
            ConfirmedAt = null;
            Synthetic = false;
        }
        var doc = SourceDocument.Create(Id, fileName, kind, sha256, path, text, pages, now);
        _documents.Add(doc);
        UpdatedAt = now;
        return doc;
    }

    public void Extracting(SourceDocument doc)
    {
        doc.Extracting();
        Status = ProfileStatus.Extracting;
    }

    /// <summary>
    /// Os fatos de um documento novo substituem os que vieram de documentos (os seus descartes e confirmações manuais ficam).
    /// Dados pessoais achados no documento só preenchem o que ainda está vazio: o que você editou vale mais.
    /// </summary>
    public void ReplaceFacts(SourceDocument doc, IEnumerable<VerifiedFact> facts, Identity found, string by, DateTime now)
    {
        _facts.RemoveAll(f => f.DocumentId is not null && f.Status != FactStatus.Rejected);
        var rejected = _facts.Where(f => f.Status == FactStatus.Rejected).Select(f => Key(f)).ToHashSet();
        _facts.AddRange(facts.Where(f => !rejected.Contains(Key(f))));
        FullName = FullName.Length > 0 ? FullName : found.FullName;
        Email ??= found.Email;
        Phone ??= found.Phone;
        Location ??= found.Location;
        LinkedIn ??= found.LinkedIn;
        GitHub ??= found.GitHub;
        Website ??= found.Website;
        doc.Ready(by, now);
        Status = Pending ? ProfileStatus.NeedsConfirmation : ProfileStatus.Ready;
        Touch(now);
    }

    public void ExtractionFailed(SourceDocument doc, string error, DateTime now)
    {
        doc.Failed(error, now);
        Status = _facts.Count == 0 ? ProfileStatus.Empty : Pending ? ProfileStatus.NeedsConfirmation : ProfileStatus.Ready;
        UpdatedAt = now;
    }

    public void SetIdentity(Identity id, DateTime now)
    {
        FullName = id.FullName.Trim();
        Email = Blank(id.Email);
        Phone = Blank(id.Phone);
        Location = Blank(id.Location);
        LinkedIn = Blank(id.LinkedIn);
        GitHub = Blank(id.GitHub);
        Website = Blank(id.Website);
        Touch(now);
    }

    /// <summary>A tela única: decisões sobre o que o modelo inferiu e as respostas que só você pode dar.</summary>
    public void Confirm(Confirmations c, IReadOnlyDictionary<Guid, bool> decisions, DateTime now)
    {
        foreach (var (id, accept) in decisions)
            if (_facts.FirstOrDefault(f => f.Id == id && f.Status == FactStatus.Inferred) is { } fact) fact.Decide(accept, now);
        if (_facts.Any(f => f.Status == FactStatus.Inferred)) throw new DomainRuleException("Ainda há fatos para confirmar ou descartar.");
        ConfirmationsJson = JsonSerializer.Serialize(c with { ConfirmedAt = now }, JsonSerializerOptions.Web);
        ConfirmedAt = now;
        Status = FullName.Length > 0 ? ProfileStatus.Ready : ProfileStatus.NeedsConfirmation;
        Touch(now);
    }

    /// <summary>Perfil de teste, montado de um arquivo: nunca vai para vaga real (o Guardião confere o host).</summary>
    /// <summary>Recomeça do zero: fatos, documentos, identidade e confirmações saem; a versão sobe.</summary>
    public IReadOnlyList<string> Reset(DateTime now)
    {
        var files = _documents.Select(d => d.StoredPath).ToList();
        _facts.Clear();
        _documents.Clear();
        FullName = "";
        Email = Phone = Location = LinkedIn = GitHub = Website = null;
        ConfirmationsJson = "{}";
        ConfirmedAt = null;
        Synthetic = false;
        Status = ProfileStatus.Empty;
        Touch(now);
        return files;
    }

    public void Seed(Identity id, IEnumerable<VerifiedFact> facts, Confirmations c, DateTime now)
    {
        SetIdentity(id, now);
        _facts.AddRange(facts);
        ConfirmationsJson = JsonSerializer.Serialize(c with { ConfirmedAt = now }, JsonSerializerOptions.Web);
        ConfirmedAt = now;
        Synthetic = true;
        Status = ProfileStatus.Ready;
        Touch(now);
    }

    /// <summary>Recalcula o hash do conteúdo; se mudou, a versão sobe.</summary>
    private void Touch(DateTime now)
    {
        UpdatedAt = now;
        var canonical = JsonSerializer.Serialize(new
        {
            Identity, ConfirmationsJson = Confirmations with { ConfirmedAt = null },
            Facts = _facts.Where(f => f.Usable).OrderBy(f => f.Kind).ThenBy(f => f.Position)
                .Select(f => new { f.Kind, f.Statement, f.Organization, f.Role, f.Start, f.End, f.Technologies, f.Level })
        });
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        if (hash == Hash) return;
        Hash = hash;
        Version++;
    }

    private static string Key(VerifiedFact f) => $"{f.Kind}|{f.Statement.Trim().ToLowerInvariant()}";
    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
