using System;
using System.Buffers;
using OneSTools.Ras.Client.Messages;
using OneSTools.Ras.Client.Wire;
using Xunit;

namespace OneSTools.Ras.Client.Tests.Messages;

public sealed class LockMessagesTests
{
    [Fact]
    public void Session_Locks_Request_Contains_Cluster_Infobase_And_Session_Ids()
    {
        var clusterId = Guid.NewGuid();
        var infobaseId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new GetSessionLocksRequest(clusterId, infobaseId, sessionId);
        var output = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(output);
        request.Serialize(ref writer);

        var reader = new RasReader(new ReadOnlySequence<byte>(output.WrittenMemory));
        Assert.Equal(clusterId, reader.ReadUuid());
        Assert.Equal(infobaseId, reader.ReadUuid());
        Assert.Equal(sessionId, reader.ReadUuid());
        Assert.Equal(0, reader.Remaining);
    }
}
