using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace JobOrchestrator.Assistant.Features;

public sealed record AskRequest(Guid? ConversationId, string Message, bool Compare = false, string? Rung = null);
public sealed record AssistantAction(string Type, string Title, IReadOnlyList<string> Values, double? Number);
public sealed record AssistantAnswer(bool Ok, string Reply, IReadOnlyList<AssistantAction> Actions, string ProviderId, string Model, string Rung, double Seconds, string? EscalatedFrom, string? Error);
public sealed record AskResponse(Guid ConversationId, string Rung, string Why, IReadOnlyList<AssistantAnswer> Answers);
public sealed record ChatTurn(Guid Id, DateTime At, string Role, string Text, string? Data);
public sealed record ConversationView(Guid Id, string Subject, DateTime StartedAt, DateTime LastAt, int Messages);
public sealed record ProposedSource(string Source, string Why);
public sealed record CoverLetter(bool Ok, string Text, string Language, string Model, IReadOnlyList<string> Removed, string? Error);
public sealed record SetupProposal(bool Ok, string? Error, string Area, string Summary, IReadOnlyList<string> Titles, IReadOnlyList<string> Keywords, IReadOnlyList<string> Locations,
    bool Remote, string Seniority, int MinFitScore, IReadOnlyList<ProposedSource> Sources, string ProviderId, string Model, bool FromAi);

/// <summary>
/// O assistente: uma triagem no modelo local diz o quanto a pergunta é difícil e escolhe o degrau (local, leve, médio, forte);
/// a resposta que vem insegura sobe um degrau. No modo comparar, a mesma pergunta vai a famílias diferentes ao mesmo tempo.
/// Ações saem só de uma lista fechada e só acontecem quando a pessoa clica em Aplicar.
/// </summary>
internal sealed partial class AssistantService(IIntelligenceApi ai, IServiceScopeFactory scopes, IProfileApi profile, ICampaignsApi campaigns, IJobsApi jobs, ILogger<AssistantService> log)
{
    private static readonly string[] ActionTypes = ["campaign.keywords", "campaign.locations", "campaign.minScore", "campaign.mode", "sources.add", "navigate"];
    private static readonly string[] Paths = ["/perfil", "/vagas", "/candidaturas", "/busca", "/fontes", "/fontes/varredura", "/campanha", "/provedores", "/revisao", "/atencao", "/"];
    private static readonly string[] Modes = ["DiscoveryOnly", "PrepareOnly", "ReviewRequired"];

    private const string ChatSchema = """
        {"type":"object","properties":{"reply":{"type":"string"},"confidence":{"type":"number"},"escalate":{"type":"boolean"},
        "actions":{"type":"array","items":{"type":"object","properties":{"type":{"type":"string","enum":["campaign.keywords","campaign.locations","campaign.minScore","campaign.mode","sources.add","navigate"]},
        "title":{"type":"string"},"values":{"type":"array","items":{"type":"string"}},"number":{"type":"number"}},"required":["type","title"]}}},
        "required":["reply","confidence","escalate","actions"]}
        """;
    private const string TriageSchema = """{"type":"object","properties":{"complexity":{"type":"integer","minimum":1,"maximum":4},"reason":{"type":"string"}},"required":["complexity","reason"]}""";
    private const string SetupSchema = """
        {"type":"object","properties":{"area":{"type":"string"},"summary":{"type":"string"},"titles":{"type":"array","items":{"type":"string"}},
        "keywords":{"type":"array","items":{"type":"string"}},"locations":{"type":"array","items":{"type":"string"}},"remote":{"type":"boolean"},
        "seniority":{"type":"string"},"minFitScore":{"type":"integer"},
        "sources":{"type":"array","items":{"type":"object","properties":{"source":{"type":"string"},"why":{"type":"string"}},"required":["source","why"]}}},
        "required":["area","summary","titles","keywords","locations","remote","seniority","minFitScore","sources"]}
        """;

