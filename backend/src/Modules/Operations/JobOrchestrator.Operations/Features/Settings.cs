namespace JobOrchestrator.Operations.Features;

internal sealed record PrivacySettings(bool LocalOnly, int RetentionDays);
internal sealed record WipeInput(string Confirm);

/// <summary>
/// Configurações e segurança: modo privado (só o modelo local, nada vai para a nuvem), retenção, exportar os seus dados em JSON,
/// revogar tudo (autorizações e envio automático de e-mail) e apagar tudo (com a frase digitada). Contas de IA e a campanha ficam.
/// </summary>
internal sealed class SettingsEndpoints : ICarterModule
{
    private const string Phrase = "APAGAR TUDO";

    /// <summary>What "Apagar tudo" keeps, as the screen says: AI accounts, the campaign and the settings (mail connections and templates).</summary>
    private static readonly string[] Kept = ["campaigns.campaigns", "campaigns.authorizations", "intelligence.accounts", "mail.accounts", "mail.settings", "mail.templates", "automation.adapter_stats"];
    private static readonly string[] Schemas = ["jobs", "campaigns", "applications", "automation", "intelligence", "profile", "materials", "mail"];

    /// <summary>
    /// Every table of the modules except <see cref="Kept"/>, read from the catalog: a fixed list missed applications.interviews,
    /// whose foreign key made the whole wipe fail. No cascade, so a kept table that points at wiped data fails instead of being emptied.
    /// </summary>
    internal static async Task<List<string>> WipedTablesAsync(NpgsqlConnection c) =>
        [.. await c.QueryAsync<string>("""
            select format('%I.%I', schemaname, tablename) from pg_tables
            where schemaname = any(@Schemas) and left(tablename, 2) <> '__' and schemaname || '.' || tablename <> all(@Kept)
            order by 1
            """, new { Schemas, Kept })];

    internal static async Task WipeAsync(NpgsqlConnection c)
    {
        var tables = await WipedTablesAsync(c);
        await using var tx = await c.BeginTransactionAsync();
        await c.ExecuteAsync($"truncate {string.Join(", ", tables)}", transaction: tx);
        await c.ExecuteAsync("""
            update campaigns.authorizations set revoked_at = now() where revoked_at is null;
            update campaigns.campaigns set mode = 'ReviewRequired' where mode = 'AutoSubmit';
            """, transaction: tx);
        await tx.CommitAsync();
    }

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/settings").WithTags("Operations");
        g.MapGet("", (IConfiguration config) => Results.Ok(new PrivacySettings(config.GetValue("Privacy:LocalOnly", false), config.GetValue("Retention:Days", 90))));
        // O conector MCP: o comando deste executável com --mcp, pronto para o Claude Code e o Claude Desktop.
        g.MapGet("mcp", () =>
        {
            var exe = Environment.ProcessPath is { } path && Path.GetFileNameWithoutExtension(path) == "job-orchestrator" ? path : "job-orchestrator";
            var shown = exe.Contains(' ') ? $"\"{exe}\"" : exe;
            var desktop = JsonSerializer.Serialize(new { mcpServers = new Dictionary<string, object> { ["job-orchestrator"] = new { command = exe, args = new[] { "--mcp" } } } },
                new JsonSerializerOptions { WriteIndented = true });
            return Results.Ok(new { command = exe, claudeCode = $"claude mcp add job-orchestrator -- {shown} --mcp", desktop });
        });
        g.MapPut("", async (PrivacySettings body) =>
        {
            await UserSettings.SetAsync(new Dictionary<string, object?> { ["Privacy:LocalOnly"] = body.LocalOnly, ["Retention:Days"] = Math.Clamp(body.RetentionDays, 7, 3650) });
            return Results.NoContent();
        });

