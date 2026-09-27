using System.Threading.Channels;

namespace ReceiptExtractor.Api.Extraction;

/// <summary>
/// In-memory queue of receipt IDs waiting for extraction.
/// Uploads return immediately; <see cref="ExtractionWorker"/> processes them one at a time in the background.
/// </summary>
public sealed class ExtractionQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(Guid receiptId, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(receiptId, ct);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
