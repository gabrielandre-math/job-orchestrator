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
