using System;
using System.Collections.Generic;
using OneSTools.Ras.Client.Exceptions;
using OneSTools.Ras.Client.Wire;

namespace OneSTools.Ras.Client.Protocol;

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

    public static Exception? ReadThrowable(ref RasReader reader)
    {
        string? className = reader.ReadString();
        if (className is null)
            return null;

        string message = reader.ReadString() ?? string.Empty;
        int size = reader.ReadSize();

        var frames = new List<RasRemoteStackFrame>(size);
        for (int i = 0; i < size; i++)
        {
            string clazz = reader.ReadString() ?? string.Empty;
            string method = reader.ReadString() ?? string.Empty;
            string? fileName = reader.ReadString();
            int line = reader.ReadInt32();
            frames.Add(new RasRemoteStackFrame(clazz, method, fileName, line));
        }

        Exception? cause = ReadThrowable(ref reader);
        return new RasRemoteException(className, message, frames, cause);
    }
}
