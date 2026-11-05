using JobOrchestrator.Assistant.Features;
using JobOrchestrator.Intelligence.Providers;
using JobOrchestrator.Materials.Compose;
using JobOrchestrator.Profile.Contracts;

namespace JobOrchestrator.Tests.Unit;

/// <summary>O acabamento do currículo por vaga, o texto de falha do Codex e a resposta escapada duas vezes.</summary>
public sealed class TailorTests
{
    private static ProfileFact Fact(string kind, string statement, Guid? parent = null, params string[] tech) =>
        new(Guid.NewGuid(), kind, statement, parent, kind == "experience" ? "Acme" : null, kind == "experience" ? "Engenheiro de Software" : null, "2024-01", null, tech, null, "cv");

    [Fact]
    public void Job_keywords_the_candidate_has_lead_the_skills()
    {
        var exp = Fact("experience", "Engenheiro de software no time de pagamentos da Acme, com APIs em .NET e filas na AWS.", null, "C#", ".NET", "AWS");
        var s = new Selection([new Picked("F1", exp, null)], "pt", ["AWS", "C#"], ["Kafka"]);
        var d = new Draft(new Claim("Desenvolvedor Back-end .NET | C# | AWS | PostgreSQL | Terraform | Docker | React", ["F1"]), null,
            [new DraftExperience("F1", "Engenheiro de Software", [])], ["React", "C#", "Docker"], [], []);

        var done = Tailor.Finish(d, s);

        Assert.Equal(["AWS", "C#", "React", "Docker"], done.Skills);
        Assert.DoesNotContain("Kafka", done.Skills);
        Assert.True(done.Headline!.Text.Length <= 64);
        Assert.StartsWith("Desenvolvedor Back-end .NET | C# | AWS", done.Headline.Text);
        // A experiência que ficou sem tópico ganha o próprio fato (está no idioma do currículo).
        Assert.Single(done.Experiences[0].Bullets);
        Assert.Equal(["F1"], done.Experiences[0].Bullets[0].Facts);
    }

    [Fact]
    public void Headline_is_cut_at_a_separator()
    {
        Assert.Equal("Backend Engineer | C# | .NET", Tailor.Fit("Backend Engineer | C# | .NET | AWS | Microservices & Event-Driven Architecture", 30));
        Assert.Equal("Curto", Tailor.Fit("Curto", 64));
    }

    [Fact]
    public void Codex_failure_hides_the_banner_and_the_prompt()
    {
        const string prompt = "Você é o Assistente do Job Orchestrator.\n\nPergunta: monte a busca";
        var output = "OpenAI Codex v0.162.1 (research preview)\n--------\nworkdir: /tmp/x\nmodel: gpt-6.1-sol\nprovider: openai\nsession id: 01a\n--------\nuser\n" + prompt + "\nERROR: stream disconnected before completion\n";

        var reason = CodexCli.Failure(output, prompt);

        Assert.Contains("stream disconnected", reason);
        Assert.DoesNotContain("workdir", reason);
        Assert.DoesNotContain("Você é o Assistente", reason);
        Assert.Equal("O Codex terminou sem resposta e sem mensagem de erro.", CodexCli.Failure("OpenAI Codex v1\n--------\nmodel: x\n--------\nuser\n" + prompt, prompt));
    }

    [Theory]
    [InlineData("Linha 1\\n\\n## Título\\n- item", "Linha 1\n\n## Título\n- item")]
    [InlineData("Já tem\nquebra e um \\n literal", "Já tem\nquebra e um \\n literal")]
    [InlineData("Sem escape nenhum", "Sem escape nenhum")]
    public void Reply_escaped_twice_is_restored(string raw, string expected) => Assert.Equal(expected, AssistantService.Unescape(raw));

    [Fact]
    public void Ollama_package_matches_this_system()
    {
        var names = new[] { "ollama-linux-amd64.tgz", "ollama-linux-amd64.tar.zst", "ollama-linux-arm64.tgz", "ollama-linux-amd64-rocm.tgz", "ollama-darwin.tgz", "ollama-windows-amd64.zip", "ollama-windows-arm64.zip", "sha256sum.txt" };
        var picked = OllamaHost.Assets(names, amd: true);
        Assert.NotEmpty(picked);
        if (OperatingSystem.IsLinux() && System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.X64)
            Assert.Equal(["ollama-linux-amd64.tgz", "ollama-linux-amd64-rocm.tgz"], picked);
    }
}
