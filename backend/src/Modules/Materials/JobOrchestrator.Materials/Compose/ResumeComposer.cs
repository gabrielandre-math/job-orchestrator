using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace JobOrchestrator.Materials.Compose;

/// <summary>
/// ResumeComposer: Seletor → Redator (nuvem, ou rascunho local) → Conferente → Revisor (outra família) → Renderizador.
/// Todos falam no mesmo fio, que o canvas mostra ao vivo. Sem nenhum modelo, sai o currículo só com os fatos (mode plain),
/// ou a candidatura pausa, se Materials:WhenNoAi = pause.
/// </summary>
internal sealed class ResumeComposer(IProfileApi profiles, IJobsApi jobs, IIntelligenceApi ai, IAutomationApi automation, MaterialsDbContext db,
    IConfiguration config, TimeProvider time, ILogger<ResumeComposer> log) : IMaterialsApi
{
    public async Task<ResumeInterviewContext?> ResumeForInterviewAsync(Guid applicationId, string sha256, CancellationToken ct)
    {
        var archived = await db.Resumes.AsNoTracking().Where(r => r.ApplicationId == applicationId && r.Sha256 == sha256)
            .OrderByDescending(r => r.CreatedAt).Select(r => new { r.Id, r.PdfPath, r.Html }).FirstOrDefaultAsync(ct);
        if (archived is null || !await MatchesArchiveAsync(archived.PdfPath, sha256, ct)) return null;
        return new(archived.Id, sha256, Template.InterviewText(archived.Html));
    }

    internal static async Task<bool> MatchesArchiveAsync(string path, string sha256, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return stream.Length <= 20_000_000
                && Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct)) == sha256;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    public async Task<ComposeResult> ComposeResumeAsync(ComposeRequest r, CancellationToken ct)
    {
        var profile = await profiles.CurrentAsync(ct);
        if (profile is null) return Fail("Nenhum perfil. Envie o seu currículo na tela Perfil.");
        var job = await jobs.TextAsync(r.JobId, ct);
        if (job is null) return Fail("Vaga não encontrada.");
        var thread = AgentThread.New("resume", $"Currículo para {Cut(r.Title, 90)}, {Cut(r.Company, 60)}");

        var s = Selector.Pick(profile, job);
        await ai.NoteAsync(thread, "Seletor",
            $"Separei {s.Of("experience").Count()} experiências, {s.Facts.Count(f => f.ParentLabel is not null)} conquistas e {s.Of("skill").Count()} skills. Idioma: {(s.Language == "en" ? "inglês" : "português")}, o mesmo da vaga",
            $"A vaga cita: {Join(s.Matched.Concat(s.Missing))}.\nNo seu perfil: {Join(s.Matched)}.\nFora do perfil, não entram: {Join(s.Missing)}.", "Redator", true, ct);

        Draft draft;
        string mode, by;
        string? writerFamily = null;
        var written = await ai.CompleteAsync(LlmRequest.For(LlmTasks.ResumeTailor, Prompts.Writer(s.Language), Prompts.WriterPrompt(job, s), Prompts.WriterSchema,
            timeoutSeconds: 300, thread: thread, agent: "Redator"), ct);
        var parsed = written.Ok ? Draft.Parse(written.Json!) : null;
        if (parsed is not null)
        {
            draft = parsed;
            mode = written.Degraded ? "local" : "ai";
            by = $"{written.ProviderId} · {written.Model}";
            writerFamily = written.Family;
        }
        else
        {
            if (config["Materials:WhenNoAi"] == "pause")
            {
                await ai.NoteAsync(thread, "Redator", "Sem modelo disponível: a candidatura espera um provedor voltar", written.Error, "Supervisor", false, ct);
                return new ComposeResult(false, null, null, null, s.Language, "none", false, written.Error, true, thread.Id);
            }
            draft = Draft.Plain(s);
            // Sem IA não há tradução: o currículo sai no idioma dos fatos.
            s = s with { Language = Languages.Detect(string.Join(" ", s.Facts.Select(f => f.Fact.Statement))) };
            mode = "plain";
            by = "sem IA";
            await ai.NoteAsync(thread, "Redator", "Sem modelo disponível: o currículo sai com os fatos como estão no perfil", written.Error, "Conferente", true, ct);
        }

        var checkedDraft = Conferente.Check(draft, s);
        draft = Tailor.Finish(checkedDraft.Draft, s);
        var claimCount = draft.Claims().Count();
        await ai.NoteAsync(thread, "Conferente",
            checkedDraft.Removed.Count == 0 ? $"Todas as {claimCount} afirmações citam fatos que as sustentam" : $"{claimCount} afirmações com fato; {checkedDraft.Removed.Count} {(checkedDraft.Removed.Count == 1 ? "saiu" : "saíram")}",
            checkedDraft.Removed.Count == 0 ? "Nenhum número, tecnologia ou nome próprio sem fato." : string.Join("\n", checkedDraft.Removed.Select(x => $"- {x}")),
            mode == "plain" ? "Renderizador" : "Revisor", true, ct);

        var reviewed = false;
        string? reviewer = null;
        if (mode != "plain")
        {
            var review = await ai.CompleteAsync(LlmRequest.For(LlmTasks.ResumeFactcheck, Prompts.Reviewer, Prompts.ReviewerPrompt(draft, s), Prompts.ReviewerSchema,
                avoidFamily: writerFamily, timeoutSeconds: 240, thread: thread, agent: "Revisor"), ct);
            if (review.Ok && Prompts.ParseVerdicts(review.Json!) is { } verdicts)
            {
                var applied = Conferente.Apply(draft, s, verdicts);
                draft = Tailor.Finish(applied.Draft, s);
                reviewed = true;
                reviewer = $"{review.ProviderId} · {review.Model}";
                await ai.NoteAsync(thread, "Conferente",
                    applied.Removed.Count == 0 ? "O Revisor aceitou todas as afirmações" : $"O Revisor recusou {applied.Removed.Count}; saíram do currículo",
                    applied.Removed.Count == 0 ? null : string.Join("\n", applied.Removed.Select(x => $"- {x}")), "Renderizador", true, ct);
            }
            else
                await ai.NoteAsync(thread, "Conferente", "Revisão cruzada indisponível agora: vale só a conferência sem IA, e o currículo vai para a sua revisão",
                    review.Error, "Renderizador", false, ct);
        }

        // Renderizador: PDF pelo Chromium, conferido pelo texto (pdftotext) e pelas páginas (pdfinfo). Passou do limite, corta a conquista menos relevante.
        var maxPages = config.GetValue("Materials:MaxPages", 2);
        // Nome de arquivo que recrutador e ATS esperam: Nome-Sobrenome-Resume.pdf, numa pasta por versão.
        var dir = AppPaths.In(AppPaths.Data, "materials", r.ApplicationId.ToString("N"), time.GetUtcNow().ToString("yyyyMMddHHmmss"));
        var path = Path.Combine(dir, $"{FileName(profile.Identity.FullName)}-{(s.Language == "en" ? "Resume" : "Curriculo")}.pdf");
        string html = "";
        var pages = 0;
        var trims = 0;
        PdfCheck check;
        while (true)
        {
            html = Template.Render(draft, s, s.Language, config.GetValue("Materials:Theme", "classico") ?? "classico");
            await automation.RenderPdfAsync(Template.Fill(html, profile.Identity), path, ct);
            check = await PdfCheck.RunAsync(path, profile.Identity, draft, s, s.Language, maxPages, ct);
            pages = check.Pages;
            if (pages <= maxPages || trims >= 10 || !Trim(ref draft)) break;
            trims++;
        }
        var sha = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path, ct)));
        var problems = check.Problems;
        if (problems.Count > 0)
        {
            await ai.NoteAsync(thread, "Inspetor ATS", "O PDF não passou na leitura como ATS", string.Join("\n", problems), "Supervisor", false, ct);
            return new ComposeResult(false, null, null, null, s.Language, mode, reviewed, string.Join("; ", problems), false, thread.Id);
        }
        await ai.NoteAsync(thread, "Renderizador",
            $"PDF com {pages} {(pages == 1 ? "página" : "páginas")}: {Path.GetFileName(path)}",
            $"{(trims > 0 ? $"Cortei {trims} conquista(s) menos relevante(s) para caber em {maxPages} páginas.\n" : "")}Hash {sha[..12]}. Esse hash é o que o Guardião confere antes de qualquer envio.",
            "Inspetor ATS", true, ct);
        await ai.NoteAsync(thread, "Inspetor ATS", $"Leitura como ATS: {check.Score}% ({check.Items.Count(i => i.Passed)} de {check.Items.Count} itens)",
            string.Join("\n", check.Items.Select(i => $"{(i.Passed ? "ok" : i.Critical ? "REPROVADO" : "atenção")}: {i.Name} ({i.Detail})")), "Guardião", check.Problems.Count == 0, ct);

        var version = ResumeVersion.Create(r.ApplicationId, r.JobId, profile, s.Language, mode, by, reviewer, reviewed, html, path, sha, pages,
            checkedDraft.Lines, thread.Id, time.GetUtcNow().UtcDateTime);
        db.Resumes.Add(version);
        await db.SaveChangesAsync(ct);
        log.LogInformation("Currículo {Version} para {Application}: {Mode}, {Pages} páginas, revisado {Reviewed}", version.Id, r.ApplicationId, mode, pages, reviewed);
        return new ComposeResult(true, version.Id, path, sha, s.Language, mode, reviewed, null, false, thread.Id);
    }

    /// <summary>
    /// Currículo base: sem vaga e sem IA, montado dos fatos do perfil (as experiências com as conquistas, as competências, formação, idiomas
    /// e certificados) no tema e no idioma escolhidos, conferido pelo Conferente e pelo Inspetor ATS como os das vagas. Fica no app como
    /// rascunho; nada é enviado.
    /// </summary>
    public async Task<ComposeResult> ComposeBaseAsync(string language, string theme, CancellationToken ct)
    {
        var profile = await profiles.CurrentAsync(ct);
        if (profile is null || profile.Facts.Count == 0) return Fail("Nenhum perfil. Envie o seu currículo ou conecte o LinkedIn na tela Perfil.");
        var lang = language == "en" ? "en" : "pt";
        var style = Template.Themes.Any(t => t.Key == theme) ? theme : "classico";
        // A "vaga" do currículo base é o próprio perfil: o título e todas as competências e tecnologias, para o Seletor pegar o que é da pessoa.
        var own = new JobText
        {
            Title = profile.Facts.FirstOrDefault(f => f.Kind == "headline")?.Statement ?? "",
            Description = string.Join(", ", profile.Facts.Where(f => f.Kind == "skill").Select(f => f.Statement).Concat(profile.Facts.SelectMany(f => f.Technologies)).Distinct()),
        };
        var s = Selector.Pick(profile, own) with { Language = lang };
        var thread = AgentThread.New("resume", "Currículo base");
        var checkedDraft = Conferente.Check(Draft.Plain(s), s);
        var draft = Tailor.Finish(checkedDraft.Draft, s);
        var maxPages = config.GetValue("Materials:MaxPages", 2);
        var dir = AppPaths.In(AppPaths.Data, "materials", profile.ProfileId.ToString("N"), "base-" + time.GetUtcNow().ToString("yyyyMMddHHmmss"));
        var path = Path.Combine(dir, $"{FileName(profile.Identity.FullName)}-{(lang == "en" ? "Resume" : "Curriculo")}.pdf");
        string html;
        PdfCheck check;
        var trims = 0;
        while (true)
        {
            html = Template.Render(draft, s, lang, style);
            await automation.RenderPdfAsync(Template.Fill(html, profile.Identity), path, ct);
            check = await PdfCheck.RunAsync(path, profile.Identity, draft, s, lang, maxPages, ct);
            if (check.Pages <= maxPages || trims >= 10 || !Trim(ref draft)) break;
            trims++;
        }
        if (check.Problems.Count > 0)
        {
            await ai.NoteAsync(thread, "Inspetor ATS", "O PDF não passou na leitura como ATS", string.Join("\n", check.Problems), "Supervisor", false, ct);
            return new ComposeResult(false, null, null, null, lang, "base", false, string.Join("; ", check.Problems), false, thread.Id);
        }
        var sha = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path, ct)));
        await ai.NoteAsync(thread, "Renderizador", $"Currículo base, tema {Template.Themes.First(t => t.Key == style).Name}: {check.Pages} {(check.Pages == 1 ? "página" : "páginas")}",
            $"Leitura como ATS: {check.Score}%. Sem vaga e sem IA: só os fatos do perfil.", "Você", true, ct);
        var version = ResumeVersion.Create(profile.ProfileId, Guid.Empty, profile, lang, "base", $"regras, tema {style}", null, false, html, path, sha, check.Pages,
            checkedDraft.Lines, thread.Id, time.GetUtcNow().UtcDateTime);
        db.Resumes.Add(version);
        await db.SaveChangesAsync(ct);
        return new ComposeResult(true, version.Id, path, sha, lang, "base", false, null, false, thread.Id);
    }

    /// <summary>Tira a última conquista da experiência com mais conquistas (mínimo de duas em cada).</summary>
    private static bool Trim(ref Draft d)
    {
        var longest = d.Experiences.Select((e, i) => (e, i)).Where(x => x.e.Bullets.Length > 2).OrderByDescending(x => x.e.Bullets.Length).ThenByDescending(x => x.i).FirstOrDefault();
        if (longest.e is null) return false;
        var list = d.Experiences.ToArray();
        list[longest.i] = longest.e with { Bullets = longest.e.Bullets[..^1] };
        d = d with { Experiences = list };
        return true;
    }

    /// <summary>Nome-Sobrenome sem acento nem espaço, para o arquivo.</summary>
    internal static string FileName(string fullName)
    {
        var parts = Claims.Fold(fullName).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => new string([.. p.Where(char.IsLetterOrDigit)])).Where(p => p.Length > 0)
            .Select(p => char.ToUpperInvariant(p[0]) + p[1..]);
        var name = string.Join('-', parts);
        return name.Length > 0 ? name : "Candidato";
    }

    private static ComposeResult Fail(string error) => new(false, null, null, null, "pt", "none", false, error, false, null);
    private static string Join(IEnumerable<string> xs) => xs.Any() ? string.Join(", ", xs.Distinct(StringComparer.OrdinalIgnoreCase)) : "nada";
    private static string Cut(string s, int max) => s.Length > max ? s[..max] : s;
}

