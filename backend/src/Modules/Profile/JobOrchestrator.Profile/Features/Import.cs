using System.Security.Cryptography;
using JobOrchestrator.Profile.Import;

namespace JobOrchestrator.Profile.Features;

/// <summary>O documento recebido.</summary>
internal sealed record ImportedView(Guid DocumentId, string Status, int Pages);

/// <summary>
/// Comando: guarda o CV (ou o export do LinkedIn) no diretório de dados, tira o texto e pede a leitura ao Worker.
/// O mesmo arquivo de novo não duplica nada.
/// </summary>
internal sealed record ImportDocumentCommand(string FileName, byte[] Content) : ICommand<ImportedView>, IProfileCommand;

internal sealed class ImportDocumentValidator : AbstractValidator<ImportDocumentCommand>
{
    private static readonly string[] Allowed = [".pdf", ".docx", ".txt", ".md", ".zip"];

    public ImportDocumentValidator()
    {
        RuleFor(x => x.Content).Must(c => c.Length is > 0 and <= TextExtractor.MaxBytes)
            .WithMessage(x => x.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "O ZIP passa de 15 MB. Deixe-o na pasta Downloads e use Conectar LinkedIn, que acha o arquivo lá." : "O arquivo precisa ter até 15 MB.");
        RuleFor(x => x.FileName).Must(n => Allowed.Contains(Path.GetExtension(n).ToLowerInvariant())).WithMessage("Envie PDF, DOCX, TXT ou o ZIP do export do LinkedIn.");
    }
}

internal sealed class ImportDocumentHandler(ProfileDbContext db, TimeProvider time) : ICommandHandler<ImportDocumentCommand, ImportedView>
{
    public async Task<Result<ImportedView>> Handle(ImportDocumentCommand c, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var ext = Path.GetExtension(c.FileName).ToLowerInvariant();
        var content = c.Content;
        // Do ZIP do LinkedIn só os CSVs do perfil são guardados; mensagens, conexões e o resto ficam de fora.
        if (ext == ".zip")
        {
            try { content = LinkedInExport.Slim(new MemoryStream(c.Content)); }
            catch (InvalidDataException) { return Error.Validation("unreadable", "O ZIP está incompleto ou corrompido. Baixe de novo pelo link do e-mail do LinkedIn."); }
        }
        var sha = Convert.ToHexStringLower(SHA256.HashData(content));
        // Fora do git, no diretório de dados: o CV nunca vai para o repositório.
        var path = Path.Combine(AppPaths.In(AppPaths.Data, "profile", "documents"), sha + ext);
        if (!File.Exists(path)) await File.WriteAllBytesAsync(path, content, ct);
        ExtractedText text;
        try { text = await TextExtractor.ExtractAsync(path, c.FileName, ct); }
        catch (NotSupportedException ex) { return Error.Validation("unreadable", ex.Message); }

        var profile = await db.CurrentAsync(ct);
        if (profile is null)
        {
            profile = CandidateProfile.Create(now);
            db.Profiles.Add(profile);
        }
        var known = profile.Documents.Any(d => d.Sha256 == sha);
        var doc = profile.AddDocument(Path.GetFileName(c.FileName), text.Kind, sha, path, text.Text, text.Pages, now);
        if (!known || doc.Status == DocumentStatus.Failed)
        {
            profile.Extracting(doc);
            db.Publish(DocumentImportedV1.EventType, new DocumentImportedV1(profile.Id, doc.Id), now);
        }
        return new ImportedView(doc.Id, doc.Status.ToString(), doc.Pages);
    }
}

/// <summary>Comando: lê de novo um documento que falhou ou ficou parado (Worker reiniciado no meio, por exemplo).</summary>
internal sealed record RetryDocumentCommand(Guid DocumentId) : ICommand<Done>, IProfileCommand;

internal sealed class RetryDocumentHandler(ProfileDbContext db, TimeProvider time) : ICommandHandler<RetryDocumentCommand, Done>
{
    public async Task<Result<Done>> Handle(RetryDocumentCommand c, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var profile = await db.CurrentAsync(ct);
        var doc = profile?.Documents.FirstOrDefault(d => d.Id == c.DocumentId);
        if (profile is null || doc is null) return Error.NotFound("document-not-found", "Documento não encontrado.");
        var stuck = doc.Status == DocumentStatus.Extracting && now - doc.ImportedAt > TimeSpan.FromMinutes(15);
        if (doc.Status is not (DocumentStatus.Failed or DocumentStatus.Stored) && !stuck)
            return Error.Conflict("document-busy", "Este documento já está sendo lido ou já foi lido.");
        profile.Extracting(doc);
        db.Publish(DocumentImportedV1.EventType, new DocumentImportedV1(profile.Id, doc.Id), now);
        return new Done();
    }
}

/// <summary>
/// No Worker: Leitor, Extrator e Conferente conversam num fio que aparece no canvas. Idempotente pela inbox; a chamada ao
/// modelo fica fora da transação, para um CV longo não segurar o banco.
/// </summary>
internal sealed class DocumentImportedConsumer(ProfileDbContext db, IIntelligenceApi ai, TimeProvider time, ILogger<DocumentImportedConsumer> log) : IEventConsumer
{
    public string EventType => DocumentImportedV1.EventType;