    private const string SourceFormats = """
        Fontes no formato plataforma:alvo. Empresas em sistema de vagas (alvo = nome da empresa como aparece no endereço): greenhouse, lever, ashby,
        smartrecruiters, recruitee, breezy, personio e gupy (gupy cobre empresas brasileiras de todas as áreas: gupy:ambev, gupy:natura e afins). APIs públicas: remotive, arbeitnow e himalayas (alvo = palavra de busca), jobicy e remoteok (alvo = tag
        em inglês), themuse (alvo = categoria em inglês, como Healthcare, Sales, Marketing, Education, Software Engineering, Design and UX, Accounting and Finance,
        Customer Service, Human Resources and Recruitment, Project Management, Administration and Office, Law, Retail), wwr (alvo = categoria, como
        remote-sales-and-marketing-jobs, remote-design-jobs, remote-customer-support-jobs, remote-programming-jobs, all-other-remote-jobs). Comunidades: github
        (alvo = repositório de vagas, como backend-br/vagas ou frontendbr/vagas) e hn:whoishiring. Sites de carreira: web:dominio (inclusive empresa.gupy.io).
        """;

    private const string ChatSystem = $"""
        Você é o Assistente do Job Orchestrator, um app pessoal e local que ajuda uma pessoa de qualquer área a achar vagas, adaptar o currículo para ATS e
        acompanhar candidaturas. Responda em português do Brasil, direto, em frases curtas e com passos práticos; use listas quando ajudar.
        Sobre a pessoa, use só o que está no contexto: nunca invente experiência, número, empresa, formação ou idioma. Se faltar dado, diga o que falta e em
        qual tela do app a pessoa resolve. Você pode propor ações, que só acontecem quando a pessoa clicar em Aplicar:
        campaign.keywords (values: de 3 a 12 palavras-chave e competências da área da pessoa; as duas primeiras são as essenciais),
        campaign.locations (values: cidades e, se fizer sentido, Remoto), campaign.minScore (number de 50 a 95),
        campaign.mode (values: DiscoveryOnly, PrepareOnly ou ReviewRequired; o envio automático só liga pela tela de consentimento),
        sources.add (values: fontes; {SourceFormats}),
        navigate (values: um caminho entre /perfil, /vagas, /candidaturas, /busca, /fontes, /campanha, /provedores, /revisao, /atencao).
        Nunca diga que aplicou algo. LinkedIn não é automatizado: sugira o export de dados do LinkedIn para o perfil e o link salvo da vaga.
        confidence (0 a 1) diz o quanto você confia na resposta; escalate=true pede um modelo mais forte quando a pergunta exige análise que você não faz bem.
        """;

    private const string TriageSystem = """
        Classifique a dificuldade da pergunta de uma pessoa que procura emprego, para escolher o modelo que responde.
        1: conversa simples, saudação ou dúvida sobre onde fica algo no app. 2: explicação curta, dica ou texto pequeno.
        3: análise do perfil ou de vagas, plano de busca, texto longo, carta ou revisão de currículo.
        4: estratégia com várias etapas, comparação crítica entre opções, negociação ou algo que exige raciocínio cuidadoso.
        Responda só o JSON.
        """;

