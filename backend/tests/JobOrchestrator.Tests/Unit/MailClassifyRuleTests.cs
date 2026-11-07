using JobOrchestrator.Mail.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>The mailbox the user showed on 07/11: newsletters, promotions and work tools are not replies, and the model's class needs its own words.</summary>
public sealed class MailClassifyRuleTests
{
    [Theory]
    [InlineData("noreply@recrutei-mail.com.br", "Aqui está uma vaga que combina com você!", "Achamos que você pode se interessar por essa vaga!", "job_alert")]
    [InlineData("support@englishcentral.com", "Últimos 2 dias para aproveitar sua oferta especial!", "<!DOCTYPE html>", "other")]
    [InlineData("jira@drmeddie.atlassian.net", "[JIRA] (SCRUM-250) Retire the RequiresIntroCall gate and offer an intro call", "CONTEXT Part of SCRUM-248", "other")]
    [InlineData("no-reply@gupy.com.br", "Desenvolvedor(a) Web C#/.NET - Pleno - Vaga finalizada", "Informamos que essa vaga já foi finalizada", "job_closed")]
    [InlineData("imile@ses-mail.inhire.app", "Confirmação de Inscrição - Processo Seletivo (iMile Delivery Brazil)", "", null)]
    [InlineData("convites@fcinterview.fcamara.com.br", "Convite para entrevista — Desenvolvedor Backend (Grupo FCamara)", "Você foi convidado para realizar uma entrevista técnica", null)]
    public void Rules_decide_what_is_not_a_reply(string from, string subject, string text, string? expected) =>
        Assert.Equal(expected, MailService.Rule(from, subject, text));

    [Theory]
    [InlineData("interview_invite", "Aqui está uma vaga", "Encontramos uma vaga publicada recentemente", "other")]
    [InlineData("interview_invite", "Convite para entrevista", "Você foi convidado para uma entrevista técnica", "interview_invite")]
    [InlineData("offer", "Sua oferta especial", "Aproveite o desconto", "other")]
    [InlineData("offer", "Proposta de trabalho", "Segue a proposta de trabalho com o salário", "offer")]
    [InlineData("assessment", "Próxima etapa", "Envie o desafio técnico até sexta", "assessment")]
    public void The_model_class_needs_its_own_words(string category, string subject, string text, string expected) =>
        Assert.Equal(expected, MailService.Checked(category, subject, text));
}
