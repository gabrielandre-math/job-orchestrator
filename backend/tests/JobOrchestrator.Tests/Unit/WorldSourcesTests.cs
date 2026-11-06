using System.IO.Compression;
using System.Text;
using System.Text.Json;
using JobOrchestrator.Automation.Adapters;
using JobOrchestrator.Jobs.Domain;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Fontes novas do mundo (05/11): República Tcheca e Letônia oficiais, Pinpoint, BambooHR e Recruiterbox, com o formato real de cada uma.</summary>
public sealed class WorldSourcesTests
{
    private const string Czech = """
        {"portalId":67256368,"referencniCislo":"33087320704","azylant":false,"cizinecMimoEu":true,"datumVlozeni":"2026-05-05T00:00:00.000Z",
         "datumZmeny":"2026-11-05T22:01:44.523Z","mesicniMzdaDo":null,"mesicniMzdaOd":27328,"modraKarta":false,"pocetHodinTydne":38.75,
         "pozadovanaProfese":{"cs":"Programátor C# (M/Ž)"},"terminZahajeniPracovnihoPomeru":"2026-07-01","zamestnaneckaKarta":true,
         "upresnujiciInformace":{"cs":"Vývoj aplikací v .NET. Kontakt: RENÁTA ČERVENKOVÁ, tel.: +420 777 232 574, renata@example.cz"},
         "mistoVykonuPrace":{"pracoviste":[{"nazev":"PSS Bohemia s.r.o. - PRAHA","adresa":{"dodatekAdresy":"Praha"}}]},
         "zamestnavatel":{"ico":"26451255","nazev":"PSS Bohemia s.r.o."},"profeseCzIsco":{"id":"CzIsco/25141"},
         "pracovnePravniVztahy":[{"id":"PracovnepravniVztah/plny"}],
         "prvniKontaktSeZamestnavatelem":{"komuSeHlasit":{"telefon":"777232574","jmeno":"Renáta","prijmeni":"Červenková"}}}
        """;

    [Fact]
    public void Vaga_tcheca_vira_vaga_com_bandeiras_oficiais_e_sem_o_contato()
    {
        using var doc = JsonDocument.Parse(Czech);
        var p = MpsvSource.Parse("mpsv:programator", doc.RootElement);
        Assert.Equal(("67256368", "PSS Bohemia s.r.o.", "Programátor C# (M/Ž)", "Praha, Czechia"), (p.ExternalId, p.Company, p.Title, p.Location));
        Assert.Equal("https://www.uradprace.cz/web/cz/vyhledani-volneho-mista#/volna-mista-detail/67256368", p.ApplyUrl);
        Assert.Equal("CZK 27328/mês", p.Compensation);
        Assert.Equal("Tempo integral", p.Employment);
        Assert.Contains("aceita estrangeiro de fora da UE", p.Description);
        Assert.Contains("zaměstnanecká karta", p.Description);
        Assert.Contains("Vývoj aplikací v .NET.", p.Description);
        foreach (var personal in new[] { "ČERVENKOVÁ", "777 232 574", "renata@example.cz", "Červenková" })
        {
            Assert.DoesNotContain(personal, p.Description);
            Assert.DoesNotContain(personal, p.RawJson);
        }
        // Com o código oficial de municípios, a cidade e a região certas.
        using var brno = JsonDocument.Parse(Czech.Replace("\"mistoVykonuPrace\":{", "\"mistoVykonuPrace\":{\"obec\":{\"id\":\"Obec/582786\"},").Replace("\"dodatekAdresy\":\"Praha\"", "\"kraj\":{\"id\":\"Kraj/116\"}"));
        Assert.Equal("Brno, Jihomoravský kraj, Czechia", MpsvSource.Parse("mpsv:programator", brno.RootElement, new Dictionary<string, string> { ["Obec/582786"] = "Brno" }).Location);
        // O cartão de funcionário é a autorização de trabalho de quem vem de fora da UE: vaga presencial com o caminho escrito.
        Assert.Equal("RELOCATION_WITH_SPONSORSHIP", Eligibility.Classify(p.Title, p.Description, p.Location, null).Class);
    }

    [Fact]
    public async Task Arquivo_grande_e_lido_item_a_item_mesmo_com_buffer_pequeno()
    {
        var items = string.Join(",", Enumerable.Range(1, 5).Select(i => $$"""{"portalId":{{i}},"pozadovanaProfese":{"cs":"Profese {{i}} {{new string('x', 90)}}"},"pole":[1,2,{"a":"]"}]}"""));
        var json = $$"""{"polozky": [{{items}}], "pocet": 5}""";
        using var gz = new MemoryStream();
        using (var w = new GZipStream(gz, CompressionLevel.Fastest, leaveOpen: true)) w.Write(Encoding.UTF8.GetBytes(json));
        gz.Position = 0;
        var ids = new List<int>();
        await using var input = new GZipStream(gz, CompressionMode.Decompress);
        await foreach (var item in JsonItems.ReadAsync(input, "polozky", bufferSize: 64)) ids.Add(item.GetProperty("portalId").GetInt32());
        Assert.Equal([1, 2, 3, 4, 5], ids);
    }