    public async Task<AskResponse> AskAsync(AskRequest r, CancellationToken ct)
    {
        var question = r.Message.Trim();
        if (question.Length > 6000) question = question[..6000];
        var thread = r.ConversationId is { } id ? new AgentThread(id, "assistant", Subject(question)) : AgentThread.New("assistant", Subject(question));
        var history = r.ConversationId is null ? [] : await HistoryAsync(thread.Id, ct);
        await ai.NoteAsync(thread, "Você", Subject(question), question, new { role = "user" }, "Assistente", true, ct);

        var providers = await ai.ProvidersAsync(ct);
        var cloud = providers.Where(p => p.Available && p.Family != "local").ToList();
        var local = providers.Any(p => p.Available && p.Family == "local");
        Rung[] ladder = cloud.Count > 0 ? (local ? [Rung.Local, Rung.Light, Rung.Medium, Rung.Strong] : [Rung.Light, Rung.Medium, Rung.Strong]) : [Rung.Local, Rung.LocalStrong];
        var (start, why) = r.Rung is { } forced && Enum.TryParse<Rung>(forced, true, out var f) ? (f, "degrau escolhido por você") : await TriageAsync(question, ladder, local, thread, ct);
        var prompt = Prompt(await ContextAsync(providers, ct), history, question);

        if (r.Compare && cloud.Count + (local ? 1 : 0) > 1)
        {
            // Lado a lado: cada família responde no próprio escopo (o DbContext do roteador não é compartilhável entre chamadas paralelas).
            var rung = start < Rung.Light && cloud.Count > 0 ? Rung.Light : start;
            var families = cloud.Select(p => p.Family).Distinct().Take(2).ToList();
            var runs = families.Select(fam => Scoped(prompt, rung, families.Count > 1 ? families.First(x => x != fam) : null, thread, ct)).ToList();
            if (local) runs.Add(Scoped(prompt, Rung.Local, null, thread, ct));
            var answers = (await Task.WhenAll(runs)).Select(x => x.Answer).ToList();
            foreach (var a in answers.Where(a => a.Ok)) await SayAsync(thread, a, compare: true, ct);
            return new AskResponse(thread.Id, rung.ToString(), "comparando famílias diferentes ao mesmo tempo", answers);
        }

        var i = Math.Max(0, Array.IndexOf(ladder, start));
        AssistantAnswer? best = null;
        string? from = null;
        for (var tries = 0; i < ladder.Length && tries < 3; i++, tries++)
        {
            var (answer, escalate, confidence) = await AnswerAsync(ai, prompt, ladder[i], null, thread, from, ct);
            if (!answer.Ok) { best ??= answer; from ??= ladder[i].ToString(); continue; }
            best = answer;
            if ((escalate || confidence < 0.45) && i + 1 < ladder.Length)
            {
                await ai.NoteAsync(thread, "Roteador", $"Subindo de {Label(ladder[i])} para {Label(ladder[i + 1])}", escalate ? "O modelo pediu ajuda de um mais forte." : $"Confiança baixa ({confidence:0.00}).",
                    new { role = "route", from = ladder[i].ToString(), to = ladder[i + 1].ToString() }, "Assistente", true, ct);
                from = ladder[i].ToString();
                continue;
            }
            break;
        }
        best ??= new AssistantAnswer(false, "", [], "nenhum", "", start.ToString(), 0, null, "Nenhum modelo respondeu. Confira as contas em Provedores de IA.");
        if (best.Ok) await SayAsync(thread, best, compare: false, ct);
        else await ai.NoteAsync(thread, "Assistente", "Não consegui responder", best.Error, new { role = "assistant", error = best.Error }, "Você", false, ct);
        return new AskResponse(thread.Id, best.Rung, why, [best]);
    }

    private async Task<(AssistantAnswer Answer, bool Escalate, double Confidence)> Scoped(string prompt, Rung rung, string? avoid, AgentThread thread, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await AnswerAsync(scope.ServiceProvider.GetRequiredService<IIntelligenceApi>(), prompt, rung, avoid, thread, null, ct);
    }

    private static async Task<(AssistantAnswer, bool, double)> AnswerAsync(IIntelligenceApi api, string prompt, Rung rung, string? avoid, AgentThread thread, string? from, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var res = await api.CompleteAsync(new LlmRequest(LlmTasks.AssistantChat.Name, ChatSystem, prompt, ChatSchema, rung, avoid, 150, false, thread, "Assistente"), ct);
        if (!res.Ok || res.Json is null)
            return (new AssistantAnswer(false, "", [], res.ProviderId, res.Model, rung.ToString(), clock.Elapsed.TotalSeconds, from, res.Error ?? "sem resposta"), false, 0);
        try
        {
            using var doc = JsonDocument.Parse(res.Json);
            var root = doc.RootElement;
            var reply = Unescape(root.TryGetProperty("reply", out var t) ? t.GetString() ?? "" : "");
            var confidence = root.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : 0.7;
            var escalate = root.TryGetProperty("escalate", out var e) && e.ValueKind == JsonValueKind.True;
            var actions = root.TryGetProperty("actions", out var a) && a.ValueKind == JsonValueKind.Array ? Actions(a) : [];
            return (new AssistantAnswer(reply.Length > 0, reply, actions, res.ProviderId, res.Model, rung.ToString(), Math.Round(res.Seconds > 0 ? res.Seconds : clock.Elapsed.TotalSeconds, 1), from,
                reply.Length > 0 ? null : "resposta vazia"), escalate, confidence);
        }
        catch (JsonException) { return (new AssistantAnswer(false, "", [], res.ProviderId, res.Model, rung.ToString(), clock.Elapsed.TotalSeconds, from, "resposta fora do formato"), false, 0); }
    }

