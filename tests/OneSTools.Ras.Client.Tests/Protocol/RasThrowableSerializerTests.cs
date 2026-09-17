using System;
using System.Buffers;
using System.Collections.Generic;
using OneSTools.Ras.Client.Exceptions;
using OneSTools.Ras.Client.Protocol;
using OneSTools.Ras.Client.Wire;
using Xunit;

namespace OneSTools.Ras.Client.Tests.Protocol;

public sealed class RasThrowableSerializerTests
{
    [Fact]
    public void Null_Throwable_Encodes_As_Null_String()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        RasThrowableSerializer.WriteThrowable(ref writer, null);

        Assert.Equal(new byte[] { 0x80 }, buffer.WrittenSpan.ToArray());

        var seq = new ReadOnlySequence<byte>(buffer.WrittenMemory);
        var reader = new RasReader(seq);
        var decoded = RasThrowableSerializer.ReadThrowable(ref reader);
        Assert.Null(decoded);
    }

    [Fact]
    public void Remote_Exception_With_Stack_And_Cause_Roundtrips()
    {
        var inner = new RasRemoteException(
            "com.v8.core.InnerException",
            "Root failure",
            new List<RasRemoteStackFrame>
            {
                new("com.v8.core.InternalService", "doLowLevel", "InternalService.java", 42)
            }
        );

        var outer = new RasRemoteException(
            "com.v8.ibis.admin.AgentAdminException",
            "Cluster access denied",
            new List<RasRemoteStackFrame>
            {
                new("com.v8.ibis.admin.AgentAdminConnection", "getClusters", "AgentAdminConnection.java", 150),
                new("com.v8.ibis.admin.AgentAdminConnection", "execute", "AgentAdminConnection.java", 80)
            },
            inner
        );

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new RasWriter(buffer);
        RasThrowableSerializer.WriteThrowable(ref writer, outer);

        var seq = new ReadOnlySequence<byte>(buffer.WrittenMemory);
        var reader = new RasReader(seq);
        var decoded = RasThrowableSerializer.ReadThrowable(ref reader) as RasRemoteException;

        Assert.NotNull(decoded);
        Assert.Equal("com.v8.ibis.admin.AgentAdminException", decoded.RemoteClassName);
        Assert.Contains("Cluster access denied", decoded.Message);
        Assert.Equal(2, decoded.RemoteStackTrace.Count);
        Assert.Equal("getClusters", decoded.RemoteStackTrace[0].MethodName);
        Assert.Equal(150, decoded.RemoteStackTrace[0].LineNumber);

        var decodedInner = decoded.InnerException as RasRemoteException;
        Assert.NotNull(decodedInner);
        Assert.Equal("com.v8.core.InnerException", decodedInner.RemoteClassName);
        Assert.Contains("Root failure", decodedInner.Message);
        Assert.Single(decodedInner.RemoteStackTrace);
        Assert.Equal(42, decodedInner.RemoteStackTrace[0].LineNumber);
    }
}
