using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using NightmaresSound.Core;
using Xunit;

namespace NightmaresSound.Tests;

public sealed class RenderCancellationTests
{
    private static SoundRecipe ThreeVoices()
    {
        var recipe = SoundCatalog.Create("instrument-fiddle");
        return recipe with { DurationSeconds = 30, Channels = 2, SampleRate = 48000,
            Instrument = recipe.Instrument! with { Mode = InstrumentMode.Phrase, Tempo = 40, Voices =
            [new() { Id = "fiddle", Phrase = InstrumentPhrase.Melody }, new() { Id = "harp" }, new() { Id = "lute" }] } };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellingThreeVoiceBackgroundRenderingDoesNotThrowEvenAtFirstChance(bool dispose)
    {
        var thrown = new ConcurrentQueue<Exception>();
        void OnException(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (args.Exception is OperationCanceledException) thrown.Enqueue(args.Exception);
        }
        using var coordinator = new RenderCoordinator();
        AppDomain.CurrentDomain.FirstChanceException += OnException;
        try
        {
            var pending = coordinator.UpdateAsync(ThreeVoices(), 0);
            await Task.Delay(20);
            Assert.False(pending.IsCompleted, "The three-voice render must still be running when cancelled.");
            if (dispose) coordinator.Dispose();
            else
            {
                var latestRecipe = SoundCatalog.Create("confirm");
                var latest = await coordinator.UpdateAsync(latestRecipe, 0);
                Assert.NotNull(latest);
                Assert.Equal(new SoundRenderer().Render(latestRecipe).Pcm16, latest.Pcm16);
            }
            Assert.Null(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Empty(thrown);
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= OnException; }
    }

    [Fact]
    public async Task CancellingDebounceDoesNotThrowEvenAtFirstChance()
    {
        var thrown = new ConcurrentQueue<Exception>();
        void OnException(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (args.Exception is OperationCanceledException) thrown.Enqueue(args.Exception);
        }
        using var coordinator = new RenderCoordinator();
        AppDomain.CurrentDomain.FirstChanceException += OnException;
        try
        {
            var recipe = SoundCatalog.Create("confirm");
            var pending = coordinator.UpdateAsync(recipe, 500);
            Assert.NotNull(await coordinator.UpdateAsync(recipe with { Seed = 99 }, 0));
            Assert.Null(await pending);
            Assert.Empty(thrown);
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= OnException; }
    }

    [Fact]
    public void TryRenderReturnsNullOnCancellationAndKeepsValidationErrorsVisible()
    {
        var renderer = new SoundRenderer();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Null(renderer.TryRender(ThreeVoices(), cancelled.Token));
        using var duringRender = new CancellationTokenSource();
        duringRender.CancelAfter(10);
        Assert.Null(renderer.TryRender(ThreeVoices(), duringRender.Token));
        Assert.Throws<InvalidDataException>(() => renderer.TryRender(ThreeVoices() with { Channels = 3 }));
        Assert.Throws<OperationCanceledException>(() => renderer.Render(ThreeVoices(), cancelled.Token));
        var recipe = SoundCatalog.Create("confirm");
        Assert.Equal(renderer.Render(recipe).Pcm16, renderer.TryRender(recipe)!.Pcm16);
    }
}
