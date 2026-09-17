using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.IntegrationTests.ClickHouse;

/// <summary>
/// Скелет сквозных тестов: Клиент → Mock-RAS → ClickHouseStorage.
/// Требует поднятых сервисов из docker-compose (clickhouse, mock-ras). Разворачивается на
/// следующих этапах вместе с реализацией клиента и хранилища.
/// </summary>
public sealed class ClickHouseExportSkeletonTests
{
    [Fact]
    public void WireConstants_Are_Visible_From_Client_Assembly()
    {
        Assert.Equal(0x80, RasWireConstants.NullByte);
    }
}