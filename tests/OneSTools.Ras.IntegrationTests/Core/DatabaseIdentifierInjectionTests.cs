using System;
using System.Threading.Tasks;
using OneSTools.Ras.Exporter.Core.ClickHouse;
using Xunit;

namespace OneSTools.Ras.IntegrationTests.Core;

/// <summary>Инъекционные регрессии T7/S4: идентификаторы БД не должны пропускать метасимволы SQL.</summary>
public sealed class DatabaseIdentifierInjectionTests
{
    [Theory]
    [InlineData("`x`; DROP DATABASE y;--")]
    [InlineData("valid'; TRUNCATE TABLE t; --")]
    [InlineData("a b\tc\n")]
    [InlineData("__proto__")]
    public void GetDatabaseName_Neutralizes_Injectable_Names(string malicious)
    {
        var storage = new ClickHouseMetricsStorage(
            "Host=invalid.invalid;Port=1;");

        string db = storage.GetDatabaseName(malicious, Guid.NewGuid());

        Assert.Matches(@"^[a-z0-9_]{1,128}$", db);
        Assert.DoesNotContain("`", db);
        Assert.DoesNotContain(";", db);
        Assert.DoesNotContain("--", db);
        Assert.DoesNotContain(" ", db);
    }

    [Fact]
    public async Task Initialize_Rejects_NonSlugged_Name_Even_If_Caller_Bypasses_GetDatabaseName()
    {
        var storage = new ClickHouseMetricsStorage("Host=invalid.invalid;Port=1;");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            storage.InitializeClusterDatabaseAsync("`bad`; DROP DATABASE x", default));
    }
}
