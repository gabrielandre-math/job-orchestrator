using JobOrchestrator.Mail.Data;
using JobOrchestrator.Mail.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Aviso de falha na entrega volta no fio do e-mail, mas não é resposta da empresa: fica como não entregue.</summary>
public sealed class MailBounceTests
{
    [Theory]
    [InlineData("mailer-daemon@googlemail.com", "Mail Delivery Subsystem", "Delivery Status Notification (Failure)")]
    [InlineData("postmaster@empresa.com.br", null, "Undeliverable: Candidatura")]
    [InlineData("avisos@empresa.com.br", null, "Mensagem não entregue")]
    public void Aviso_de_entrega_e_falha(string from, string? name, string subject) => Assert.True(MailService.Bounce(from, name, subject));

    [Fact] public void Resposta_da_empresa_nao_e_falha() => Assert.False(MailService.Bounce("rh@empresa.com.br", "RH Empresa", "Re: Candidatura: Desenvolvedor .NET"));

    [Fact]
    public void Falha_vira_nao_entregue_e_tira_a_resposta()
    {
        var m = new OutgoingMail { Status = "Replied", RepliedAt = DateTime.UtcNow, ReplyClass = "other", To = "rh@empresa.com.br" };
        MailService.Bounced(m, "** Message blocked ** Your message to rh@empresa.com.br has been blocked.");
        Assert.Equal(("Bounced", null, null), (m.Status, m.RepliedAt, m.ReplyClass));
        Assert.StartsWith("Não chegou na empresa: Message blocked", m.Error);
    }
}
