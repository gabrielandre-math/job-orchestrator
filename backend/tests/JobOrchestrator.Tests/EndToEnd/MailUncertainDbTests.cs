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
    public async Task Switching_the_automatic_mode_off_hands_its_releases_back_to_you() => await WithMailAsync(async (db, mail) =>
    {
        await mail.SaveSettingsAsync(new SettingsInput("Auto", 5, 7, 0, 24, true, Consent: true), CancellationToken.None);
        OutgoingMail Mail(string by) => new() { JobId = Guid.NewGuid(), Company = "Acme", JobTitle = "Dev", To = "jobs@acme.example", Subject = "Dev", Body = "Olá",
            Status = "Scheduled", ApprovedAt = DateTime.UtcNow, ApprovedBy = by, CreatedAt = DateTime.UtcNow };
        var auto = Mail("auto");
        var yours = Mail("you");
        db.Outgoing.AddRange(auto, yours);
        await db.SaveChangesAsync();

        await mail.SaveSettingsAsync(new SettingsInput("Review", 5, 7, 0, 24, true, Consent: false), CancellationToken.None);

        var back = await db.Outgoing.AsNoTracking().SingleAsync(o => o.Id == auto.Id);
        Assert.Equal("AwaitingApproval", back.Status);
        Assert.Null(back.ApprovedAt);
        Assert.Equal(MailService.Unreleased, back.Error);
        Assert.Equal("Scheduled", (await db.Outgoing.AsNoTracking().SingleAsync(o => o.Id == yours.Id)).Status);

        // "Revogar tudo" writes the settings straight to the database; the next send run catches up before picking anything.
        await mail.CancelAsync(yours.Id, CancellationToken.None);
        var late = Mail("auto");
        db.Outgoing.Add(late);
        await db.SaveChangesAsync();
        Assert.Equal(0, await mail.SendDueAsync(CancellationToken.None));
        Assert.Equal("AwaitingApproval", (await db.Outgoing.AsNoTracking().SingleAsync(o => o.Id == late.Id)).Status);
    });

    private static async Task WithMailAsync(Func<MailDbContext, MailService, Task> test)
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
            await test(db, new MailService(db, null!, null!, null!, null!, TimeProvider.System, NullLogger<MailService>.Instance));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(Admin);
            await admin.OpenAsync();
            await admin.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }

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