    /// <summary>Só ações da lista, com valores limpos: caminho conhecido, fonte no formato aceito, modo sem envio automático, nota entre 50 e 95.</summary>
    private static List<AssistantAction> Actions(JsonElement list)
    {
        var result = new List<AssistantAction>();
        foreach (var x in list.EnumerateArray().Take(6))
        {
            var type = x.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            if (!ActionTypes.Contains(type)) continue;
            var title = x.TryGetProperty("title", out var ti) ? ti.GetString() ?? type : type;
            var values = x.TryGetProperty("values", out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Select(y => y.ValueKind == JsonValueKind.String ? y.GetString()!.Trim() : y.ToString()).Where(y => y.Length > 0).Distinct().Take(15).ToList() : [];
            double? number = x.TryGetProperty("number", out var n) && n.ValueKind == JsonValueKind.Number ? n.GetDouble() : null;
            switch (type)
            {
                case "navigate": values = values.Where(Paths.Contains).Take(1).ToList(); if (values.Count == 0) continue; break;
                case "sources.add": values = values.Where(s => SourceFormat().IsMatch(s)).ToList(); if (values.Count == 0) continue; break;
                case "campaign.mode": values = values.Where(Modes.Contains).Take(1).ToList(); if (values.Count == 0) continue; break;
                case "campaign.minScore": if (number is null) continue; number = Math.Clamp(Math.Round(number.Value), 50, 95); break;
                default: if (values.Count == 0) continue; break;
            }
            result.Add(new AssistantAction(type, title.Length > 120 ? title[..120] : title, values, number));
        }
        return result;
    }

    private Task SayAsync(AgentThread thread, AssistantAnswer a, bool compare, CancellationToken ct) =>
        ai.NoteAsync(thread, "Assistente", Subject(a.Reply), a.Reply,
            new { role = "assistant", actions = a.Actions, providerId = a.ProviderId, model = a.Model, rung = a.Rung, escalatedFrom = a.EscalatedFrom, seconds = a.Seconds, compare }, "Você", true, ct);

    private async Task<(Rung, string)> TriageAsync(string q, Rung[] ladder, bool local, AgentThread thread, CancellationToken ct)
    {
        var guess = Heuristic(q);
        if (!local) return (Pick(ladder, guess), "pela forma da pergunta");
        var res = await ai.CompleteAsync(new LlmRequest(LlmTasks.AssistantTriage.Name, TriageSystem, q, TriageSchema, Rung.Local, null, 40, false, thread, "Roteador"), ct);
        if (!res.Ok || res.Json is null) return (Pick(ladder, guess), "pela forma da pergunta");
        try
        {
            using var d = JsonDocument.Parse(res.Json);
            var level = d.RootElement.TryGetProperty("complexity", out var c) && c.TryGetInt32(out var n) ? Math.Clamp(n, 1, 4) : guess;
            var reason = d.RootElement.TryGetProperty("reason", out var w) ? w.GetString() ?? "" : "";
            // O modelo local tende a achar tudo simples: a forma da pergunta segura um degrau de baixo.
            return (Pick(ladder, Math.Max(level, guess - 1)), reason.Length > 0 ? reason : "classificada pelo modelo local");
        }
        catch (JsonException) { return (Pick(ladder, guess), "pela forma da pergunta"); }
    }

    private static int Heuristic(string q)
    {
        var level = 1;
        if (q.Length > 140) level++;
        if (q.Length > 600) level++;
        if (Hard().IsMatch(q)) level++;
        if (q.Count(ch => ch == '?') > 2 || q.Split('\n').Length > 4) level++;
        return Math.Clamp(level, 1, 4);
    }

    private static Rung Pick(Rung[] ladder, int level) => ladder[Math.Clamp(level - 1, 0, ladder.Length - 1)];
    private static string Label(Rung r) => r switch { Rung.Local => "modelo local", Rung.LocalStrong => "modelo local forte", Rung.Light => "degrau leve", Rung.Medium => "degrau médio", Rung.Strong => "degrau forte", _ => r.ToString() };

    private async Task<IReadOnlyList<ChatTurn>> HistoryAsync(Guid id, CancellationToken ct) =>
        [.. (await ConversationAsync(id, ct)).TakeLast(8)];

    public async Task<IReadOnlyList<ChatTurn>> ConversationAsync(Guid id, CancellationToken ct) =>
        [.. (await ai.ThreadAsync(id, ct)).Where(m => m.Kind == "note" && (m.Agent == "Você" || m.Agent == "Assistente" || m.Agent == "Roteador"))
            .Select(m => new ChatTurn(m.Id, m.At, m.Agent == "Você" ? "user" : m.Agent == "Roteador" ? "route" : "assistant", m.Body ?? m.Title, m.Data))];

    public async Task<IReadOnlyList<ConversationView>> ConversationsAsync(CancellationToken ct) =>
        [.. (await ai.ThreadsAsync(100, ct)).Where(t => t.Kind == "assistant").Select(t => new ConversationView(t.Id, t.Subject, t.StartedAt, t.LastAt, t.Messages))];

    private static string Prompt(string context, IReadOnlyList<ChatTurn> history, string question)
    {
        var sb = new StringBuilder("Contexto do app agora:\n").AppendLine(context);
        if (history.Count > 0)
        {
            sb.AppendLine("Conversa até aqui:");
            foreach (var h in history.Where(h => h.Role != "route")) sb.AppendLine($"{(h.Role == "user" ? "Pessoa" : "Assistente")}: {(h.Text.Length > 1200 ? h.Text[..1200] + "…" : h.Text)}");
        }
        return sb.AppendLine().Append("Pergunta: ").Append(question).ToString();
    }

    /// <summary>O que o modelo sabe: fatos verificados do currículo (sem contato), campanha, números e contas. Nada de e-mail, telefone ou link.</summary>
    private async Task<string> ContextAsync(IReadOnlyList<ProviderView> providers, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var p = await profile.CurrentAsync(ct);
        if (p is null || p.Facts.Count == 0) sb.AppendLine("Perfil: vazio. A pessoa ainda não enviou o currículo (tela Perfil ou primeiro contato).");
        else
        {
            sb.AppendLine($"Perfil: {p.Facts.Count} fatos verificados no currículo{(p.Confirmed ? ", confirmados pela pessoa" : ", ainda sem confirmação")}.");
            foreach (var fact in p.Facts.Take(70)) sb.AppendLine($"- {Fact(fact)}");
            if (p.Identity.Location is { Length: > 0 } where) sb.AppendLine($"Região: {where}");
            var c = p.Confirmations;
            if (c.Modalities.Count > 0) sb.AppendLine($"Modalidades aceitas: {string.Join(", ", c.Modalities)}");
            if (c.Cities.Count > 0) sb.AppendLine($"Cidades aceitas: {string.Join(", ", c.Cities)}{(c.Relocate ? "; aceita mudar de cidade" : "")}");
        }
        if ((await campaigns.ListAsync(ct)).FirstOrDefault() is { } camp)
            sb.AppendLine($"Campanha: modo {camp.Mode}, nota mínima {camp.MinFitScore}, palavras-chave [{string.Join(", ", camp.Technologies)}], locais [{string.Join(", ", camp.Locations)}], fontes [{string.Join(", ", camp.Sources)}].");
        else sb.AppendLine("Campanha: nenhuma ainda.");
        var counts = await jobs.CountsAsync(ct);
        sb.AppendLine($"Vagas: {counts.Total} lidas, {counts.Shortlisted} na fila, {counts.Rejected} cortadas.");
        sb.AppendLine($"Contas de IA prontas: {string.Join(", ", providers.Where(x => x.Available).Select(x => x.Name).DefaultIfEmpty("nenhuma"))}.");
        return sb.ToString();
    }

    private static string Fact(ProfileFact f)
    {
        var tech = f.Technologies.Count > 0 ? $" [ferramentas: {string.Join(", ", f.Technologies)}]" : "";
        return f.Kind switch
        {
            "experience" => $"experiência: {f.Role} em {f.Organization} ({f.Start ?? "?"} a {f.End ?? "atual"}). {f.Statement}{tech}",
            _ => $"{f.Kind}: {f.Statement}{(f.Level is { Length: > 0 } l ? $" ({l})" : "")}{tech}",
        };
    }

    /// <summary>
    /// Carta para o copiloto (candidatura pelo site original): a IA escreve só com os fatos verificados, o Conferente tira frase com termo
    /// ou número sem fonte e o nome entra aqui, depois da IA. Idioma da vaga.
    /// </summary>
    /// <summary>A carta da vaga no tom (direto, formal, caloroso) e no idioma pedidos; sem idioma, o da descrição da vaga.</summary>
    public async Task<CoverLetter> CoverAsync(Guid jobId, CancellationToken ct, string? tone = null, string? wanted = null)
    {
        var job = await jobs.TextAsync(jobId, ct);
        var p = await profile.CurrentAsync(ct);
        if (job is null || p is null || p.Facts.Count == 0) return new CoverLetter(false, "", "pt", "", [], "Falta o perfil ou a vaga.");
        var lower = job.Description.ToLowerInvariant();
        var lang = wanted is "pt" or "en" or "es" ? wanted : EnWords().Matches(lower).Count > PtWords().Matches(lower).Count ? "en" : "pt";
        var english = lang == "en";
        var facts = string.Join("\n", p.Facts.Where(f => f.Kind is "experience" or "skill" or "achievement" or "education" or "certification" or "language").Take(60).Select(f => $"- {Fact(f)}"));
        var system = lang switch
        {
            "en" => "Write a short cover letter (up to 180 words) in English for this job, using only the verified resume facts. Never invent numbers, companies, titles or results. Tie 2 or 3 facts to what the job asks. No em dashes. Do not write the candidate's name or contact details. Plain text.",
            "es" => "Escribe una carta de presentación breve (hasta 180 palabras) en español para esta vacante, usando solo los hechos verificados del currículum. Nunca inventes números, empresas, cargos ni resultados. Relaciona 2 o 3 hechos con lo que pide la vacante. Sin rayas. No escribas el nombre ni los datos de contacto de la persona. Texto simple.",
            _ => "Escreva uma carta de apresentação curta (até 180 palavras) em português do Brasil para esta vaga, usando só os fatos verificados do currículo. Nunca invente número, empresa, cargo ou resultado. Ligue 2 ou 3 fatos ao que a vaga pede. Sem travessão. Não escreva o nome nem o contato da pessoa. Texto simples.",
        } + (tone, lang) switch
        {
            ("formal", "en") => " Tone: formal and polite.", ("formal", "es") => " Tono: formal y cortés.", ("formal", _) => " Tom: formal e cordial.",
            ("caloroso", "en") => " Tone: warm and genuinely interested, without clichés.", ("caloroso", "es") => " Tono: cálido y con interés genuino, sin clichés.",
            ("caloroso", _) => " Tom: caloroso e com interesse genuíno, sem clichês.",
            (_, "en") => " Tone: direct and concise.", (_, "es") => " Tono: directo y conciso.", _ => " Tom: direto e objetivo.",
        };
        var thread = AgentThread.New("cover", $"Carta para {job.Company}: {job.Title}");
        const string schema = """{"type":"object","properties":{"letter":{"type":"string"}},"required":["letter"],"additionalProperties":false}""";
        var prompt = $"Vaga: {job.Title} na {job.Company} ({job.Location}).\n{(job.Description.Length > 3500 ? job.Description[..3500] : job.Description)}\n\nFatos verificados:\n{facts}";
        var res = await ai.CompleteAsync(new LlmRequest(LlmTasks.CoverWrite.Name, system, prompt, schema, LlmTasks.CoverWrite.Rung, null, 180, true, thread, "Redator"), ct);
        var language = lang;
        if (!res.Ok || res.Json is null) return new CoverLetter(false, "", language, res.Model, [], res.Error ?? "sem resposta");
        string letter;
        try { using var d = JsonDocument.Parse(res.Json); letter = d.RootElement.GetProperty("letter").GetString() ?? ""; }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException) { return new CoverLetter(false, "", language, res.Model, [], "resposta fora do formato"); }
        var sources = p.Facts.Select(f => $"{f.Statement} {f.Role} {f.Organization} {string.Join(' ', f.Technologies)} {f.Level}").Append(job.Description).Append($"{job.Company} {job.Title} {job.Location}").ToList();
        var removed = new List<string>();
        var kept = letter.Split('\n').Select(line => string.Join(" ", SentenceEnd().Split(line).Where(x => x.Trim().Length > 0).Where(x =>
        {
            var bad = Claims.Unsupported(x, sources);
            if (bad.Count > 0) removed.Add($"\"{x.Trim()}\" ({string.Join(", ", bad)})");
            return bad.Count == 0;
        })));
        var text = Gaps().Replace(string.Join("\n", kept), "\n\n").Trim() + $"\n\n{p.Identity.FullName}";
        await ai.NoteAsync(thread, "Conferente", removed.Count == 0 ? "Cada afirmação tem fonte no currículo ou na vaga" : $"{removed.Count} frase(s) sem fonte saíram",
            removed.Count == 0 ? null : string.Join("\n", removed), new { stage = "check", removed = removed.Count }, "Você", removed.Count == 0, ct);
        return new CoverLetter(true, text, language, res.Model, removed, null);
    }

