using System;
using System.Collections.Generic;
using OneSTools.Ras.Client.Exceptions;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Protocol;

/// <summary>(Де)сериализация Java-throwable из wire-формата SWP (docs/TRANSPORT_SPEC.md §3.5).</summary>
public static class RasThrowableSerializer
{
    public static void WriteThrowable(ref RasWriter writer, Exception? exception)
    {
        if (exception is null)
        {
            writer.WriteString(null);
            return;
        }

        string className = exception is RasRemoteException remote ? remote.RemoteClassName : exception.GetType().FullName ?? "Exception";
        writer.WriteString(className);
        writer.WriteString(exception.Message);

        if (exception is RasRemoteException rre && rre.RemoteStackTrace is { Count: > 0 } frames)
        {
            writer.WriteSize(frames.Count);
            foreach (var frame in frames)
            {
                writer.WriteString(frame.ClassName);
                writer.WriteString(frame.MethodName);
                writer.WriteString(frame.FileName);
                writer.WriteInt32(frame.LineNumber);
            }
        }
        else
        {
            writer.WriteSize(0);
        }

        WriteThrowable(ref writer, exception.InnerException);
    }

    public static Exception? ReadThrowable(ref RasReader reader) => ReadThrowableCore(ref reader, 0);

    private static Exception? ReadThrowableCore(ref RasReader reader, int depth)
    {
        if (depth > RasWireConstants.MaxThrowableDepth)
            throw new RasProtocolException("throwable cause chain too deep");

        string? className = reader.ReadString();
        if (className is null)
            return null;

        string message = reader.ReadString() ?? string.Empty;
        int size = reader.ReadListCount();

        var frames = new List<RasRemoteStackFrame>(Math.Min(size, 256));
        for (int i = 0; i < size; i++)
        {
            string clazz = reader.ReadString() ?? string.Empty;
            string method = reader.ReadString() ?? string.Empty;
            string? fileName = reader.ReadString();
            int line = reader.ReadInt32();
            frames.Add(new RasRemoteStackFrame(clazz, method, fileName, line));
        }

        Exception? cause = ReadThrowableCore(ref reader, depth + 1);
        return new RasRemoteException(className, message, frames, cause);
    }
}