        g.MapGet("export", async ([FromKeyedServices("operations")] NpgsqlDataSource data) =>
        {
            await using var c = await data.OpenConnectionAsync();
            async Task<JsonElement> Q(string sql) => JsonSerializer.Deserialize<JsonElement>(await c.ExecuteScalarAsync<string>($"select coalesce(json_agg(t), '[]')::text from ({sql}) t") ?? "[]");
            var export = new Dictionary<string, object>
            {
                ["exportadoEm"] = DateTime.UtcNow,
                ["perfil"] = await Q("select full_name, email, phone, location, linked_in, git_hub, website, confirmations, confirmed_at from profile.profiles"),
                ["fatos"] = await Q("select kind, statement, organization, role, start, \"end\", technologies, level, status from profile.facts"),
                ["campanhas"] = await Q("select name, mode, min_fit_score, max_submissions_per_day, technologies, titles, locations, sources from campaigns.campaigns"),
                ["vagas"] = await Q("select company, title, location, status, score, apply_url, first_seen_at from jobs.postings order by first_seen_at desc"),
                ["candidaturas"] = await Q("select company, title, state, score, apply_url, created_at, updated_at, receipt_id from applications.applications order by created_at desc"),
                ["historico"] = await Q("select application_id, \"from\", \"to\", reason, actor, at from applications.events order by at"),
                ["emailsEnviados"] = await Q("select company, job_title, \"to\", subject, body, status, sent_at from mail.outgoing order by created_at desc"),
                ["respostas"] = await Q("select \"from\", subject, class, date from mail.incoming order by date desc"),
            };
            return Results.File(JsonSerializer.SerializeToUtf8Bytes(export, new JsonSerializerOptions(JsonSerializerOptions.Web) { WriteIndented = true }), "application/json", $"job-orchestrator-{DateTime.Now:yyyy-MM-dd}.json");
        });

        g.MapPost("revoke", async ([FromKeyedServices("operations")] NpgsqlDataSource data) =>
        {
            await using var c = await data.OpenConnectionAsync();
            var auth = await c.ExecuteAsync("update campaigns.authorizations set revoked_at = now() where revoked_at is null");
            await c.ExecuteAsync("update campaigns.campaigns set mode = 'ReviewRequired' where mode = 'AutoSubmit'");
            var mail = await c.ExecuteAsync("update mail.settings set mode = 'Review', auto_consent_at = null where mode = 'Auto'");
            // What the automatic mode had released and has not left goes back to your approval, as when the mode is switched off on the Mail screen.
            await c.ExecuteAsync("update mail.outgoing set status = 'AwaitingApproval', approved_at = null, approved_by = null, error = @Note where status = 'Scheduled' and approved_by = 'auto'",
                new { Note = "O envio automático foi desligado depois que este e-mail foi liberado: ele espera a sua aprovação." });
            return Results.Ok(new { authorizations = auth, mail });
        });

        g.MapPost("wipe", async (WipeInput body, [FromKeyedServices("operations")] NpgsqlDataSource data, JobOrchestrator.Automation.Contracts.ILocalBrowserSessions browsers, ILogger<SettingsEndpoints> log, CancellationToken ct) =>
        {
            if (body.Confirm?.Trim() != Phrase) return Results.Problem($"Digite {Phrase} para confirmar.", statusCode: 400);
            await browsers.ClearAsync(ct);
            await using var c = await data.OpenConnectionAsync();
            await WipeAsync(c);
            foreach (var dir in new[] { "profile", "resumes", "materials", "reports" }.Select(d => Path.Combine(AppPaths.Data, d)).Where(Directory.Exists))
                try { Directory.Delete(dir, recursive: true); } catch (IOException ex) { log.LogWarning("Wipe left {Dir}: {Error}", dir, ex.Message); }
            log.LogWarning("Everything wiped by the user");
            return Results.NoContent();
        });
    }
}

/// <summary>No Worker, a cada seis horas: apaga o que passou da retenção (conversas dos agentes, chamadas de IA, cópias das vagas e cache vencido).</summary>
internal sealed class Retention([FromKeyedServices("operations")] NpgsqlDataSource data, IConfiguration config, ILogger<Retention> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stop);
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var days = Math.Clamp(config.GetValue("Retention:Days", 90), 7, 3650);
                await using var c = await data.OpenConnectionAsync(stop);
                var p = new { cut = DateTime.UtcNow.AddDays(-days) };
                var removed = await c.ExecuteAsync("delete from intelligence.messages where at < @cut", p)
                    + await c.ExecuteAsync("delete from intelligence.calls where at < @cut", p)
                    + await c.ExecuteAsync("delete from jobs.snapshots where fetched_at < @cut", p)
                    + await c.ExecuteAsync("delete from intelligence.cache where expires_at < now()");
                if (removed > 0) log.LogInformation("Retention removed {Rows} rows older than {Days} days", removed, days);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Retention failed: {Error}", ex.Message); }
            await Task.Delay(TimeSpan.FromHours(6), stop);
        }
    }
}
