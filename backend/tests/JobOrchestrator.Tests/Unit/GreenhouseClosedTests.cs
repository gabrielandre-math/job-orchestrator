using JobOrchestrator.Automation.Adapters;

namespace JobOrchestrator.Tests.Unit;

/// <summary>The night of 07/11: two closed Greenhouse jobs answered 200 on the board with ?error=true and were handed to the person as forms.</summary>
public sealed class GreenhouseClosedTests
{
    [Theory]
    [InlineData("https://job-boards.greenhouse.io/ilia/jobs/6206931004", "https://job-boards.greenhouse.io/ilia?error=true", true)]
    [InlineData("https://job-boards.greenhouse.io/stone/jobs/8007016003", "https://job-boards.greenhouse.io/stone", true)]
    [InlineData("https://job-boards.greenhouse.io/vtex/jobs/5224072004", "https://job-boards.greenhouse.io/vtex/jobs/5224072004", false)]
    [InlineData("https://boards.greenhouse.io/acme/jobs/1", "https://job-boards.greenhouse.io/acme/jobs/1", false)]
    public void A_job_that_lands_on_the_board_is_closed(string asked, string landed, bool closed) => Assert.Equal(closed, GreenhouseLikeForm.Closed(asked, landed));
}

/// <summary>The night of 07/11: Stone's and Zup's "how did you hear about us" boxes came to the person as eleven separate questions.</summary>
public sealed class CheckboxGroupTests
{
    private static GreenhouseLikeForm.CheckboxGroup Stone => new("Como você conheceu a nossa empresa?", true, ["a", "b", "c", "d", "e"],
        ["Amigos, colegas ou familiares", "Alguém da Stone entrou em contato comigo", "Notícias (jornal, TV, revistas ou sites)", "LinkedIn", "Site de carreiras da Stone"]);

    [Fact]
    public void An_answer_ticks_the_options_it_names_even_with_commas_inside()
    {
        Assert.Equal([0], GreenhouseLikeForm.Picks(Stone, "Amigos, colegas ou familiares"));
        Assert.Equal([2, 3], GreenhouseLikeForm.Picks(Stone, "Notícias (jornal, TV, revistas ou sites); LinkedIn"));
    }

    [Fact]
    public void How_you_heard_falls_back_to_the_careers_site_then_linkedin()
    {
        Assert.Equal([4], GreenhouseLikeForm.Picks(Stone, "Company careers page"));
        Assert.Equal([1], GreenhouseLikeForm.Picks(Stone with { Options = ["Amigos", "LinkedIn", "Outros"] }, null));
        // A group that is not about how you heard does not guess.
        Assert.Empty(GreenhouseLikeForm.Picks(Stone with { Label = "Qual motivo te fez querer se juntar ao nosso time?" }, null));
    }
}

/// <summary>The night of 07/11: a WhatsApp number and "where do you live" went to the person though the profile has both.</summary>
public sealed class BankQuestionTests
{
    private static readonly JobOrchestrator.Automation.Contracts.MaterialBundle Ana = new("Ana", "Lima", "ana@example.com", "+55 21 90000-0000", "linkedin.com/in/ana", "/tmp/cv.pdf",
        new Dictionary<string, string> { ["location"] = "Rio de Janeiro, RJ, Brasil" });

    [Theory]
    [InlineData("Please share your active WhatsApp number for smoother communication", "+55 21 90000-0000")]
    [InlineData("What state do you live in?", "Rio de Janeiro, RJ, Brasil")]
    [InlineData("Where do you live now?", "Rio de Janeiro, RJ, Brasil")]
    public void The_profile_answers_them(string question, string expected) => Assert.Equal(expected, GreenhouseLikeForm.Resolve(question, "text", Ana).Value);
}

/// <summary>The night of 07/11: three Lever jobs went to the person as "no form" because the robot opened the description, not /apply.</summary>
public sealed class FormPageTests
{
    [Theory]
    [InlineData("https://jobs.lever.co/ciandt/fa720e93-a0b1-41b6-abbc-4b83d8e9dc7b", "apply", "https://jobs.lever.co/ciandt/fa720e93-a0b1-41b6-abbc-4b83d8e9dc7b/apply")]
    [InlineData("https://jobs.lever.co/ciandt/fa720e93-a0b1-41b6-abbc-4b83d8e9dc7b/apply", "apply", "https://jobs.lever.co/ciandt/fa720e93-a0b1-41b6-abbc-4b83d8e9dc7b/apply")]
    [InlineData("https://jobs.ashbyhq.com/acme/1b2c3d", "application", "https://jobs.ashbyhq.com/acme/1b2c3d/application")]
    [InlineData("http://127.0.0.1:5480/lever/acme/12/apply", "apply", "http://127.0.0.1:5480/lever/acme/12/apply")]
    public void The_robot_opens_the_page_with_the_form(string link, string page, string expected) => Assert.Equal(expected, GreenhouseLikeForm.OnPage(link, 2, page));
}

/// <summary>The night of 07/11: "How many years of hands-on C# experience" took only digits and the robot gave up on the form.</summary>
public sealed class NumericMaskTests
{
    [Theory]
    [InlineData("How many years of hands-on C# development experience do you have?", true)]
    [InlineData("Quantos anos de experiência com .NET você tem?", true)]
    [InlineData("Pretensão salarial", true)]
    [InlineData("Why do you want to work here?", false)]
    public void A_count_of_years_takes_the_digits(string label, bool digits) => Assert.Equal(digits, JobOrchestrator.Automation.Adapters.LabelForm.AllowsNumericMask("input", "text", label));
}

