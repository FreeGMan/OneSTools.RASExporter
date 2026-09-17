using System;
using System.Buffers;
using OneSTools.Ras.Client.Messages;
using OneSTools.Ras.Client.Models;
using OneSTools.Ras.Client.Wire;
using Xunit;

namespace OneSTools.Ras.Client.Tests.Messages;

public sealed class ProcessAndConnectionMessagesTests
{
    [Fact]
    public void ProcessInfo_Roundtrips()
    {
        var proc = new ProcessInfo
        {
            Uuid = Guid.NewGuid(),
            AvgBackCallTime = 0.5,
            AvgCallTime = 1.2,
            AvgDbCallTime = 0.8,
            AvgLockCallTime = 0.1,
            AvgServerCallTime = 0.3,
            AvgThreads = 4.0,
            Capacity = 1000,
            Connections = 15,
            Host = "srv-app-01",
            Enable = true,
            Port = 1560,
            MemoryExcessTime = 0,
            MemorySize = 512 * 1024 * 1024,
            Pid = "4567",
            Running = true,
            SelectionSize = 50,
            StartedAt = DateTime.UtcNow.AddDays(-1),
            Use = true,
            AvailablePerformance = 100,
            Reverse = false
        };

        var response = new GetWorkingProcessesResponse { Processes = new[] { proc } };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        response.Serialize(ref writer, version: 9);

        var seq = new ReadOnlySequence<byte>(buffer.WrittenMemory);
        var reader = new RasReader(seq);
        var deserialized = GetWorkingProcessesResponse.Deserialize(ref reader, version: 9);

        Assert.Single(deserialized.Processes);
        var p = deserialized.Processes[0];
        Assert.Equal(proc.Uuid, p.Uuid);
        Assert.Equal("srv-app-01", p.Host);
        Assert.Equal("4567", p.Pid);
        Assert.Equal(1560, p.Port);
        Assert.True(p.Running);
        Assert.True(p.Enable);
    }

    [Fact]
    public void ConnectionInfo_Roundtrips()
    {
        var conn = new ConnectionInfo
        {
            Uuid = Guid.NewGuid(),
            Application = "WebClient",
            BlockedByLs = 0,
            ConnectedAt = DateTime.UtcNow,
            Id = 1234,
            Host = "10.0.0.5",
            InfobaseId = Guid.NewGuid(),
            ProcessId = Guid.NewGuid(),
            SessionId = 5678
        };

        var response = new GetConnectionsShortResponse { Connections = new[] { conn } };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        response.Serialize(ref writer);

        var seq = new ReadOnlySequence<byte>(buffer.WrittenMemory);
        var reader = new RasReader(seq);
        var deserialized = GetConnectionsShortResponse.Deserialize(ref reader);

        Assert.Single(deserialized.Connections);
        var c = deserialized.Connections[0];
        Assert.Equal(conn.Uuid, c.Uuid);
        Assert.Equal("WebClient", c.Application);
        Assert.Equal(1234, c.Id);
        Assert.Equal(5678, c.SessionId);
    }

    [Fact]
    public void LockInfo_Roundtrips()
    {
        var lockInfo = new LockInfo
        {
            ConnectionId = Guid.NewGuid(),
            Description = "Exclusive infobase lock",
            LockedAt = DateTime.UtcNow,
            ObjectId = Guid.NewGuid(),
            SessionId = Guid.NewGuid()
        };

        var response = new GetLocksResponse { Locks = new[] { lockInfo } };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        response.Serialize(ref writer);

        var seq = new ReadOnlySequence<byte>(buffer.WrittenMemory);
        var reader = new RasReader(seq);
        var deserialized = GetLocksResponse.Deserialize(ref reader);

        Assert.Single(deserialized.Locks);
        var l = deserialized.Locks[0];
        Assert.Equal(lockInfo.ConnectionId, l.ConnectionId);
        Assert.Equal("Exclusive infobase lock", l.Description);
        Assert.Equal(lockInfo.ObjectId, l.ObjectId);
        Assert.Equal(lockInfo.SessionId, l.SessionId);
    }

    [Fact]
    public void InfobaseSummaryInfo_Roundtrips()
    {
        var ib = new InfobaseSummaryInfo
        {
            Uuid = Guid.NewGuid(),
            Descr = "ERP Database Production",
            Name = "erp_prod"
        };

        var response = new GetInfoBasesShortResponse { InfoBases = new[] { ib } };

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        response.Serialize(ref writer);

        var seq = new ReadOnlySequence<byte>(buffer.WrittenMemory);
        var reader = new RasReader(seq);
        var deserialized = GetInfoBasesShortResponse.Deserialize(ref reader);

        Assert.Single(deserialized.InfoBases);
        Assert.Equal(ib.Uuid, deserialized.InfoBases[0].Uuid);
        Assert.Equal("erp_prod", deserialized.InfoBases[0].Name);
        Assert.Equal("ERP Database Production", deserialized.InfoBases[0].Descr);
    }
}
