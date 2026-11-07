using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Npgsql;

namespace JobOrchestrator.Tests.EndToEnd;

/// <summary>
/// Sobe processos reais (ATS falso, API e Worker) contra um banco novo no Postgres de teste (infra/compose.test.yaml).
/// Matar o Worker aqui é matar o processo de verdade, como um kill -9.
/// </summary>
internal sealed class Stack : IAsyncDisposable
{
    private const string Admin = "Host=127.0.0.1;Port=5463;Username=jo;Password=jo-test;Database=jo_test";
    private readonly string _db = $"jo_e2e_{Guid.NewGuid():N}"[..20];
    private readonly string _dir = Directory.CreateTempSubdirectory("jo-e2e-").FullName;
    private readonly int _ats = FreePort();
    private readonly int _api = FreePort();
    private Process? _atsProcess, _apiProcess, _worker;
    private readonly Dictionary<string, string> _extra = [];

    /// <summary>The run's database, data folder and logs, for the night rehearsal's own checks.</summary>
    public string Database => _db;
    public string DataDir => Path.Combine(_dir, "data");
    public string Folder => _dir;
    public string Connection => $"Host=127.0.0.1;Port=5463;Username=jo;Password=jo-test;Database={_db}";

    public HttpClient Http { get; } = new() { Timeout = TimeSpan.FromSeconds(30) };
    public string Api => $"http://127.0.0.1:{_api}/api";
    public string Ats => $"http://127.0.0.1:{_ats}";

    public static async Task<Stack> StartAsync(string? crashAt = null, Dictionary<string, string>? extra = null)
    {
        var s = new Stack();
        foreach (var (k, v) in extra ?? []) s._extra[k] = v;
        await using (var c = new NpgsqlConnection(Admin))
        {
            await c.OpenAsync();
            await new NpgsqlCommand($"create database {s._db}", c).ExecuteNonQueryAsync();
        }
        s.StartAts();
        await s.StartApiAsync();
        s.StartWorker(crashAt);
        return s;
    }

    public void StartAts() => _atsProcess = Launch("Tools/JobOrchestrator.FakeAts", new()
    {
        ["FAKEATS_URL"] = Ats, ["FAKEATS_STATE"] = Path.Combine(_dir, "ats.json"), ["FAKEATS_SLOW_SECONDS"] = "12"
    });

    public async Task StartApiAsync()
    {
        _apiProcess = Launch("Bootstrappers/JobOrchestrator.Api", Env(new() { ["Urls"] = $"http://127.0.0.1:{_api}" }));
        await WaitAsync(async () => (await Http.GetAsync($"{Api}/health")).IsSuccessStatusCode, TimeSpan.FromSeconds(90), "API não subiu");
    }

    /// <summary>Kills a process like kill -9 ("api", "worker" or "ats"), for the night rehearsal's faults.</summary>
    public async Task KillAsync(string which)
    {
        var p = which switch { "api" => _apiProcess, "worker" => _worker, "ats" => _atsProcess, _ => throw new ArgumentException(which) };
        if (p is { HasExited: false }) { p.Kill(entireProcessTree: true); await p.WaitForExitAsync(); }
    }

    /// <summary>Resident memory of the API and the Worker, in MB.</summary>
    public (long Api, long Worker) Memory()
    {
        long Mb(Process? p) { if (p is not { HasExited: false }) return 0; p.Refresh(); return p.WorkingSet64 / 1048576; }
        return (Mb(_apiProcess), Mb(_worker));
    }

    public void StartWorker(string? crashAt = null) =>
        _worker = Launch("Bootstrappers/JobOrchestrator.Worker", Env(crashAt is null ? new() : new() { ["JO_CRASH_AT"] = crashAt }));

