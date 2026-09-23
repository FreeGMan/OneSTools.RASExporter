using System;
using OneSTools.Ras.Client.Models;

namespace OneSTools.Ras.Exporter.Core.Storage;

/// <summary>Сводка состояния кластера для таблицы cluster_summary.</summary>
public sealed record ClusterSummaryMetricItem(
    DateTime Timestamp,
    Guid ClusterId,
    string ClusterName,
    ClusterInfo ClusterInfo,
    int SessionsCount,
    int ProcessesCount,
    int ConnectionsCount,
    int LocksCount);

/// <summary>Снимок сессии для таблицы sessions.</summary>
public sealed record SessionMetricItem(
    DateTime Timestamp,
    Guid ClusterId,
    string ClusterName,
    SessionInfo Session);

/// <summary>Снимок рабочего процесса для таблицы processes.</summary>
public sealed record ProcessMetricItem(
    DateTime Timestamp,
    Guid ClusterId,
    string ClusterName,
    ProcessInfo Process);

/// <summary>Снимок соединения для таблицы connections.</summary>
public sealed record ConnectionMetricItem(
    DateTime Timestamp,
    Guid ClusterId,
    string ClusterName,
    ConnectionInfo Connection);

/// <summary>Снимок блокировки для таблицы locks.</summary>
public sealed record LockMetricItem(
    DateTime Timestamp,
    Guid ClusterId,
    string ClusterName,
    LockInfo Lock);
