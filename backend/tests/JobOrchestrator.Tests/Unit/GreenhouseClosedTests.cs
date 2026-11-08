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