    public async Task WaitWorkerDeathAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        await _worker!.WaitForExitAsync(cts.Token);
    }

    public async Task<JsonElement> GetAsync(string path) => await Http.GetFromJsonAsync<JsonElement>($"{Api}{path}");

    public async Task<HttpStatusCode> PostAsync(string path, object? body = null) =>
        (await Http.PostAsJsonAsync($"{Api}{path}", body ?? new { })).StatusCode;

    public async Task<HttpStatusCode> PutAsync(string path, object body) => (await Http.PutAsJsonAsync($"{Api}{path}", body)).StatusCode;

    public async Task<int> SubmissionsAsync(int? job = null)
    {
        var all = await Http.GetFromJsonAsync<JsonElement>($"{Ats}/api/submissions");
        return all.EnumerateArray().Count(e => job is null || e.GetProperty("job").GetInt32() == job);
    }

    /// <summary>A candidatura de uma empresa, ou null.</summary>
    public async Task<JsonElement?> ApplicationAsync(string company)
    {
        foreach (var a in (await GetAsync("/applications")).EnumerateArray())
            if (a.GetProperty("company").GetString() == company) return a;
        return null;
    }

    public async Task<string?> StateAsync(string company) => (await ApplicationAsync(company))?.GetProperty("state").GetString();

    public async Task ConfigureAsync(string mode)
    {
        var campaign = (await GetAsync("/campaigns"))[0];
        Assert.Equal(HttpStatusCode.NoContent, await PutAsync($"/campaigns/{campaign.GetProperty("id").GetString()}", new
        {
            mode, minFitScore = 80, maxSubmissionsPerDay = 15,
            technologies = new[] { "C#", ".NET", "ASP.NET Core", "AWS", "PostgreSQL" },
            locations = new[] { "Brasil", "Remoto" },
            sources = campaign.GetProperty("sources").EnumerateArray().Select(x => x.GetString()).ToArray()
        }));
    }

    /// <summary>Troca as fontes da campanha (mantém modo e critérios do teste).</summary>
    public Task SourcesAsync(string mode, params string[] sources) => SourcesAsync(mode, 15, sources);

    /// <summary>The same, with the campaign's daily limit: the number is the person's, with no fixed ceiling.</summary>
    public async Task SourcesAsync(string mode, int perDay, params string[] sources)
    {
        var campaign = (await GetAsync("/campaigns"))[0];
        Assert.Equal(HttpStatusCode.NoContent, await PutAsync($"/campaigns/{campaign.GetProperty("id").GetString()}", new
        {
            mode, minFitScore = 80, maxSubmissionsPerDay = perDay, technologies = new[] { "C#", ".NET", "ASP.NET Core", "AWS", "PostgreSQL" },
            locations = new[] { "Brasil", "Remoto" }, sources
        }));
    }

    public async Task RunAsync()
    {
        var id = (await GetAsync("/campaigns"))[0].GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Accepted, await PostAsync($"/campaigns/{id}/runs"));
    }

    public async Task ApproveAsync(string company)
    {
        var id = (await ApplicationAsync(company))!.Value.GetProperty("id").GetString();
        var hash = (await GetAsync($"/applications/{id}")).GetProperty("payloadHash").GetString();
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync($"/applications/{id}/approve", new { payloadHash = hash }));
    }

    public async Task<string[]> HistoryAsync(string company)
    {
        var id = (await ApplicationAsync(company))!.Value.GetProperty("id").GetString();
        return [.. (await GetAsync($"/applications/{id}")).GetProperty("history").EnumerateArray().Select(h => h.GetProperty("to").GetString()!)];
    }

    public async Task WaitAsync(Func<Task<bool>> condition, TimeSpan timeout, string what)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            try { if (await condition()) return; }
            catch (HttpRequestException) { }
            await Task.Delay(500);
        }
        Assert.Fail($"Tempo esgotado: {what}");
    }

    private Dictionary<string, string> Env(Dictionary<string, string> extra)
    {
        var env = new Dictionary<string, string>
        {
            ["DOTNET_ENVIRONMENT"] = "Testing", ["ASPNETCORE_ENVIRONMENT"] = "Testing",
            ["ConnectionStrings__Default"] = $"Host=127.0.0.1;Port=5463;Username=jo;Password=jo-test;Database={_db}",
            ["JO_DATA_DIR"] = Path.Combine(_dir, "data"), ["JO_CONFIG_DIR"] = Path.Combine(_dir, "config"),
            ["Profile__Seed"] = Path.Combine(Repo, "config", "profile.example.json"),
            ["Llm__Disabled"] = "true",
            ["Automation__FakeAtsHosts__0"] = $"127.0.0.1:{_ats}", ["Automation__Browser__TimeoutSeconds"] = "5",
            ["Seed__Sources__0"] = $"greenhouse:{Ats}/boards/demo", ["Seed__Sources__1"] = $"greenhouse:{Ats}/boards/agregador",
            ["Seed__Keywords__0"] = "C#", ["Seed__Keywords__1"] = ".NET", ["Seed__Keywords__2"] = "ASP.NET Core", ["Seed__Keywords__3"] = "AWS", ["Seed__Keywords__4"] = "PostgreSQL",
            ["Workflow__ReconcileDelaySeconds"] = "1", ["Workflow__ReconcileRetrySeconds"] = "2",
            // O Explorador e a busca contínua saem para a internet: no teste ponta a ponta ficam desligados.
            ["Explorer__Enabled"] = "false", ["Search__IntervalMinutes"] = "1440",
            ["Logging__LogLevel__Default"] = "Warning"
        };
        foreach (var (k, v) in _extra) env[k] = v;
        foreach (var (k, v) in extra) env[k] = v;
        return env;
    }

    private Process Launch(string project, Dictionary<string, string> env)
    {
        var dir = Path.Combine(Repo, "backend", "src", project, "bin", "Debug", "net10.0");
        var info = new ProcessStartInfo("dotnet", Path.Combine(dir, Path.GetFileName(project) + ".dll"))
        {
            WorkingDirectory = dir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var (k, v) in env) info.Environment[k] = v;
        var log = Path.Combine(_dir, Path.GetFileName(project) + ".log");
        var p = Process.Start(info)!;
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) File.AppendAllText(log, e.Data + "\n"); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) File.AppendAllText(log, e.Data + "\n"); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    private static string Repo
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d is not null && !File.Exists(Path.Combine(d.FullName, "backend", "JobOrchestrator.slnx"))) d = d.Parent;
            return d?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
        }
    }

    private static int FreePort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        return ((IPEndPoint)l.LocalEndpoint).Port;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var p in new[] { _worker, _apiProcess, _atsProcess })
            if (p is { HasExited: false }) { p.Kill(entireProcessTree: true); await p.WaitForExitAsync(); }
        NpgsqlConnection.ClearAllPools();
        await using var c = new NpgsqlConnection(Admin);
        await c.OpenAsync();
        await new NpgsqlCommand($"drop database if exists {_db} with (force)", c).ExecuteNonQueryAsync();
        Http.Dispose();
    }
}
