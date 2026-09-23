using System;
using System.Collections.Generic;

namespace OneSTools.Ras.Client.Exceptions;

public class RasException : Exception
{
    public RasException(string message) : base(message) { }
    public RasException(string message, Exception? innerException) : base(message, innerException) { }
}

public sealed class RasRemoteException : RasException
{
    public string RemoteClassName { get; }
    public IReadOnlyList<RasRemoteStackFrame> RemoteStackTrace { get; }

    public RasRemoteException(
        string remoteClassName,
        string message,
        IReadOnlyList<RasRemoteStackFrame> stackTrace,
        Exception? innerException = null)
        : base($"Remote 1C exception [{remoteClassName}]: {message}", innerException)
    {
        RemoteClassName = remoteClassName;
        RemoteStackTrace = stackTrace;
    }
}

public readonly record struct RasRemoteStackFrame(string ClassName, string MethodName, string? FileName, int LineNumber);

/// <summary>
/// Повреждённые/враждебные данные в wire-потоке: отрицательная/запредельная длина,
/// переполнение varint, превышение лимитов протокола. Безопасный отказ вместо OOM/StackOverflow.
/// </summary>
public class RasProtocolException : RasException
{
    public RasProtocolException(string message) : base(message) { }
    public RasProtocolException(string message, Exception? innerException) : base(message, innerException) { }
}
