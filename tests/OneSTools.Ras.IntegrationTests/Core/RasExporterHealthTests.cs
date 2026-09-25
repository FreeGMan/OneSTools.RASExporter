using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OneSTools.Ras.Exporter.Core.ClickHouse;
using OneSTools.Ras.Exporter.Core.Pipeline;
using OneSTools.Ras.Exporter.Services.Health;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.Core;

public sealed class RasExporterHealthTests
{
    [Fact]
    public async Task Failed_Poll_Is_Visible_Until_That_Collector_Recovers()
    {
        var registry = new RasExporterHealthRegistry();
        await using var gate = new StorageHealthGate(_ => Task.FromResult(true));
        await using var pipeline = new MetricsBatchingPipeline(new ClickHouseMetricsStorage("Host=invalid.invalid;"));
        var check = new RasExporterHealthCheck(registry, gate, pipeline);
        var context = new HealthCheckContext();
        registry.MarkConnected("server/cluster");
        registry.ConfigurePolls("server/cluster", new Dictionary<string, TimeSpan>
        {
            ["sessions"] = TimeSpan.FromSeconds(5),
            ["locks"] = TimeSpan.FromSeconds(5)
        });

        registry.RecordPoll("server/cluster", "sessions", new InvalidOperationException("denied"));
        registry.RecordPoll("server/cluster", "locks", null);

        var failed = await check.CheckHealthAsync(context);
        Assert.Equal(HealthStatus.Degraded, failed.Status);
        Assert.Contains("sessions", failed.Description);

        registry.RecordPoll("server/cluster", "sessions", null);
        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(context)).Status);
    }
}
