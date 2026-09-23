using System;
using System.Collections.Generic;
using OneSTools.Ras.Client.Exceptions;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Protocol;

/// <summary>Сериализация карты параметров протокола (ключ → типизированное значение), docs/TRANSPORT_SPEC.md §3.4.</summary>
public static class RasParametersSerializer
{
    public static void WriteParameters(ref RasWriter writer, IReadOnlyDictionary<string, object?> parameters)
    {
        writer.WriteNullableSize(parameters.Count);
        foreach (var kvp in parameters)
        {
            writer.WriteString(kvp.Key);
            WriteTypedValue(ref writer, kvp.Value);
        }
    }

    public static Dictionary<string, object?> ReadParameters(ref RasReader reader)
    {
        var parameters = new Dictionary<string, object?>();
        int? count = reader.ReadNullableSize();
        if (count is null)
            return parameters;
        if (count.Value > RasWireConstants.MaxListSize || count.Value > reader.Remaining)
            throw new RasProtocolException($"parameters count out of bounds: {count.Value}");

        for (int i = 0; i < count.Value; i++)
        {
            string? key = reader.ReadString();
            if (key is null)
                throw new InvalidOperationException("Parameter key cannot be null");

            object? value = ReadTypedValue(ref reader);
            parameters.Add(key, value);
        }

        return parameters;
    }

    private static void WriteTypedValue(ref RasWriter writer, object? value)
    {
        if (value is null)
        {
            writer.WriteType(null);
            return;
        }

        switch (value)
        {
            case bool b:
                writer.WriteType(ServiceWireType.Boolean);
                writer.WriteBool(b);
                break;
            case byte b:
                writer.WriteType(ServiceWireType.Byte);
                writer.WriteByte(b);
                break;
            case short s:
                writer.WriteType(ServiceWireType.Short);
                writer.WriteShort(s);
                break;
            case int i:
                writer.WriteType(ServiceWireType.Int);
                writer.WriteInt32(i);
                break;
            case long l:
                writer.WriteType(ServiceWireType.Long);
                writer.WriteLong(l);
                break;
            case float f:
                writer.WriteType(ServiceWireType.Float);
                writer.WriteFloat(f);
                break;
            case double d:
                writer.WriteType(ServiceWireType.Double);
                writer.WriteDouble(d);
                break;
            case string s:
                writer.WriteType(ServiceWireType.String);
                writer.WriteString(s);
                break;
            case Guid g:
                writer.WriteType(ServiceWireType.Uuid);
                writer.WriteUuid(g);
                break;
            case ServiceWireType t:
                writer.WriteType(ServiceWireType.Type);
                writer.WriteType(t);
                break;
            default:
                throw new NotSupportedException($"Unsupported parameter type: {value.GetType()}");
        }
    }

    private static object? ReadTypedValue(ref RasReader reader)
    {
        ServiceWireType? type = reader.ReadType();
        if (type is null)
            return null;

        return type.Value switch
        {
            ServiceWireType.Boolean => reader.ReadBool(),
            ServiceWireType.Byte => reader.ReadByte(),
            ServiceWireType.Short => reader.ReadShort(),
            ServiceWireType.Int => reader.ReadInt32(),
            ServiceWireType.Long => reader.ReadLong(),
            ServiceWireType.Float => reader.ReadFloat(),
            ServiceWireType.Double => reader.ReadDouble(),
            ServiceWireType.String => reader.ReadString(),
            ServiceWireType.Uuid => reader.ReadUuid(),
            ServiceWireType.Type => reader.ReadType(),
            // ServiceWireType.EndpointId uses the same nullable varint encoding as NullableSize
            ServiceWireType.EndpointId => reader.ReadEndpointId(),
            ServiceWireType.Size => reader.ReadSize(),
            ServiceWireType.NullableSize => reader.ReadNullableSize(),
            _ => throw new NotSupportedException($"Unsupported parameter type code: {type.Value}")
        };
    }
}