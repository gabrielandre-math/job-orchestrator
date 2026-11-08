using JobOrchestrator.Applications.Features;
using JobOrchestrator.Jobs.Contracts;

namespace JobOrchestrator.Tests.Unit;

/// <summary>A platform's notice finds its application by the company, and by the role when there are two there; a doubt is left alone.</summary>
public sealed class HiringMailTests
{
    private static readonly Guid Konsi = Guid.NewGuid(), KonsiData = Guid.NewGuid(), Acme = Guid.NewGuid(), Gupy = Guid.NewGuid();

    private static HiringMailV1 Mail(string subject, string sender = "Konsi <no-reply@hire.lever.co>", string snippet = "", string? company = null) =>
        new("<m1@lever>", sender, subject, snippet, company, "application_received", DateTime.UtcNow);

    [Fact]
    public void The_company_in_the_subject_or_sender_finds_the_application()
    {
        (Guid, string, string)[] apps = [(Konsi, "Konsi Tecnologia Ltda.", "Desenvolvedor .NET Pleno"), (Acme, "Acme S.A.", "Engenheira de Dados")];
        Assert.Equal(Konsi, HiringMailConsumer.Match(apps, Mail("Thanks for applying to Konsi")));
        Assert.Equal(Acme, HiringMailConsumer.Match(apps, Mail("Atualização da sua candidatura", "Gupy <naoresponda@gupy.io>", "Olá! A ACME agradece o seu interesse.")));
        Assert.Equal(Acme, HiringMailConsumer.Match(apps, Mail("Sua candidatura", "Gupy <naoresponda@gupy.io>", company: "Acme")));
        Assert.Null(HiringMailConsumer.Match(apps, Mail("Thanks for applying to Konsiderate", "Konsiderate <jobs@konsiderate.io>")));
    }

    [Fact]
    public void Two_applications_at_the_same_company_are_told_apart_by_the_role_and_a_tie_is_left_alone()
    {
        (Guid, string, string)[] apps = [(Konsi, "Konsi", "Desenvolvedor .NET Pleno"), (KonsiData, "Konsi", "Analista de Dados")];
        Assert.Equal(KonsiData, HiringMailConsumer.Match(apps, Mail("Konsi | Analista de Dados: próxima etapa")));
        Assert.Equal(Konsi, HiringMailConsumer.Match(apps, Mail("Your application for Desenvolvedor .NET")));
        Assert.Null(HiringMailConsumer.Match(apps, Mail("Thanks for applying to Konsi")));
    }

    [Fact]
    public void A_platform_or_a_hidden_name_is_never_the_company()
    {
        Assert.Equal("", HiringMailConsumer.Key("Gupy"));
        Assert.Equal("", HiringMailConsumer.Key("Empresa Confidencial"));
        Assert.Equal(" konsi ", HiringMailConsumer.Key("Konsi Tecnologia do Brasil Ltda"));
        (Guid, string, string)[] apps = [(Gupy, "Gupy", "Desenvolvedor Backend")];
        Assert.Null(HiringMailConsumer.Match(apps, Mail("Gupy | Desenvolvedor Backend", "Gupy <naoresponda@gupy.io>")));
    }

    [Theory]
    [InlineData(null, "Recusada", true)]
    [InlineData("Entrevista", "Recusada", true)]
    [InlineData("Entrevista", "Oferta", true)]
    [InlineData("Oferta", "Entrevista", false)]
    [InlineData("Aceita", "Recusada", false)]
    public void An_email_only_moves_the_step_forward(string? current, string next, bool moves) => Assert.Equal(moves, JobEmailRepliedConsumer.Advances(current, next));
}