    [GeneratedRegex(@"\b(the|and|with|you|will|our)\b")] private static partial Regex EnWords();
    [GeneratedRegex(@"\b(de|com|para|você|nossa|vaga)\b")] private static partial Regex PtWords();
    [GeneratedRegex(@"(?<=[.!?])\s+")] private static partial Regex SentenceEnd();
    [GeneratedRegex(@"\n{3,}")] private static partial Regex Gaps();

    /// <summary>
    /// Alguns modelos, presos ao JSON da resposta, escapam duas vezes: o texto chega com "\n" literal em vez da quebra de linha, e o
    /// Markdown vira uma linha só na tela. Sem nenhuma quebra de verdade e com sequências literais, desfaz o escape extra.
    /// </summary>
    internal static string Unescape(string text)
    {
        if (text.Contains('\n') || !text.Contains("\\n", StringComparison.Ordinal)) return text;
        return text.Replace("\\r\\n", "\n", StringComparison.Ordinal).Replace("\\n", "\n", StringComparison.Ordinal).Replace("\\t", "\t", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal).Trim();
    }

    /// <summary>Primeiro contato: lê o perfil verificado e propõe a campanha inteira (área, cargos, palavras, locais, fontes). Sem IA, monta pelo próprio perfil.</summary>
    public async Task<SetupProposal> SetupAsync(CancellationToken ct)
    {
        var p = await profile.CurrentAsync(ct);
        if (p is null || p.Facts.Count == 0) return Fallback(null, "O perfil ainda está vazio: envie o currículo primeiro.");
        var providers = await ai.ProvidersAsync(ct);
        var cloud = providers.Any(x => x.Available && x.Family != "local");
        var thread = AgentThread.New("setup", "Primeiro contato: montar o ambiente");
        var prompt = new StringBuilder(await ContextAsync(providers, ct)).AppendLine().AppendLine("""
            Monte a campanha de busca de emprego desta pessoa, para qualquer área (saúde, vendas, educação, tecnologia, indústria, direito, etc.).
            area: a área profissional em poucas palavras. summary: duas frases dizendo o que a busca vai priorizar e por quê, citando fatos do perfil.
            titles: de 2 a 5 cargos-alvo coerentes com a experiência. keywords: de 5 a 12 palavras que aparecem nas vagas da área dela (competências,
            ferramentas, registros profissionais, especialidades); as duas primeiras são essenciais e a vaga precisa citar ao menos uma delas.
            locations: cidades da região da pessoa e Remoto quando fizer sentido. remote: se vale buscar remoto. seniority: júnior, pleno, sênior ou
            liderança, pelo tempo de experiência. minFitScore: de 65 a 85. sources: de 5 a 10 fontes que cubram a área e o país dela, cada uma com o porquê.
            """).AppendLine(SourceFormats).AppendLine("Para empresas em sistema de vagas, use só nomes que você tem certeza que aparecem no endereço. Não invente fatos.").ToString();
        await ai.NoteAsync(thread, "Assistente", "Lendo o perfil para montar a campanha", $"{p.Facts.Count} fatos verificados", new { role = "setup" }, "Roteador", true, ct);
        var res = await ai.CompleteAsync(new LlmRequest(LlmTasks.AssistantSetup.Name, ChatSystem, prompt, SetupSchema, cloud ? Rung.Medium : Rung.LocalStrong, null, 180, false, thread, "Assistente"), ct);
        if (!res.Ok || res.Json is null) return Fallback(p, res.Error ?? "sem resposta");
        try
        {
            using var doc = JsonDocument.Parse(res.Json);
            var r = doc.RootElement;
            List<string> Strings(string k, int max) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array
                ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(max).ToList() : [];
            var sources = r.TryGetProperty("sources", out var s) && s.ValueKind == JsonValueKind.Array
                ? s.EnumerateArray().Select(x => new ProposedSource(x.TryGetProperty("source", out var a) ? a.GetString()?.Trim() ?? "" : "", x.TryGetProperty("why", out var w) ? w.GetString() ?? "" : ""))
                    .Where(x => SourceFormat().IsMatch(x.Source)).DistinctBy(x => x.Source).Take(12).ToList() : [];
            var keywords = Strings("keywords", 12);
            if (keywords.Count == 0) return Fallback(p, "a proposta veio sem palavras-chave");
            var min = r.TryGetProperty("minFitScore", out var m) && m.TryGetInt32(out var mv) ? Math.Clamp(mv, 50, 90) : 75;
            return new SetupProposal(true, null, r.TryGetProperty("area", out var ar) ? ar.GetString() ?? "" : "", r.TryGetProperty("summary", out var su) ? su.GetString() ?? "" : "",
                Strings("titles", 5), keywords, Strings("locations", 8), r.TryGetProperty("remote", out var re) && re.ValueKind == JsonValueKind.True,
                r.TryGetProperty("seniority", out var se) ? se.GetString() ?? "" : "", min, sources, res.ProviderId, res.Model, true);
        }
        catch (JsonException) { return Fallback(p, "a proposta veio fora do formato"); }
    }

