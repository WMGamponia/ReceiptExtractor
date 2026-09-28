using Microsoft.Extensions.Options;
using ReceiptExtractor.Api.LiveDemo;

namespace ReceiptExtractor.Tests;

public sealed class ReadGateTests
{
    private static ReadGate Gate(int maxWaiting = 3, int budget = 100) =>
        new(Options.Create(new LiveDemoOptions { MaxWaiting = maxWaiting, Budget = budget }));

    [Fact]
    public async Task One_read_at_a_time()
    {
        var gate = Gate();
        var first = await gate.WaitTurnAsync(CancellationToken.None);
        var second = gate.WaitTurnAsync(CancellationToken.None);

        await Task.Delay(50);
        Assert.False(second.IsCompleted);

        first.Turn!.Dispose();
        Assert.NotNull((await second.WaitAsync(TimeSpan.FromSeconds(5))).Turn);
    }

    [Fact]
    public async Task Too_many_waiting_is_busy()
    {
        var gate = Gate(maxWaiting: 1);
        var reading = await gate.WaitTurnAsync(CancellationToken.None);
        var waiting = gate.WaitTurnAsync(CancellationToken.None);

        var third = await gate.WaitTurnAsync(CancellationToken.None);

        Assert.Equal(ReadGate.Refusal.Busy, third.Refused);
        Assert.Null(third.Turn);
        Assert.Equal(2, gate.InLine);
        reading.Turn!.Dispose();
        (await waiting).Turn!.Dispose();
        Assert.Equal(0, gate.InLine);
    }

    [Fact]
    public async Task The_budget_runs_out()
    {
        var gate = Gate(budget: 2);
        (await gate.WaitTurnAsync(CancellationToken.None)).Turn!.Dispose();
        (await gate.WaitTurnAsync(CancellationToken.None)).Turn!.Dispose();

        Assert.Equal(ReadGate.Refusal.BudgetUsed, (await gate.WaitTurnAsync(CancellationToken.None)).Refused);
        Assert.Equal(0, gate.Left);
    }

    [Fact]
    public async Task A_budget_that_runs_out_while_waiting_frees_the_line()
    {
        var gate = Gate(budget: 1);
        var reading = await gate.WaitTurnAsync(CancellationToken.None);
        var waiting = gate.WaitTurnAsync(CancellationToken.None);

        reading.Turn!.Dispose();

        Assert.Equal(ReadGate.Refusal.BudgetUsed, (await waiting).Refused);
        Assert.Equal(0, gate.InLine);
        Assert.Equal(1, gate.Used);
    }

    [Fact]
    public async Task Leaving_while_waiting_frees_the_place_and_costs_nothing()
    {
        var gate = Gate(maxWaiting: 1, budget: 5);
        var reading = await gate.WaitTurnAsync(CancellationToken.None);
        using var leave = new CancellationTokenSource();
        var waiting = gate.WaitTurnAsync(leave.Token);

        leave.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Equal(1, gate.InLine); // only the one being read
        Assert.Equal(1, gate.Used);
        reading.Turn!.Dispose();
    }

    [Fact]
    public async Task Ending_a_turn_twice_releases_once()
    {
        var gate = Gate();
        var turn = (await gate.WaitTurnAsync(CancellationToken.None)).Turn!;
        turn.Dispose();
        turn.Dispose();

        var a = await gate.WaitTurnAsync(CancellationToken.None);
        var b = gate.WaitTurnAsync(CancellationToken.None);
        await Task.Delay(50);

        Assert.False(b.IsCompleted); // still one at a time
        a.Turn!.Dispose();
        (await b).Turn!.Dispose();
    }
}
