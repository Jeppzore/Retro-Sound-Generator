namespace NightmaresSound.Core;

/// <summary>Coalesces edits and cancels obsolete renders, without a UI dependency.</summary>
public sealed class RenderCoordinator : IDisposable
{
    private readonly object sync = new();
    private readonly Func<SoundRecipe, CancellationToken, RenderedSound?> render;
    private CancellationTokenSource? pending;
    private bool disposed;
    private int revision;

    public RenderCoordinator(Func<SoundRecipe, CancellationToken, RenderedSound>? render = null)
    {
        if (render is null) this.render = new SoundRenderer().TryRender;
        else this.render = render;
    }

    public async Task<RenderedSound?> UpdateAsync(SoundRecipe recipe, int debounceMilliseconds = 180)
    {
        CancellationTokenSource source;
        int version;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            pending?.Cancel();
            pending = source = new CancellationTokenSource();
            version = ++revision;
        }
        try
        {
            // Wake the debounce through a result rather than awaiting a cancelled task.
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = source.Token.Register(() => cancelled.TrySetResult());
            await Task.WhenAny(Task.Delay(debounceMilliseconds), cancelled.Task).ConfigureAwait(false);
            if (source.IsCancellationRequested) return null;
            var result = await Task.Run(() =>
            {
                if (source.IsCancellationRequested) return null;
                // Injected renderers may still use the throwing cancellation API.
                try { return render(recipe, source.Token); }
                catch (OperationCanceledException) when (source.IsCancellationRequested) { return null; }
            }).ConfigureAwait(false);
            lock (sync) return version == revision && !disposed ? result : null;
        }
        finally
        {
            lock (sync)
            {
                if (ReferenceEquals(pending, source)) pending = null;
                source.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            disposed = true;
            revision++;
            pending?.Cancel();
        }
    }
}
