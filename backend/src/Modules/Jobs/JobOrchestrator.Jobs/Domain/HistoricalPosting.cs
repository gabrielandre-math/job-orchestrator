namespace JobOrchestrator.Jobs.Domain;

/// <summary>Uma linha de um export histórico (Open Jobs ou outro CSV ou Parquet no mesmo formato), como o importador manda.</summary>
internal sealed record HistoryRowIn(string Ats, string Board, string Id, string Title, string? Company, string? Location, string? Url,
    DateTime? FirstSeenAt, DateTime? LastSeenAt, bool? SnapshotOpen, string? RemoteScope, string? Arrangement);

/// <summary>
/// Vaga de um snapshot histórico. Entra sempre como UNVERIFIED: estar no arquivo não abre vaga, nem quando o export diz que estava
/// aberta (especificação V2: nada de OPEN só por aparecer num snapshot antigo). Só a fonte ao vivo muda o estado: OPEN quando o
/// quadro lista a vaga hoje, CLOSED quando o quadro respondeu e ela não está lá. Não vira vaga da fila: o quadro com vaga aberta
/// vira pista do Explorador, que passa a vigiá-lo pela porta oficial e traz a vaga com first_seen e last_seen honestos.
/// </summary>
internal sealed class HistoricalPosting
{
    public const string Unverified = "UNVERIFIED", Open = "OPEN", Closed = "CLOSED";

    public Guid Id { get; private set; }
    public string Snapshot { get; private set; } = "";
    public string Ats { get; private set; } = "";
    public string Board { get; private set; } = "";
    public string ExternalId { get; private set; } = "";
    /// <summary>A fonte do app que lê este quadro ao vivo; nula quando o app não tem conector para o ATS.</summary>
    public string? Source { get; private set; }
    public string Title { get; private set; } = "";
    public string Company { get; private set; } = "";
    public string Location { get; private set; } = "";
    public string Url { get; private set; } = "";
    public string? RemoteScope { get; private set; }
    public string? Arrangement { get; private set; }
    public string? Eligibility { get; private set; }
    public string? EligibilityEvidence { get; private set; }
    public DateTime? FirstSeenAt { get; private set; }
    public DateTime? LastSeenAt { get; private set; }
    /// <summary>O que o export dizia (aberta no dia do snapshot). Fica como fato histórico; não decide nada.</summary>
    public bool? SnapshotOpen { get; private set; }
    public string Status { get; private set; } = Unverified;
    public DateTime? CheckedAt { get; private set; }
    public string? Note { get; private set; }
    public int Attempts { get; private set; }
    public DateTime ImportedAt { get; private set; }

    private HistoricalPosting() { }

    public static HistoricalPosting Import(string snapshot, HistoryRowIn row, DateTime now)
    {
        var h = new HistoricalPosting { Id = Guid.CreateVersion7(now), Snapshot = snapshot, Ats = Cut(row.Ats, 40), Board = Cut(row.Board, 200), ExternalId = Cut(row.Id, 200), ImportedAt = now };
        h.Refresh(row);
        return h;
    }

    /// <summary>A mesma vaga importada de novo: os dados do arquivo se atualizam, o estado verificado fica como está.</summary>
    public void Refresh(HistoryRowIn row)
    {
        Title = Cut(row.Title, 300);
        Company = Cut(row.Company ?? Board, 200);
        Location = Cut(row.Location ?? "", 300);
        Url = Cut(row.Url ?? "", 1000);
        RemoteScope = row.RemoteScope is { Length: > 0 } scope ? Cut(scope, 120) : null;
        Arrangement = row.Arrangement is { Length: > 0 } a ? Cut(a, 20) : null;
        FirstSeenAt = row.FirstSeenAt;
        LastSeenAt = row.LastSeenAt;
        SnapshotOpen = row.SnapshotOpen;
        Source = SourceKeys.OpenJobs(Ats, Board) ?? (Url.Length > 0 ? SourceKeys.Board(Url) : null);
        var remote = Arrangement == "remote" ? "Remoto" : null;
        var e = Domain.Eligibility.Classify(Title, RemoteScope is null ? "" : $"Remote scope: {RemoteScope}", Location, remote);
        Eligibility = e.Class;
        EligibilityEvidence = Cut(e.Evidence, 320);
    }

    /// <summary>
    /// A conferência com o quadro ao vivo (a rodada do conector da fonte, com o portão de permissão já aplicado). Aberta só se o
    /// quadro lista esta vaga agora (mesmo id ou mesmo link); encerrada se o quadro respondeu sem ela ou marcou o encerramento.
    /// </summary>
    public void Verify(IReadOnlyCollection<DiscoveredPosting> live, DateTime now)
    {
        var match = live.FirstOrDefault(p => string.Equals(p.ExternalId, ExternalId, StringComparison.OrdinalIgnoreCase) || SameUrl(p.ApplyUrl, Url));
        Status = match is { Tombstone: false } ? Open : Closed;
        Note = match is null ? $"o quadro respondeu com {live.Count(p => !p.Tombstone)} vagas e esta não está mais lá"
            : match.Tombstone ? "a fonte marcou a vaga como encerrada" : "listada no quadro ao vivo";
        CheckedAt = now;
        Attempts++;
    }

    /// <summary>O quadro não respondeu ou o portão não deixou ler: o estado fica como estava, com o motivo e a hora.</summary>
    public void Fail(string reason, DateTime now)
    {
        Note = Cut(reason, 300);
        CheckedAt = now;
        Attempts++;
    }

    internal static bool SameUrl(string? a, string? b)
    {
        static string Norm(string s) => (Uri.TryCreate(s.Trim(), UriKind.Absolute, out var u) ? $"{u.Host}{u.AbsolutePath}" : s.Trim()).TrimEnd('/').ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && Norm(a) == Norm(b);
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];
}
