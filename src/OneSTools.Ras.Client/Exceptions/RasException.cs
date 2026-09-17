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
