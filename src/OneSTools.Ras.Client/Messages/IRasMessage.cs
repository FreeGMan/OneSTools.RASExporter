using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Messages;

public interface IRasMessage
{
    MessageType MessageType { get; }
    void Serialize(ref RasWriter writer, int version = 0);
}

public interface IRasRequest<TResponse> : IRasMessage
    where TResponse : IRasResponse<TResponse>
{
}

public interface IRasVoidRequest : IRasMessage
{
}

public interface IRasResponse<TSelf>
    where TSelf : IRasResponse<TSelf>
{
    static abstract TSelf Deserialize(ref RasReader reader, int version = 0);
}
