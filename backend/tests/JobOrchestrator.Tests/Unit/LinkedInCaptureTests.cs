using JobOrchestrator.Automation.Features;
using JobOrchestrator.Jobs.Features;

namespace JobOrchestrator.Tests.Unit;

public sealed class LinkedInCaptureTests
{
    [Theory]
    [InlineData("https://br.linkedin.com/jobs/view/backend-engineer-123?trk=mail", "https://www.linkedin.com/jobs/view/123/")]
    [InlineData("https://www.linkedin.com/jobs/view/123/", "https://www.linkedin.com/jobs/view/123/")]
    [InlineData("https://www.linkedin.com.evil.test/jobs/view/123", null)]
    [InlineData("https://evil-linkedin.com/jobs/view/123", null)]
    [InlineData("https://linkedin.com@evil.test/jobs/view/123", null)]
    [InlineData("https://www.linkedin.com:8443/jobs/view/123", null)]
    [InlineData("https://www.linkedin.com/in/recruiter", null)]
    public void Canonical_id_rejects_spoofed_hosts_profiles_and_nonstandard_ports(string url, string? expected) => Assert.Equal(expected, LinkedInCapture.Canonical(url));

    [Theory]
    [InlineData("Privacidade: privacy@empresa.example\nPara se candidatar, envie seu currículo para jobs@empresa.example", "jobs@empresa.example", "published_for_application")]
    [InlineData("Envie o currículo para:\njobs@empresa.example", "jobs@empresa.example", "published_for_application")]
    [InlineData("Conheça nossa equipe: pessoa@empresa.example", null, "not_published")]
    [InlineData("Não envie currículo para jobs@empresa.example", null, "not_published")]
    [InlineData("Candidaturas: jobs@empresa.example ou hiring@empresa.example", null, "ambiguous")]
    [InlineData("Para candidatura, suporte em support@empresa.example", null, "not_published")]
    public void Contact_requires_published_application_intent(string text, string? email, string status)
    {
        var contact = LinkedInCapture.Contact(text); Assert.Equal(email, contact.Email); Assert.Equal(status, contact.Status);
        if (email is not null) Assert.Contains(email, contact.Evidence!);
    }

    [Fact]
    public void Search_query_is_encoded_and_has_explicit_filters()
    {
        var url = new Uri(LinkedInSession.SearchUrl("C# & .NET", new(Location: "Rio de Janeiro", RemoteOnly: true)));
        Assert.Equal("www.linkedin.com", url.Host);
        Assert.Contains("keywords=C%23%20%26%20.NET", url.Query);
        Assert.Contains("f_WT=2", url.Query);
        Assert.Contains("location=Rio%20de%20Janeiro", url.Query);
    }

    [Fact]
    public void Session_posting_keeps_description_contact_proof_and_closed_state()
    {
        var posting = LinkedInSession.Posting(".NET", new("https://www.linkedin.com/jobs/view/123/", "Developer", "Acme", "Brasil", "Envie currículo: jobs@empresa.example", null, null, null, Closed: true), DateTimeOffset.UtcNow);
        Assert.Equal("123", posting.ExternalId); Assert.Equal("jobs@empresa.example", posting.ApplyEmail);
        Assert.Contains("published_for_application", posting.RawJson); Assert.True(posting.Tombstone);
    }

    [Fact]
    public void A_published_supported_ats_is_the_application_target_without_double_sending_email()
    {
        var posting = LinkedInSession.Posting(".NET", new("https://www.linkedin.com/jobs/view/123/", "Developer", "Acme", "Brasil", "Envie currículo: jobs@empresa.example", null, null,
            "https://jobs.lever.co/acme/123"), DateTimeOffset.UtcNow);
        Assert.Equal("https://jobs.lever.co/acme/123", posting.ApplyUrl);
        Assert.Null(posting.ApplyEmail); Assert.Contains("jobs@empresa.example", posting.RawJson);
        Assert.Null(LinkedInSession.ExternalAts("https://jobs.lever.co.evil.test/acme/123"));
    }
}
