using System;
using OneSTools.Ras.Exporter.Core.ClickHouse;
using OneSTools.Ras.Exporter.Core.Pipeline;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.Core;

/// <summary>Юнит-проверки именования БД (T2/R5) и backoff-политики (T2/R3).</summary>
public sealed class CoreUtilsTests
{
    [Theory]
    [InlineData("MainCluster", "maincluster")]
    [InlineData("Локальный кластер", "lokalnyy_klaster")]
    [InlineData("Cluster-01", "cluster_01")]
    [InlineData("МОСКВА-2024", "moskva_2024")]
    [InlineData("ras_e2e_pipeline_test", "ras_e2e_pipeline_test")]
    [InlineData("щуп #7", "schup_7")]
    public void Slugify_ConformsToConvention(string input, string expected)
        => Assert.Equal(expected, ClickHouseDatabaseNamer.Slugify(input));

    [Fact]
    public void Backoff_Grows_Resets_And_Jitters()
    {
        var backoff = new BackoffPolicy();

        var first = backoff.NextDelay();
        Assert.InRange(first.TotalSeconds, 0.8, 1.2); // 1s ±20%

        var second = backoff.NextDelay();
        Assert.InRange(second.TotalSeconds, 1.6, 2.4); // 2s ±20%

        for (int i = 0; i < 10; i++)
            Assert.True(backoff.NextDelay() <= TimeSpan.FromSeconds(72)); // потолок 60+20%

        backoff.Reset();
        Assert.InRange(backoff.NextDelay().TotalSeconds, 0.8, 1.2);
    }
}
