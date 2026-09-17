using System.Collections.Generic;

namespace OneSTools.Ras.Client.Protocol;

public readonly record struct NegotiateMessage(int Magic, short ProtocolVersion, short CodecVersion);

public sealed record ConnectMessage(Dictionary<string, object?> Parameters)
{
    public ConnectMessage() : this(new Dictionary<string, object?>()) { }
}

public sealed record ConnectAckMessage(Dictionary<string, object?> Parameters)
{
    public ConnectAckMessage() : this(new Dictionary<string, object?>()) { }
}

public readonly record struct KeepAliveMessage;

public readonly record struct EndpointOpenMessage(string ServiceId, Dictionary<string, object?> Parameters);

public readonly record struct EndpointOpenAckMessage(string ServiceId, int? EndpointId, Dictionary<string, object?> Parameters);

public readonly record struct EndpointCloseMessage(int? EndpointId);

public readonly record struct EndpointFailureMessage(string ServiceId, int? EndpointId, object? Throwable); // Throwable parsing is complex, object? for now

public readonly record struct EndpointMessage(int? EndpointId, short Format, byte[] Payload);
