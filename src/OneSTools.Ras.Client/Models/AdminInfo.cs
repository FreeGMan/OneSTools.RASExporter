using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Models;

public sealed record AdminInfo
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public bool PasswordAuthAllowed { get; init; }
    public bool SysAuthAllowed { get; init; }
    public string SysUserName { get; init; } = string.Empty;

    public void Serialize(ref RasWriter writer, int version = 0)
    {
        writer.WriteString(Name);
        writer.WriteString(Description);
        writer.WriteString(Password);
        writer.WriteBool(PasswordAuthAllowed);
        writer.WriteBool(SysAuthAllowed);
        writer.WriteString(SysUserName);
    }

    public static AdminInfo Deserialize(ref RasReader reader, int version = 0)
    {
        return new AdminInfo
        {
            Name = reader.ReadString() ?? string.Empty,
            Description = reader.ReadString() ?? string.Empty,
            Password = reader.ReadString() ?? string.Empty,
            PasswordAuthAllowed = reader.ReadBool(),
            SysAuthAllowed = reader.ReadBool(),
            SysUserName = reader.ReadString() ?? string.Empty,
        };
    }
}
