using Microsoft.Extensions.Options;
using OneSTools.Ras.Exporter.Configuration;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.Core;

public sealed class SettingsValidationTests
{
    [Fact]
    public void Invalid_Server_And_Polling_Parameters_Are_Rejected()
    {
        var settings = new RasExporterSettings
        {
            Servers = [new RasServerSettings
            {
                Name = "server",
                Host = " ",
                Port = 0,
                ConnectTimeoutMilliseconds = -1,
                PollIntervals = new PollIntervalsSettings { SessionsSeconds = 0 },
                Clusters = [new RasClusterSettings()]
            }]
        };

        ValidateOptionsResult result = new RasExporterSettingsValidator().Validate(null, settings);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, error => error.Contains("Host"));
        Assert.Contains(result.Failures, error => error.Contains("Port"));
        Assert.Contains(result.Failures, error => error.Contains("Sessions") || error.Contains("интервалы"));
        Assert.Contains(result.Failures, error => error.Contains("ClusterId"));
    }

    [Fact]
    public void InfoBases_Poll_Interval_Is_Validated_Like_Other_Metrics()
    {
        var settings = new RasExporterSettings
        {
            Servers = [new RasServerSettings
            {
                Name = "server",
                Host = "ras",
                PollIntervals = new PollIntervalsSettings { InfoBasesSeconds = 0 },
                Clusters = []
            }]
        };

        Assert.True(new RasExporterSettingsValidator().Validate(null, settings).Failed);
        Assert.Equal(300, TimeSpan.FromSeconds(new PollIntervalsSettings().InfoBasesSeconds).TotalSeconds);
    }

    [Fact]
    public void Valid_Configuration_Is_Accepted_And_Invalid_Batch_Size_Is_Rejected()
    {
        Assert.False(new RasExporterSettingsValidator().Validate(null, new RasExporterSettings
        {
            Servers = [new RasServerSettings { Name = "server", Host = "ras", Clusters = [] }]
        }).Failed);

        var validator = new ClickHouseSettingsValidator();
        Assert.False(validator.Validate(null, new ClickHouseSettings()).Failed);
        Assert.True(validator.Validate(null, new ClickHouseSettings { BatchSize = 0, ConnectionString = "" }).Failed);
    }
}