/// <summary>Um item da inspeção ATS.</summary>
internal sealed record AtsItem(string Name, bool Passed, string Detail, bool Critical);

/// <summary>
/// Agente Inspetor ATS (sem IA): lê o PDF como um ATS lê (pdftotext, pdfinfo, pdffonts) e confere o que derruba currículo em
/// parser: texto real, nome no topo, contato no corpo, seções padrão em ordem, datas num formato só, palavras-chave da vaga que você
/// tem, fontes embutidas, nada de caractere estranho, até duas páginas e metadados preenchidos. Item crítico reprovado barra o envio.
/// </summary>
internal sealed record PdfCheck(int Pages, int Expected, int Found, List<string> Problems, List<AtsItem> Items)
{
    public int Score => Items.Count == 0 ? 0 : (int)Math.Round(100.0 * Items.Count(i => i.Passed) / Items.Count);

    public static async Task<PdfCheck> RunAsync(string path, Identity id, Draft d, Selection s, string language, int maxPages, CancellationToken ct)
    {
        // Com o poppler (Linux, em geral): pdftotext, pdfinfo e pdffonts. Sem ele (macOS e Windows): o PdfPig lê o mesmo PDF por dentro do app.
        var poppler = Platform.Which("pdftotext") is not null && Platform.Which("pdfinfo") is not null;
        string text, layout, title, fonts = "";
        int pages;
        if (poppler)
        {
            text = await Run("pdftotext", ["-enc", "UTF-8", path, "-"], ct);
            layout = await Run("pdftotext", ["-enc", "UTF-8", "-layout", path, "-"], ct);
            var info = await Run("pdfinfo", [path], ct);
            if (Platform.Which("pdffonts") is not null) fonts = await Run("pdffonts", [path], ct);
            string Info(string key) => info.Split('\n').Where(l => l.StartsWith(key + ":", StringComparison.Ordinal)).Select(l => l[(key.Length + 1)..].Trim()).FirstOrDefault() ?? "";
            pages = int.TryParse(Info("Pages"), out var n) ? n : 0;
            title = Info("Title");
        }
        else
        {
            using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
            pages = doc.NumberOfPages;
            title = doc.Information.Title ?? "";
            text = string.Join("\n", doc.GetPages().Select(p => UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(p)));
            layout = text;
        }
        var flat = Flat(text);
        var claims = d.Claims().Select(c => c.Claim.Text).ToList();
        var found = claims.Count(c => flat.Contains(Flat(c), StringComparison.Ordinal));
        var en = language == "en";
        var items = new List<AtsItem>();
        void Item(string name, bool ok, string detail, bool critical = false) => items.Add(new AtsItem(name, ok, detail, critical));

        Item("Texto real (não imagem)", text.Trim().Length > 400, $"{text.Trim().Length} caracteres extraídos", critical: true);
        var first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        Item("Nome na primeira linha", id.FullName.Length > 0 && Flat(first) == Flat(id.FullName), first.Length > 0 ? $"primeira linha: {first}" : "vazia", critical: true);
        var contact = new[] { id.Email, id.Phone }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        Item("Contato no corpo do PDF", contact.Count > 0 && contact.All(c => flat.Contains(Flat(c!), StringComparison.Ordinal)),
            contact.Count == 0 ? "sem e-mail nem telefone no perfil" : "e-mail e telefone no texto, fora de cabeçalho e rodapé", critical: true);
        string[] headings = en ? ["SUMMARY", "SKILLS", "EXPERIENCE", "EDUCATION"] : ["RESUMO", "COMPETENCIAS", "EXPERIENCIA", "FORMACAO"];
        var upper = Claims.Fold(text).ToUpperInvariant();
        var positions = headings.Select(h => upper.IndexOf(h, StringComparison.Ordinal)).ToList();
        var present = positions.Count(p => p >= 0);
        var ordered = positions.Where(p => p >= 0).SequenceEqual(positions.Where(p => p >= 0).Order());
        Item("Seções com nomes padrão, em ordem", present >= 3 && ordered, $"{present} de {headings.Length} seções padrão{(ordered ? "" : ", fora de ordem")}");
        var dateLines = d.Experiences.Length;
        var dates = System.Text.RegularExpressions.Regex.Matches(layout, @"(?i)\b(?:[a-z]{3} )?\d{4} - (?:(?:[a-z]{3} )?\d{4}|present|atual)\b").Count;
        Item("Datas num formato só (mês ano - mês ano)", dates >= dateLines, $"{dates} períodos no formato, {dateLines} experiências");
        var keywords = s.Matched.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var covered = keywords.Count(k => Claims.Fold(text).Contains(Claims.Fold(k), StringComparison.Ordinal));
        Item("Palavras-chave da vaga que você tem", covered == keywords.Count, keywords.Count == 0 ? "a vaga não cita tecnologia do seu perfil" : $"{covered} de {keywords.Count}: {string.Join(", ", keywords)}");
        if (fonts.Length > 0)
        {
            var embedded = fonts.Split('\n').Skip(2).Where(l => l.Trim().Length > 0).ToList();
            var notEmbedded = embedded.Count(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries) is var cols && cols.Length >= 5 && cols[^5] == "no");
            Item("Fontes embutidas", embedded.Count > 0 && notEmbedded == 0, $"{embedded.Count} fonte(s), {notEmbedded} sem embutir");
        }
        var odd = text.Where(c => char.IsSurrogate(c) || (c >= '\uE000' && c <= '\uF8FF') || c == '\uFFFD').Count();
        Item("Sem caractere estranho (ícone, emoji, glifo privado)", odd == 0, odd == 0 ? "nenhum" : $"{odd} caractere(s)");
        Item($"Até {maxPages} páginas", pages is > 0 && pages <= maxPages, $"{pages} página(s)", critical: pages == 0);
        Item("Título do PDF preenchido", title.Length > 0 && Flat(title).Contains(Flat(id.FullName), StringComparison.Ordinal), $"título: {title}");
        Item("Afirmações presentes no texto", found == claims.Count, $"{found} de {claims.Count}", critical: true);
        Item("Sem placeholder sobrando", !text.Contains("{{", StringComparison.Ordinal), "nenhum {{...}}", critical: true);

