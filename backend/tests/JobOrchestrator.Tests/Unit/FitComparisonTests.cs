using System.Text.Json;
using JobOrchestrator.Jobs.Features;

namespace JobOrchestrator.Tests.Unit;

public sealed class FitComparisonTests
{
    [Fact]
    public void Cita_a_vaga_e_limita_as_evidencias_aos_fatos_fornecidos()
    {
        var fact = new FitEvidence(Guid.NewGuid(), "Construí APIs REST em .NET.");
        var description = "Experiência com APIs REST. Conhecimento de filas. Operação de Kubernetes.";
        var json = JsonSerializer.Serialize(new
        {
            requirements = new[]
            {
                new { requirement = "Experiência com APIs REST", match = "direct", factIds = new[] { fact.FactId } },
                new { requirement = "Conhecimento de filas", match = "adjacent", factIds = new[] { fact.FactId, Guid.NewGuid() } },
                new { requirement = "Operação de Kubernetes", match = "direct", factIds = new[] { Guid.NewGuid() } },
                new { requirement = "Experiência com APIs REST", match = "gap", factIds = Array.Empty<Guid>() },
                new { requirement = "Certificação inventada", match = "direct", factIds = new[] { fact.FactId } }
            }
        });
        var result = Assert.IsType<FitComparison>(CompareFitHandler.Parse(Guid.NewGuid(), 82, "local", description, [fact], json));
        Assert.Equal(82, result.CampaignScore);
        Assert.Equal(50, result.EvidenceScore);
        Assert.Equal(["direct", "adjacent", "gap"], result.Requirements.Select(x => x.Match));
        Assert.All(result.Requirements.Take(2), item => Assert.Equal(fact, Assert.Single(item.Evidence)));
        Assert.Empty(result.Requirements[2].Evidence);
    }

    [Fact]
    public void A_fact_that_does_not_say_what_the_requirement_asks_is_no_evidence()
    {
        var cooking = new FitEvidence(Guid.NewGuid(), "Concluí curso de gastronomia.");
        var json = JsonSerializer.Serialize(new { requirements = new[] { new { requirement = "Experiência com Kubernetes", match = "direct", factIds = new[] { cooking.FactId } } } });

        var result = Assert.IsType<FitComparison>(CompareFitHandler.Parse(Guid.NewGuid(), 82, "local", "Experiência com Kubernetes e Terraform.", [cooking], json));

        Assert.Equal(["gap", "gap"], result.Requirements.Select(x => x.Match));
        Assert.Equal("Terraform", result.Requirements[1].Requirement);
        Assert.Equal(0, result.EvidenceScore);
    }

    [Theory]
    [InlineData("Experiência com Kubernetes em produção", "direct", "Operei clusters Kubernetes em produção na Acme.", "direct")]
    [InlineData("5 anos com C#", "direct", "Desenvolvi serviços em C# na Acme.", "adjacent")]
    [InlineData("Experiência com APIs REST", "direct", "Construí APIs REST em .NET.", "direct")]
    [InlineData("Experiência com times ágeis", "direct", "Concluí curso de gastronomia.", "adjacent")]
    public void Direct_needs_the_requirement_in_the_fact(string requirement, string model, string fact, string expected) =>
        Assert.Equal(expected, CompareFitHandler.Backed(requirement, [new FitEvidence(Guid.NewGuid(), fact)], model));

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"requirements\":[null]}")]
    [InlineData("não é JSON")]
    public void Resposta_sem_requisito_verificavel_nao_vira_nota(string json) =>
        Assert.Null(CompareFitHandler.Parse(Guid.NewGuid(), 82, "local", "Experiência com APIs REST.", [], json));
}
