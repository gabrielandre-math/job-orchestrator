using JobOrchestrator.Materials.Compose;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace JobOrchestrator.Tests.Unit;

public sealed class ResumeLayoutTests
{
    [Fact]
    public void Corte_preserva_a_unica_evidencia_para_a_vaga()
    {
        var aws = new Claim("Implantei o serviço no ECS.", ["F1"]);
        var redundant = new Claim("Colaborei nas rotinas do time.", ["F2"]);
        var generic = new Claim("Acompanhei as entregas do time.", ["F2"]);
        var draft = new Draft(null, null, [
            new("P1", "Função recente", [generic, generic, generic, aws]),
            new("P2", "Função anterior", [redundant, new("Criei pipelines.", ["F3"]), new("Desenhei APIs.", ["F4"])])
        ], [], [], []);

        Assert.True(ResumeComposer.Trim(ref draft, new Selection([], "pt", ["ECS"], [])));
        Assert.Contains(aws, draft.Experiences[0].Bullets);
        Assert.Equal(3, draft.Experiences[0].Bullets.Length);
        Assert.Equal(3, draft.Experiences[1].Bullets.Length);
    }

    [Fact]
    public void Corte_preserva_o_minimo_de_topicos_por_experiencia()
    {
        var draft = new Draft(null, null, [new("P1", "Função", [new("Um", ["F1"]), new("Dois", ["F2"])])], [], [], []);
        Assert.False(ResumeComposer.Trim(ref draft, new Selection([], "pt", [], [])));
    }

    [Fact]
    public void Geometria_distingue_titulo_orfao_de_curriculo_curto_de_uma_pagina()
    {
        var small = new LayoutPage(842, [new(45, 58, "Resumo"), new(67, 80, "Trabalho com .NET.")]);
        Assert.Empty(PdfLayout.Measure([small], []));
        var first = small with { Lines = [.. small.Lines, new(550, 563, "Experiência")] };
        var findings = PdfLayout.Measure([first, small], []);
        Assert.Contains(findings, x => x.Contains("espaço vertical"));
        Assert.Contains(findings, x => x.Contains("título isolado"));
        Assert.Contains(findings, x => x.Contains("última página"));
    }

    [Fact]
    public void Le_as_posicoes_de_um_pdf_real()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        page.AddText("Backend Developer", 11, new PdfPoint(40, 740), font);
        page.AddText("APIs and message queues", 11, new PdfPoint(40, 420), font);
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, builder.Build());
            Assert.Contains(PdfLayout.Find(path, []), x => x.Contains("espaço vertical"));
        }
        finally { File.Delete(path); }
    }
}
