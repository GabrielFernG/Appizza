using Appizza.Modules.Kitchen;

namespace Appizza.UnitTests;

public sealed class Phase5ProductionLifecycleTests
{
    [Theory]
    [InlineData("awaiting_preparation", false, true)]
    [InlineData("awaiting_preparation", true, false)]
    [InlineData("in_preparation", false, false)]
    public void StartRulesAreExplicit(string status, bool active, bool expected) => Assert.Equal(expected, ProductionLifecycle.CanStart(status, active));

    [Fact]
    public void PauseResumeFailReadyAndRestartRulesRejectInvalidCombinations()
    {
        Assert.True(ProductionLifecycle.CanPause("in_preparation", true, false));
        Assert.False(ProductionLifecycle.CanPause("paused", true, false));
        Assert.True(ProductionLifecycle.CanResume("paused", true, true));
        Assert.False(ProductionLifecycle.CanResume("paused", false, true));
        Assert.True(ProductionLifecycle.CanFail("in_preparation", true));
        Assert.False(ProductionLifecycle.CanFail("paused", true));
        Assert.True(ProductionLifecycle.CanRestart("paused", false, false, "failed"));
        Assert.False(ProductionLifecycle.CanRestart("paused", false, false, "completed"));
        Assert.True(ProductionLifecycle.CanReady("in_preparation", true, false));
        Assert.False(ProductionLifecycle.CanReady("in_preparation", true, true));
    }

    [Fact]
    public void EffectiveDurationSubtractsOnlyProductionPauses()
    {
        var start = DateTimeOffset.Parse("2026-08-12T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var end = start.AddMinutes(30);
        var pauses = new[] { (start.AddMinutes(5), (DateTimeOffset?)start.AddMinutes(10)), (start.AddMinutes(20), (DateTimeOffset?)start.AddMinutes(23)) };
        Assert.Equal(TimeSpan.FromMinutes(22), ProductionLifecycle.EffectiveDuration(start, end, pauses));
    }
}
