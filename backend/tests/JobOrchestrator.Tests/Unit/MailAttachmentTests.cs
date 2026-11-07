using System.Security.Cryptography;
using JobOrchestrator.Mail.Data;
using JobOrchestrator.Mail.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>An e-mail goes with the exact resume it was drafted with, or it does not go.</summary>
public sealed class MailAttachmentTests
{
    [Fact]
    public async Task The_drafted_file_goes_as_it_is()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            var m = new OutgoingMail { AttachmentPath = path, AttachmentSha256 = Convert.ToHexStringLower(SHA256.HashData([1, 2, 3])) };
            Assert.Equal([1, 2, 3], await MailService.AttachmentAsync(m, CancellationToken.None));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task A_missing_resume_stops_the_email()
    {
        var m = new OutgoingMail { AttachmentPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf"), AttachmentSha256 = new string('0', 64) };
        var ex = await Assert.ThrowsAsync<MailRuleException>(() => MailService.AttachmentAsync(m, CancellationToken.None));
        Assert.Contains("não existe mais", ex.Message);
    }

    [Fact]
    public async Task A_resume_changed_after_the_draft_stops_the_email()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [9, 9]);
            var m = new OutgoingMail { AttachmentPath = path, AttachmentSha256 = Convert.ToHexStringLower(SHA256.HashData([1, 2, 3])) };
            var ex = await Assert.ThrowsAsync<MailRuleException>(() => MailService.AttachmentAsync(m, CancellationToken.None));
            Assert.Contains("mudou", ex.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task No_attachment_is_no_attachment() => Assert.Null(await MailService.AttachmentAsync(new OutgoingMail(), CancellationToken.None));
}
