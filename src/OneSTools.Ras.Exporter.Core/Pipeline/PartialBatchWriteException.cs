namespace OneSTools.Ras.Exporter.Core.Pipeline;

internal sealed class PartialBatchWriteException<TItem>(IReadOnlyList<TItem> failedItems, Exception innerException)
    : Exception("Не все группы батча записаны", innerException)
{
    public IReadOnlyList<TItem> FailedItems { get; } = failedItems;
}
