using System.Diagnostics;
using JobOrchestrator.Applications.Features.Workflow;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Only leases of processes that are gone are released at boot.</summary>
public sealed class LeaseOwnerTests
{
    [Fact]
    public void This_process_is_alive() => Assert.True(WorkQueue.Alive($"{Environment.MachineName}:{Environment.ProcessId}"));

    [Fact]
    public void A_pid_that_does_not_exist_is_gone() => Assert.False(WorkQueue.Alive($"{Environment.MachineName}:{int.MaxValue - 7}"));

    [Fact]
    public void Another_program_on_a_reused_pid_is_gone()
    {
        using var sleeper = Process.Start(new ProcessStartInfo("sleep", "5") { UseShellExecute = false })!;
        try { Assert.False(WorkQueue.Alive($"{Environment.MachineName}:{sleeper.Id}")); }
        finally { sleeper.Kill(); }
    }

    [Fact]
    public void A_malformed_owner_is_gone() => Assert.False(WorkQueue.Alive("sem-pid"));
}
