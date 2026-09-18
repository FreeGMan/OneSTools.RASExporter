using System;
using OneSTools.Ras.Client.Models;

namespace OneSTools.Ras.Exporter.Core.Storage;

public sealed record ClusterSummaryMetricItem(
    DateTime Timestamp,
    Guid ClusterId,
    string ClusterName,
    ClusterInfo ClusterInfo,
    int SessionsCount,
    int ProcessesCount,
    int ConnectionsCount,
    int LocksCount);

public sealed record SessionMetricItem(
    DateTime Timestamp,
    Guid ClusterId,
    string ClusterName,
    SessionInfo Session);

public sealed record ProcessMetricItem(
    DateTime Timestamp,
    Guid ClusterId,
    string ClusterName,
    ProcessInfo Process);

public sealed record ConnectionMetricItem(
    DateTime Timestamp,
    Guid ClusterId,
    string ClusterName,
    ConnectionInfo Connection);

public sealed record LockMetricItem(
    DateTime Timestamp,
    Guid ClusterId,
    string ClusterName,
    LockInfo Lock);
