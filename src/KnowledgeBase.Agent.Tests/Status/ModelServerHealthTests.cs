using KnowledgeBase.Agent.Status;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KnowledgeBase.Agent.Tests.Status;

public sealed class ModelServerHealthTests
{
    private readonly HealthCheckService _healthChecks = Substitute.For<HealthCheckService>();
    private readonly ModelServerHealth _health;

    public ModelServerHealthTests()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KnownAgents.Pleb}:Provider"] = "OpenAICompatible",
                [$"{KnownAgents.Librarian}:Provider"] = "Anthropic"
            })
            .Build();
        _healthChecks
            .CheckHealthAsync(Arg.Any<Func<HealthCheckRegistration, bool>?>(), Arg.Any<CancellationToken>())
            .Returns(Report(HealthStatus.Degraded));
        _health = new ModelServerHealth(_healthChecks, configuration);
    }

    [Test]
    public async Task TheModelServerIsDownForAnAgentThatUsesIt()
    {
        bool available = await _health.IsAvailableToAsync(KnownAgents.Pleb, CancellationToken.None);

        ItShouldBeUnavailable();

        void ItShouldBeUnavailable() => available.ShouldBeFalse();
    }

    [Test]
    public async Task TheModelServerIsDownForAnAgentOnAnotherProvider()
    {
        bool available = await _health.IsAvailableToAsync(KnownAgents.Librarian, CancellationToken.None);

        ItShouldBeAvailable();
        ItShouldNotCheckTheModelServer();

        void ItShouldBeAvailable() => available.ShouldBeTrue();

        void ItShouldNotCheckTheModelServer() => _healthChecks.DidNotReceiveWithAnyArgs().CheckHealthAsync(default, default);
    }

    private static HealthReport Report(HealthStatus status) => new(
        new Dictionary<string, HealthReportEntry>
        {
            [ModelServerHealthCheck.Name] = new(status, null, TimeSpan.Zero, null, null)
        },
        TimeSpan.Zero);
}
