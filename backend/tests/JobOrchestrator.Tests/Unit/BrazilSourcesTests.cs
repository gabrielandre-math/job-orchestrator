using System.Text.Json;
using JobOrchestrator.Automation.Adapters;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Conectores brasileiros com as respostas de verdade das APIs públicas.</summary>
public sealed class BrazilSourcesTests
{
    [Fact]
    public void Solides_sem_vaga_responde_data_vazio_e_isso_e_zero_vagas()
    {
        // Resposta real de 06/11 para uma empresa sem vaga: antes o GetProperty quebrava aqui e travava o Explorador.
        using var doc = JsonDocument.Parse("""{"success":true,"errors":[],"data":{}}""");
        var (postings, pages) = SolidesSource.ParsePage("solides:cobmais", "cobmais", doc.RootElement);
        Assert.Empty(postings);
        Assert.Equal(0, pages);
    }

    [Fact]
    public void Solides_le_a_pagina_e_pula_vaga_encerrada()
    {
        using var doc = JsonDocument.Parse("""
            {"success":true,"errors":[],"data":{"totalPages":2,"currentPage":1,"count":2,"data":[
              {"id":101,"title":" Analista de Dados ","currentState":"em_andamento","jobType":"remoto","homeOffice":true,"description":"<p>SQL e Python.</p>","createdAt":"2026-11-01T10:00:00Z"},
              {"id":102,"title":"Vendedor","currentState":"encerrada","jobType":"presencial","city":{"name":"Recife"},"state":{"code":"PE"}}]}}
            """);
        var (postings, pages) = SolidesSource.ParsePage("solides:empresa", "empresa", doc.RootElement);
        var p = Assert.Single(postings);
        Assert.Equal(2, pages);
        Assert.Equal("Analista de Dados", p.Title);
        Assert.Equal("https://empresa.vagas.solides.com.br/vaga/101", p.ApplyUrl);
        Assert.Equal("remote", p.Workplace);
        Assert.Equal("SQL e Python.", p.Description);
    }
}
