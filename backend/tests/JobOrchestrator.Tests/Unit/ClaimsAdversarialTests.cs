using JobOrchestrator.Profile.Contracts;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Claims that reuse a number with another unit, or name a technology in lowercase, are not backed by unrelated facts.</summary>
public sealed class ClaimsAdversarialTests
{
    [Theory]
    [InlineData("implementei kubernetes em produção.", "Atuei no atendimento de suporte.", "kubernetes")]
    [InlineData("Reduzi custos em 40%.", "Resolvi 40 chamados.", "40%")]
    [InlineData("Gerenciei um orçamento de R$ 40 mil.", "Atendi 40 mil clientes.", "valor 40000")]
    [InlineData("Automatizei deploys com docker e terraform.", "Automatizei deploys com Docker.", "terraform")]
    public void An_unrelated_fact_does_not_back_the_claim(string claim, string fact, string flagged) =>
        Assert.Contains(flagged, Claims.Unsupported(claim, [fact]));

    [Theory]
    [InlineData("Reduzi custos de infraestrutura em 40%.", "Reduzi o custo de infraestrutura em 40% migrando para spot.")]
    [InlineData("Mantive clusters kubernetes em produção.", "Operei clusters Kubernetes (EKS) em produção.")]
    [InlineData("Gerenciei um orçamento de R$ 40 mil.", "Gerenciei o orçamento de R$ 40.000 por trimestre.")]
    [InlineData("Resolvi 40 chamados por semana.", "Resolvi 40 chamados por semana no suporte N2.")]
    public void The_fact_that_says_it_backs_the_claim(string claim, string fact) =>
        Assert.Empty(Claims.Unsupported(claim, [fact]));
}
