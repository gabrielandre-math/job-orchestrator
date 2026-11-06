using System.Security.Cryptography;
using System.Text.Json;
using JobOrchestrator.Applications.Features;
using JobOrchestrator.Materials.Compose;

namespace JobOrchestrator.Tests.Unit;

public sealed class InterviewPreparationTests
{
    [Fact]
    public void Guia_so_inclui_fatos_da_entrada_e_vincula_a_versao()
    {
        var fact = new InterviewFact(Guid.NewGuid(), "Automatizei o deploy de uma API.");
        var resume = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new
        {
            questions = new[]
            {
                new { question = "Como era seu processo de deploy?", focus = "Ação e resultado", factIds = new[] { fact.Id, Guid.NewGuid() } },
                new { question = "Como era seu processo de deploy?", focus = "", factIds = Array.Empty<Guid>() }
            },
            askEmployer = new[] { "Como vocês acompanham as entregas?" }
        });
        var pack = Assert.IsType<InterviewPack>(Interviews.ParsePack(resume, [fact], json));
        Assert.Equal(resume, pack.ResumeVersionId);
        Assert.Equal(fact, Assert.Single(Assert.Single(pack.Questions).Evidence));
        Assert.Single(pack.AskEmployer);
        Assert.Null(Interviews.ParsePack(null, [], "{\"questions\":[null]}"));
    }

    [Fact]
    public async Task Arquivo_trocado_ou_ausente_nao_e_curriculo_confirmado()
    {
        var path = Path.GetTempFileName();
        try
        {
            var original = "versão original"u8.ToArray();
            var hash = Convert.ToHexStringLower(SHA256.HashData(original));
            await File.WriteAllBytesAsync(path, original);
            Assert.True(await ResumeComposer.MatchesArchiveAsync(path, hash, CancellationToken.None));
            await File.WriteAllTextAsync(path, "versão diferente");
            Assert.False(await ResumeComposer.MatchesArchiveAsync(path, hash, CancellationToken.None));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Contexto_do_curriculo_nao_inclui_cabecalho_tecnico_nem_placeholders()
    {
        var text = Template.InterviewText("<html><head><style>body { color: red }</style></head><body><h1>{{NOME}}</h1><p>{{CONTATO}}</p><li>APIs &amp; filas</li></body></html>");
        Assert.Equal("APIs & filas", text);
    }
}