    [Fact]
    public void Vaga_da_Letonia_vira_vaga_com_o_link_oficial()
    {
        var (head, rows) = NvaSource.ParseCsv("﻿Vakances_Nr,Aktualizacijas_datums,Iestades_registracijas_numurs,Vakances_nosaukums,Vakances_kategorija,Alga_no,Alga_lidz,Slodzes_tips,Darba_laika_veids,Darba_stundas_nedela,Pieteiksanas_termins,Attels,Vieta,Vakances_paplasinats_apraksts\n" +
            "261104-12,2026-11-04,40003575567,AUTOMOBIĻA VADĪTĀJS,Elektronika / Enerģētika / Elektroenerģija,1774.000,1774.000,Viena vesela slodze,Normālais darba laiks,\"\",2026-11-15,\"\",\"Dārzciema iela 86, Rīga\",https://cvvp.nva.gov.lv/#/pub/vakances/468324505\n");
        var p = NvaSource.Parse("nva:vaditajs", head, rows[0]);
        Assert.Equal(("261104-12", "Automobiļa Vadītājs", "Dārzciema iela 86, Rīga, Latvia"), (p.ExternalId, p.Title, p.Location));
        Assert.Equal("https://cvvp.nva.gov.lv/#/pub/vakances/468324505", p.ApplyUrl);
        Assert.Equal("EUR 1774/mês", p.Compensation);
        Assert.Equal("Empregador 40003575567 (registro da Letônia)", p.Company);
        Assert.Contains("Inscrições até 2026-11-15", p.Description);
        Assert.Equal(new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc), p.UpdatedAt);
    }

    [Fact]
    public void Quadros_de_Pinpoint_BambooHR_e_Recruiterbox_viram_vagas()
    {
        using var pin = JsonDocument.Parse("""
            {"id":"217178","title":"Backend Engineer (.NET)","url":"https://aawdc.pinpointhq.com/en/postings/92841fbb","workplace_type":"remote",
             "location":{"city":"","province":""},"compensation_visible":true,"compensation_minimum":90000,"compensation_maximum":120000,
             "compensation_currency":"USD","compensation_frequency":"year","employment_type_text":"Full Time","description":"<p>Build APIs in C#.</p>"}
            """);
        var a = PinpointSource.Parse("pinpoint:aawdc", "aawdc", pin.RootElement);
        Assert.Equal(("Aawdc", "Remote", "remote", "USD 90000-120000/year"), (a.Company, a.Location, a.Workplace, a.Compensation));
        Assert.Equal("Build APIs in C#.", a.Description);

        using var list = JsonDocument.Parse("""{"id":"26","jobOpeningName":"Software Engineer ","employmentStatusLabel":"Permanent","location":{"city":"Manchester","state":"Greater Manchester"},"isRemote":null,"locationType":"0"}""");
        using var detail = JsonDocument.Parse("""{"jobOpeningShareUrl":"https://acme.bamboohr.com/careers/26","description":"<p>Java and AWS.</p>","location":{"city":"Manchester","state":"Greater Manchester","addressCountry":"United Kingdom"},"datePosted":"2026-10-30"}""");
        var b = BambooHrSource.Parse("bamboohr:acme", "acme", list.RootElement, detail.RootElement);
        Assert.Equal(("Software Engineer", "Manchester, Greater Manchester, United Kingdom", "https://acme.bamboohr.com/careers/26"), (b.Title, b.Location, b.ApplyUrl));
        Assert.Null(b.Workplace);

        using var box = JsonDocument.Parse("""{"id":"fk0v31","title":"Scale Loving Software Engineer","location":{"city":"Durham","state":"NC","country":"United States"},"hosted_url":"https://adzerk.hire.trakstar.com/jobs/fk0v31/","allows_remote":false,"position_type":"full_time","description":"<p>Scale.</p>"}""");
        var c = RecruiterboxSource.Parse("recruiterbox:adzerk", "adzerk", box.RootElement);
        Assert.Equal(("Adzerk", "Durham, NC, United States", "https://adzerk.hire.trakstar.com/jobs/fk0v31/", "full time"), (c.Company, c.Location, c.ApplyUrl, c.Employment));
    }

    [Theory]
    [InlineData("https://aawdc.pinpointhq.com/en/postings/92841fbb", "pinpoint:aawdc")]
    [InlineData("https://acme.bamboohr.com/careers/26", "bamboohr:acme")]
    [InlineData("https://adzerk.hire.trakstar.com/jobs/fk0v31/", "recruiterbox:adzerk")]
    public void Link_dos_ATS_novos_vira_quadro(string url, string source) => Assert.Equal(source, SourceKeys.Board(url));

    [Fact]
    public void Singapura_fica_fora_ate_haver_autorizacao_por_escrito()
    {
        Assert.StartsWith("PARTNER_ONLY", SourceRegistry.Gate("mcf:software engineer", [], true, new DateTime(2026, 11, 6)));
        Assert.Null(SourceRegistry.Gate("mpsv:programator", [], true, new DateTime(2026, 11, 6)));
        Assert.Equal(["bamboohr:100percentgroup", "pinpoint:aawdc", "recruiterbox:adzerk"],
            new[] { SourceKeys.OpenJobs("pinpoint", "aawdc"), SourceKeys.OpenJobs("bamboohr", "100percentgroup"), SourceKeys.OpenJobs("recruiterbox", "adzerk") }.Order());
    }
}