    public async Task ConsumeAsync(Envelope message, CancellationToken ct)
    {
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            if (!await db.RegisterInInboxAsync(message.MessageId, time.GetUtcNow().UtcDateTime, ct)) return;
            await tx.CommitAsync(ct);
        }
        var e = message.Read<DocumentImportedV1>();
        var profile = await db.CurrentAsync(ct);
        var doc = profile?.Documents.FirstOrDefault(d => d.Id == e.DocumentId);
        if (profile is null || doc is null || doc.Status == DocumentStatus.Ready) return;

        var thread = AgentThread.New("profile", $"Leitura de {doc.FileName}");
        var now = time.GetUtcNow().UtcDateTime;
        try
        {
            if (doc.Kind == "linkedin")
            {
                var text = await TextExtractor.ExtractAsync(doc.StoredPath, doc.FileName, ct);
                var (facts, found) = LinkedInFacts.Map(text.Tables!, profile.Id, doc.Id, now);
                await ai.NoteAsync(thread, "Leitor", $"Li o export do LinkedIn: {Count(facts, "experience")} experiências, {Count(facts, "education")} formações e {Count(facts, "skill")} skills",
                    "Cada linha dos CSVs já é estruturada, então os fatos saem verificados, sem IA, com a evidência arquivo e linha.", "Conferente", true, ct);
                profile.ReplaceFacts(doc, facts, found, "export do LinkedIn (sem IA)", now);
                await ai.NoteAsync(thread, "Conferente", $"{facts.Count} fatos verificados", null, "Você", true, ct);
            }
            else
            {
                var identity = Merge(profile.Identity, Redactor.Detect(doc.Text));
                var redacted = Redactor.Redact(doc.Text, identity);
                await ai.NoteAsync(thread, "Leitor", $"Li {doc.Pages} {(doc.Pages == 1 ? "página" : "páginas")} de {doc.FileName}, {doc.Text.Length.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))} caracteres",
                    $"Antes de mandar para o modelo, tirei: {Removed(redacted)}. Esses dados ficam só aqui e voltam no PDF final.", "Extrator", true, ct);
                var extraction = await new FactExtractor(ai).ExtractAsync(profile.Id, doc.Id, doc.Text, identity, thread, now, ct);
                if (!extraction.Ok)
                {
                    profile.ExtractionFailed(doc, extraction.Error ?? "falhou", time.GetUtcNow().UtcDateTime);
                    await ai.NoteAsync(thread, "Conferente", "Nada para conferir: a leitura falhou", extraction.Error, "Você", false, ct);
                }
                else
                {
                    profile.ReplaceFacts(doc, extraction.Facts, extraction.Found, extraction.By, time.GetUtcNow().UtcDateTime);
                    var pending = extraction.Facts.Where(f => f.Status == FactStatus.Inferred).ToList();
                    await ai.NoteAsync(thread, "Conferente",
                        $"{extraction.Verified} fatos com trecho achado no documento, {extraction.Inferred} para você confirmar, {extraction.Dropped} tecnologias descartadas",
                        pending.Count == 0 ? "Todo fato tem o trecho de onde saiu." : string.Join("\n", pending.Select(f => $"- {f.Statement}: {f.Note}")),
                        "Você", true, ct);
                }
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Falha ao ler o documento {Document}", doc.Id);
            db.ChangeTracker.Clear();
            var again = await db.CurrentAsync(ct);
            var d = again?.Documents.FirstOrDefault(x => x.Id == e.DocumentId);
            if (again is not null && d is not null)
            {
                again.ExtractionFailed(d, ex.Message, time.GetUtcNow().UtcDateTime);
                await db.SaveChangesAsync(ct);
            }
            await ai.NoteAsync(thread, "Leitor", "A leitura parou com erro", ex.Message, "Você", false, ct);
        }
    }

    private static int Count(IEnumerable<VerifiedFact> facts, string kind) => facts.Count(f => f.Kind == kind);

    /// <summary>O que você já editou vale mais do que o que o documento sugere.</summary>
    private static Identity Merge(Identity mine, Identity found) => new(
        mine.FullName.Length > 0 ? mine.FullName : found.FullName, mine.Email ?? found.Email, mine.Phone ?? found.Phone, mine.Location ?? found.Location,
        mine.LinkedIn ?? found.LinkedIn, mine.GitHub ?? found.GitHub, mine.Website ?? found.Website);

    private static string Removed(string redacted)
    {
        var parts = new (string Token, string Label)[]
        {
            ("{{NOME}}", "nome"), ("{{EMAIL}}", "e-mail"), ("{{TELEFONE}}", "telefone"), ("{{LINKEDIN}}", "LinkedIn"), ("{{GITHUB}}", "GitHub"),
            ("{{SITE}}", "site"), ("{{DOCUMENTO}}", "documento"), ("{{ENDERECO}}", "endereço"), ("{{CEP}}", "CEP"), ("{{PRIVADO}}", "dado pessoal")
        };
        var found = parts.Select(p => (p.Label, Count: CountOf(redacted, p.Token))).Where(p => p.Count > 0).Select(p => p.Count > 1 ? $"{p.Label} ({p.Count}x)" : p.Label).ToList();
        return found.Count == 0 ? "nada (nenhum dado pessoal achado)" : string.Join(", ", found);
    }

    private static int CountOf(string text, string token)
    {
        var n = 0;
        for (var i = text.IndexOf(token, StringComparison.Ordinal); i >= 0; i = text.IndexOf(token, i + token.Length, StringComparison.Ordinal)) n++;
        return n;
    }
}