    /// <summary>Sem IA: palavras pelas ferramentas e competências mais citadas, cargos pelas últimas experiências e fontes abertas de todas as áreas.</summary>
    private SetupProposal Fallback(ProfileSnapshot? p, string why)
    {
        log.LogInformation("Setup proposal fallback: {Why}", why);
        if (p is null) return new SetupProposal(false, why, "", "", [], [], [], true, "", 75, [], "nenhum", "", false);
        var words = p.Facts.SelectMany(f => f.Kind == "skill" ? [f.Statement] : f.Technologies).Where(w => w.Length is > 1 and < 40)
            .GroupBy(w => w, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key).Take(10).ToList();
        var titles = p.Facts.Where(f => f.Kind == "experience" && f.Role is { Length: > 0 }).Select(f => f.Role!).Distinct().Take(3).ToList();
        var places = new List<string>(p.Confirmations.Cities);
        if (p.Confirmations.Modalities.Any(x => x.Contains("remot", StringComparison.OrdinalIgnoreCase)) || places.Count == 0) places.Add("Remoto");
        var term = words.FirstOrDefault() ?? titles.FirstOrDefault() ?? "";
        List<ProposedSource> sources = term.Length == 0 ? [] :
            [new($"remotive:{term}", "vagas remotas de várias áreas"), new($"himalayas:{term}", "vagas remotas no mundo todo"), new($"arbeitnow:{term}", "Europa e remoto")];
        return new SetupProposal(true, why, titles.FirstOrDefault() ?? "", "Montado pelo próprio perfil, sem IA: palavras pelas ferramentas e competências mais citadas no currículo.",
            titles, words, places.Distinct().ToList(), true, "", 75, sources, "perfil", "", false);
    }

    private static string Subject(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        return line.Length > 90 ? line[..90] + "…" : line;
    }

    [GeneratedRegex(@"^(greenhouse|lever|ashby|smartrecruiters|recruitee|breezy|personio|workable|teamtailor|solides|inhire|quickin|pandape|workday|oracle|successfactors|rss|4dayweek|workingnomads|gupy|getonbrd|remotive|arbeitnow|himalayas|jobicy|themuse|remoteok|wwr|github|hn|web):[^\s]{2,120}$")]
    private static partial Regex SourceFormat();

    [GeneratedRegex(@"analis|compar|estrat|plano|revis|melhor|por que|porque|como (posso|devo|fa[çc]o)|carta|curr[ií]culo|entrevista|negoci|sal[aá]ri|transi[çc][aã]o|carreira", RegexOptions.IgnoreCase)]
    private static partial Regex Hard();
}
