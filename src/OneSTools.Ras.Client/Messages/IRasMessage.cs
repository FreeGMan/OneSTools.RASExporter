using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Messages;

/// <summary>SWP-сообщение API: тип + бинарная (де)сериализация в RasWriter/RasReader.</summary>
public interface IRasMessage
{
    MessageType MessageType { get; }
    void Serialize(ref RasWriter writer, int version = 0);
}

/// <summary>Запрос, ожидающий типизированный ответ.</summary>
public interface IRasRequest<TResponse> : IRasMessage
    where TResponse : IRasResponse<TResponse>
{
}

/// <summary>Запрос без данных в ответе (VOID или EXCEPTION).</summary>
public interface IRasVoidRequest : IRasMessage
{
}

/// <summary>Ответ, восстанавливаемый из RasReader (версионирован: см. docs/CODEC_SPEC.md).</summary>
public interface IRasResponse<TSelf>
    where TSelf : IRasResponse<TSelf>
{
    static abstract TSelf Deserialize(ref RasReader reader, int version = 0);
}
