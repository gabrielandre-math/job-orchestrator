using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace JobOrchestrator.Jobs.Domain;

/// <summary>Em que ponto está uma vaga dentro da campanha.</summary>
internal enum JobStatus { Discovered, Scored, Shortlisted, Rejected, Duplicate, Expired }

/// <summary>Agregado: uma vaga encontrada por uma campanha, com identidade canônica, avaliação e histórico de abertura.</summary>
internal sealed class JobPosting : AggregateRoot
{
    private JobPosting() { }
    public Guid Id { get; private set; }
    public Guid CampaignId { get; private set; }
    public string Source { get; private set; } = "";
    public string ExternalId { get; private set; } = "";
    public string CanonicalKey { get; private set; } = "";
    public string Company { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Location { get; private set; } = "";
    public string ApplyUrl { get; private set; } = "";
    public string Description { get; private set; } = "";
    public DateTime? SourceUpdatedAt { get; private set; }
    public DateTime FirstSeenAt { get; private set; }
    public DateTime LastSeenAt { get; private set; }
    public DateTime? LastVerifiedAt { get; private set; }
    public bool Open { get; private set; } = true;
    public JobStatus Status { get; private set; }
    public int? Score { get; private set; }
    public double? Confidence { get; private set; }
    public string Breakdown { get; private set; } = "[]";
    public string? Reason { get; private set; }
    public Guid? DuplicateOf { get; private set; }
    public uint Version { get; private set; }

    public static JobPosting Discover(Guid campaignId, DiscoveredPosting p, DateTime now) => new()
    {
        Id = Guid.CreateVersion7(),
        CampaignId = campaignId,
        Source = p.Source,
        ExternalId = p.ExternalId,
        CanonicalKey = Fingerprint.For(p.Company, p.Title, p.Location, p.ApplyUrl),
        Company = p.Company.Trim(),
        Title = p.Title.Trim(),
        Location = p.Location.Trim(),
        ApplyUrl = p.ApplyUrl.Trim(),
        Description = p.Description.Length > 20_000 ? p.Description[..20_000] : p.Description,
        SourceUpdatedAt = p.UpdatedAt,
        FirstSeenAt = now,
        LastSeenAt = now,
        Status = JobStatus.Discovered
    };

    public void SeenAgain(DiscoveredPosting p, DateTime now)
    {
        LastSeenAt = now;
        SourceUpdatedAt = p.UpdatedAt;
        Description = p.Description.Length > 20_000 ? p.Description[..20_000] : p.Description;
    }

    /// <summary>A mesma vaga já chegou por outra fonte: fica registrada, mas não gera candidatura.</summary>
    public void MarkDuplicate(Guid original)
    {
        Status = JobStatus.Duplicate;
        DuplicateOf = original;
        Reason = "Mesma vaga já encontrada por outra fonte.";
    }

    public void Verified(PostingStatus status, DateTime now)
    {
        Open = status.Open;
        LastVerifiedAt = now;
        if (!status.Open && Status is not JobStatus.Duplicate)
        {
            Status = JobStatus.Expired;
            Reason = status.Detail;
        }
    }

    /// <summary>Aplica elegibilidade e nota. Devolve true só na primeira vez que a vaga entra na fila.</summary>
    public bool Evaluate(Evaluation e, int minFitScore)
    {
        if (Status is JobStatus.Duplicate or JobStatus.Expired or JobStatus.Shortlisted) return false;
        Score = e.Score;
        Confidence = e.Confidence;
        Breakdown = JsonSerializer.Serialize(e.Factors, JsonSerializerOptions.Web);
        if (e.Rejection is not null)
        {
            Status = JobStatus.Rejected;
            Reason = e.Rejection;
            return false;
        }
        if (e.Score < minFitScore)
        {
            Status = JobStatus.Scored;
            Reason = $"Nota {e.Score} abaixo do corte da campanha ({minFitScore}).";
            return false;
        }
        Status = JobStatus.Shortlisted;
        Reason = null;
        return true;
    }
}

/// <summary>Cópia do que a fonte devolveu, guardada só quando muda.</summary>
internal sealed class JobSnapshot
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public DateTime FetchedAt { get; set; }
    public string PayloadHash { get; set; } = "";
    public string Payload { get; set; } = "{}";
}

/// <summary>
/// Identidade canônica de uma vaga. Com id de vaga do ATS no link, a identidade é o ATS + id (vale entre fontes);
/// sem isso, é o hash de empresa, cargo e local normalizados.
/// </summary>
internal static partial class Fingerprint
{
    public static string For(string company, string title, string location, string applyUrl)
    {
        if (Uri.TryCreate(applyUrl, UriKind.Absolute, out var uri) && AtsJob().Match(uri.AbsolutePath) is { Success: true } m)
            return $"ats:{uri.Authority.ToLowerInvariant()}:{m.Groups[1].Value}";
        var basis = $"{Normalize(company)}|{Normalize(title)}|{Normalize(location)}";
        return "fp:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(basis)))[..32];
    }

    internal static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var plain = new string([.. decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)]);
        return Spaces().Replace(NonWord().Replace(plain, " "), " ").Trim();
    }

    [GeneratedRegex(@"/jobs/(\d+)")] private static partial Regex AtsJob();
    [GeneratedRegex(@"[^a-z0-9#+.]+")] private static partial Regex NonWord();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
}
