namespace OneSTools.Ras.Client.Transport;

public readonly record struct ServiceId(string Name, string Version)
{
    public override string ToString() => $"{Name}:{Version}";
}
