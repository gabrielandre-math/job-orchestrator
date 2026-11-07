using Dapper;
using JobOrchestrator.BuildingBlocks.Data;
using JobOrchestrator.Mail.Data;
using JobOrchestrator.Mail.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace JobOrchestrator.Tests.EndToEnd;

/// <summary>
/// A process that stops after the SMTP server may have taken an e-mail leaves it in Sending. The next run marks it Uncertain and does
/// not send it again; only you put it back in the queue, with the same Message-ID, or mark it as sent.
/// </summary>
[Trait("Category", "E2E")]
public sealed class MailUncertainDbTests
{
    private const string Admin = "Host=127.0.0.1;Port=5463;Username=jo;Password=jo-test;Database=jo_test";

    [Fact]
    public async Task An_email_left_sending_turns_uncertain_and_only_you_send_it_again()
    {
        var name = $"jo_mail_{Guid.NewGuid():N}"[..20];
        await using (var admin = new NpgsqlConnection(Admin))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync($"create database {name}");
        }
        var conn = new NpgsqlConnectionStringBuilder(Admin) { Database = name }.ConnectionString;
        try
        {
            var options = new DbContextOptionsBuilder<MailDbContext>();
            options.UseModuleDatabase(conn, "mail");
            await using var db = new MailDbContext(options.Options);
            await db.Database.MigrateAsync();
            var stuck = new OutgoingMail { JobId = Guid.NewGuid(), Company = "Acme", JobTitle = "Dev", To = "jobs@acme.example", Subject = "Dev", Body = "Olá",
                Status = "Sending", MessageId = "<audit-2@acme.example>", ApprovedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow };
            db.Outgoing.Add(stuck);
            await db.SaveChangesAsync();
            var mail = new MailService(db, null!, null!, null!, null!, TimeProvider.System, NullLogger<MailService>.Instance);

            Assert.Equal(0, await mail.SendDueAsync(CancellationToken.None));
            var after = await db.Outgoing.AsNoTracking().SingleAsync(o => o.Id == stuck.Id);
            Assert.Equal("Uncertain", after.Status);
            Assert.Equal(MailService.UncertainNote, after.Error);

            Assert.Equal(0, await mail.SendDueAsync(CancellationToken.None));
            Assert.Equal("Uncertain", (await db.Outgoing.AsNoTracking().SingleAsync(o => o.Id == stuck.Id)).Status);

            var back = await mail.ResolveAsync(stuck.Id, sent: false, CancellationToken.None);
            Assert.Equal("Scheduled", back.Status);
            Assert.Equal("<audit-2@acme.example>", back.MessageId);
            await Assert.ThrowsAsync<MailRuleException>(() => mail.ResolveAsync(stuck.Id, sent: true, CancellationToken.None));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(Admin);
            await admin.OpenAsync();
            await admin.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }
}