        var problems = items.Where(i => i.Critical && !i.Passed).Select(i => $"{i.Name}: {i.Detail}").ToList();
        return new PdfCheck(pages, claims.Count, found, problems, items);
    }

    /// <summary>Só letras e números, sem acento: quebra de linha e hifenização do PDF não atrapalham.</summary>
    private static string Flat(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in Claims.Fold(s)) if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    private static async Task<string> Run(string file, string[] args, CancellationToken ct)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var errors = p.StandardError.ReadToEndAsync(ct);
        var output = await p.StandardOutput.ReadToEndAsync(ct);
        await errors;
        await p.WaitForExitAsync(ct);
        return output;
    }
}

/// <summary>
/// O acabamento sem IA, depois do Conferente: as palavras da vaga que você tem vão primeiro nas competências (todas, na grafia do perfil,
/// que é a mesma que a vaga usa), o título do topo cabe numa linha, e experiência que ficou sem nenhum tópico ganha o próprio fato como
/// tópico, se ele estiver no idioma do currículo. Nada entra sem fato.
/// </summary>
internal static class Tailor
{
    public static Draft Finish(Draft d, Selection s)
    {
        var skills = s.Matched.Concat(d.Skills).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(18).ToArray();
        var headline = d.Headline is { } h ? h with { Text = Fit(h.Text, 64) } : null;
        var experiences = d.Experiences.Select(e =>
        {
            if (e.Bullets.Length > 0 || s.Get(e.Fact) is not { } parent) return e;
            var own = s.Children(e.Fact).Prepend(parent).Where(p => p.Fact.Statement.Length > 30 && Languages.Detect(p.Fact.Statement) == s.Language).Take(2);
            return e with { Bullets = [.. own.Select(p => new Claim(p.Fact.Statement, [p.Label]))] };
        }).ToArray();
        return d with { Skills = skills, Headline = headline, Experiences = experiences };
    }

    /// <summary>Título comprido corta no último " | " que cabe (um título que quebra em duas linhas parece improvisado e o ATS lê pela metade).</summary>
    internal static string Fit(string text, int max)
    {
        var t = text.Trim();
        if (t.Length <= max) return t;
        var parts = t.Split(" | ");
        var kept = new List<string>();
        foreach (var p in parts)
        {
            if (string.Join(" | ", kept.Append(p)).Length > max) break;
            kept.Add(p);
        }
        return kept.Count > 0 ? string.Join(" | ", kept) : t[..max].TrimEnd();
    }
}
