namespace NightmaresSound.Core;

/// <summary>Coalesces edits and cancels obsolete renders, without a UI dependency.</summary>
public sealed class RenderCoordinator : IDisposable
{
    private readonly object sync = new();
    private readonly Func<SoundRecipe, CancellationToken, RenderedSound> render;
    private CancellationTokenSource? pending;
    private bool disposed;
    private int revision;

    public RenderCoordinator(Func<SoundRecipe, CancellationToken, RenderedSound>? render = null)
        => this.render = render ?? new SoundRenderer().Render;

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
            await Task.Delay(debounceMilliseconds, source.Token).ConfigureAwait(false);
            var result = await Task.Run(() => render(recipe, source.Token), source.Token).ConfigureAwait(false);
            lock (sync) return version == revision && !disposed ? result : null;
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { return null; }
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
