using System;
using System.Buffers;
using OneSTools.Ras.Client.Messages;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Client.Wire;
using Xunit;

namespace OneSTools.Ras.Client.Tests.Messages;

public sealed class ClusterMessagesTests
{
    [Fact]
    public void GetClustersResponse_Roundtrips()
    {
        var cluster1 = new ClusterInfo
        {
            Uuid = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ExpirationTimeout = 1000,
            Host = "srv-1c-01",
            LifetimeLimit = 3600,
            Port = 1541,
            MaxMemorySize = 1024 * 1024 * 1024,
            MaxMemoryTimeLimit = 60,
            Name = "Production-Cluster",
            SecurityLevel = 1,
            SessionFaultToleranceLevel = 2,
            LoadBalancingMode = 0,
            ErrorsCountThreshold = 10,
            KillProblemProcesses = true,
            KillByMemoryWithDump = true
        };

        var cluster2 = new ClusterInfo
        {
            Uuid = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ExpirationTimeout = 2000,
            Host = "srv-1c-02",
            LifetimeLimit = 7200,
            Port = 2541,
            MaxMemorySize = 512 * 1024 * 1024,
            MaxMemoryTimeLimit = 120,
            Name = "Test-Cluster",
            SecurityLevel = 0,
            SessionFaultToleranceLevel = 0,
            LoadBalancingMode = 1,
            ErrorsCountThreshold = 5,
            KillProblemProcesses = false,
            KillByMemoryWithDump = false
        };

        var response = new GetClustersResponse
        {
            Clusters = new[] { cluster1, cluster2 }
        };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        response.Serialize(ref writer, version: 9);

        var seq = new ReadOnlySequence<byte>(buffer.WrittenMemory);
        var reader = new RasReader(seq);
        var deserialized = GetClustersResponse.Deserialize(ref reader, version: 9);

        Assert.Equal(2, deserialized.Clusters.Count);
        Assert.Equal(cluster1.Uuid, deserialized.Clusters[0].Uuid);
        Assert.Equal(cluster1.Name, deserialized.Clusters[0].Name);
        Assert.Equal(cluster1.Port, deserialized.Clusters[0].Port);
        Assert.True(deserialized.Clusters[0].KillProblemProcesses);
        Assert.True(deserialized.Clusters[0].KillByMemoryWithDump);

        Assert.Equal(cluster2.Uuid, deserialized.Clusters[1].Uuid);
        Assert.Equal(cluster2.Name, deserialized.Clusters[1].Name);
        Assert.Equal(cluster2.Port, deserialized.Clusters[1].Port);
        Assert.False(deserialized.Clusters[1].KillProblemProcesses);
        Assert.False(deserialized.Clusters[1].KillByMemoryWithDump);
    }

    [Fact]
    public void GetClusterInfoRequest_Roundtrips()
    {
        var clusterId = Guid.NewGuid();
        var request = new GetClusterInfoRequest(clusterId);

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        request.Serialize(ref writer);

        var seq = new ReadOnlySequence<byte>(buffer.WrittenMemory);
        var reader = new RasReader(seq);
        var deserialized = GetClusterInfoRequest.Deserialize(ref reader);

        Assert.Equal(clusterId, deserialized.ClusterId);
    }
}
