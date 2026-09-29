using Microsoft.Extensions.Options;

namespace ReceiptExtractor.Api.LiveDemo;

/// <summary>
/// The GPU reads one photo at a time. A few more may wait their turn; past that, visitors are told it's busy.
/// At most <see cref="LiveDemoOptions.Budget"/> reads per run (counted in memory, so a new run starts a new budget).
/// </summary>
public sealed class ReadGate(IOptions<LiveDemoOptions> options)
{
    public enum Refusal { Busy, BudgetUsed }

    /// <summary>Either a turn (dispose it when the read is done) or why there isn't one.</summary>
    public sealed record Result(IDisposable? Turn, Refusal? Refused);

    private readonly SemaphoreSlim _gpu = new(1, 1);
    private int _inLine; // being read + waiting
    private int _used;

    public int InLine => Volatile.Read(ref _inLine);
    public int Used => Volatile.Read(ref _used);
    public int Left => Math.Max(0, options.Value.Budget - Used);

    public async Task<Result> WaitTurnAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (Used >= o.Budget)
            return new(null, Refusal.BudgetUsed);

        if (Interlocked.Increment(ref _inLine) > o.MaxWaiting + 1)
        {
            Interlocked.Decrement(ref _inLine);
            return new(null, Refusal.Busy);
        }

        try
        {
            await _gpu.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            Interlocked.Decrement(ref _inLine); // the visitor left while waiting
            throw;
        }

        if (Interlocked.Increment(ref _used) > o.Budget) // the budget ran out while this one waited
        {
            Interlocked.Decrement(ref _used);
            Release();
            return new(null, Refusal.BudgetUsed);
        }

        return new(new Turn(this), null);
    }

    private void Release()
    {
        Interlocked.Decrement(ref _inLine);
        _gpu.Release();
    }

    private sealed class Turn(ReadGate gate) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
                gate.Release();
        }
    }
}
