namespace OneSTools.Ras.Client.Transport;

/// <summary>Идентификатор SWP-сервиса: wire-имя + версия (EndpointOpen/EndpointOpenAck, docs/TRANSPORT_SPEC.md §7).</summary>
public readonly record struct ServiceId(string Name, string Version)
{
    public override string ToString() => $"{Name}:{Version}";
}
