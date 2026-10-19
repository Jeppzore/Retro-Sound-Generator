using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using NightmaresSound.App;
using NightmaresSound.Core;
using Xunit;

namespace NightmaresSound.DesktopTests;

public sealed partial class DesktopTests
{
    [Fact]
    public Task EveryAcousticInstrumentCanChangeMusicalControlsWhileLoopingWithoutValidationExceptions() => OnUi(async () =>
    {
        using var vm = new MainViewModel(new SilentPreview());
        await vm.InitializeAsync();
        var thrown = new List<Exception>();
        void OnException(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (args.Exception is ArgumentException or System.IO.InvalidDataException) thrown.Add(args.Exception);
        }
        AppDomain.CurrentDomain.FirstChanceException += OnException;
        try
        {
            foreach (var instrument in InstrumentCatalog.All.Where(i => i.HasAcoustic))
            {
                vm.SelectedProfile = SoundCatalog.Profiles.First(p => p.Id == "clink");
                vm.SelectedInstrument = instrument;
                vm.InstrumentStyle = InstrumentStyle.Acoustic;
                vm.Loop = true;
                foreach (var mode in Enum.GetValues<InstrumentMode>())
                {
                    vm.InstrumentMode = mode;
                    foreach (int key in Enumerable.Range(0, 12))
                    {
                        vm.InstrumentKey = key;
                        foreach (int octave in new[] { 3, 4, 5 }) vm.InstrumentOctave = octave;
                        foreach (var scale in Enum.GetValues<MusicalScale>()) vm.InstrumentScale = scale;
                        foreach (var phrase in instrument.Phrases) vm.InstrumentPhrase = phrase;
                        RecipeValidator.Validate(vm.Current);
                    }
                }
            }
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= OnException; }
        Assert.Empty(thrown);
        await WaitForRender(vm);
    });

    [Fact]
    public Task OutOfRangeKeyDuringAcousticLoopShowsMessageWithoutThrowingOrStoppingPlayback() => OnUi(async () =>
    {
        using var vm = new MainViewModel(new SilentPreview());
        await vm.InitializeAsync();
        vm.SelectedInstrument = InstrumentCatalog.Get("recorder");
        vm.InstrumentStyle = InstrumentStyle.Acoustic;
        vm.InstrumentMode = InstrumentMode.Phrase;
        vm.Loop = true;
        await WaitForRender(vm);
        vm.PlayCommand.Execute(null);
        string before = RecipeFile.Serialize(vm.Current);
        var sound = vm.Sound;
        var thrown = new List<Exception>();
        void OnException(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (args.Exception is ArgumentException or System.IO.InvalidDataException) thrown.Add(args.Exception);
        }
        AppDomain.CurrentDomain.FirstChanceException += OnException;
        try { Assert.Null(Record.Exception(() => vm.InstrumentKey = 11)); }
        finally { AppDomain.CurrentDomain.FirstChanceException -= OnException; }
        Assert.Empty(thrown);
        Assert.Equal(before, RecipeFile.Serialize(vm.Current));
        Assert.Same(sound, vm.Sound);
        Assert.True(vm.IsPlaying);
        Assert.Contains("acoustic", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        vm.InstrumentKey = 5;
        await WaitForRender(vm);
        Assert.Equal("", vm.ErrorMessage);
        Assert.Equal(5, vm.InstrumentKey);
        vm.UndoCommand.Execute(null);
        Assert.Equal(before, RecipeFile.Serialize(vm.Current));
        await WaitForRender(vm);
    });

    [Fact]
    public Task RejectedKeySelectionRestoresBoundComboBoxAndValidSelectionRecovers() => OnUi(async () =>
    {
        using var vm = new MainViewModel(new SilentPreview());
        await vm.InitializeAsync();
        vm.SelectedInstrument = InstrumentCatalog.Get("recorder");
        vm.InstrumentStyle = InstrumentStyle.Acoustic;
        vm.InstrumentMode = InstrumentMode.Phrase;
        vm.Loop = true;
        var combo = new ComboBox { DataContext = vm, ItemsSource = vm.MusicalKeys };
        combo.SetBinding(ComboBox.SelectedIndexProperty, new Binding(nameof(MainViewModel.InstrumentKey)) { Mode = BindingMode.TwoWay });
        Assert.Equal(2, combo.SelectedIndex);
        combo.SetCurrentValue(ComboBox.SelectedIndexProperty, 11);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.Equal(2, combo.SelectedIndex);
        Assert.Equal(2, vm.InstrumentKey);
        Assert.NotEmpty(vm.ErrorMessage);
        combo.SetCurrentValue(ComboBox.SelectedIndexProperty, 5);
        await WaitForRender(vm);
        Assert.Equal(5, vm.InstrumentKey);
        Assert.Equal("", vm.ErrorMessage);
    });

    [Fact]
    public Task SharedAndPerVoiceAcousticEditsDoNotThrowAtFirstChance() => OnUi(async () =>
    {
        using var vm = new MainViewModel(new SilentPreview());
        await vm.InitializeAsync();
        vm.SelectedInstrument = InstrumentCatalog.Get("harp");
        var voice = vm.InstrumentVoices[1];
        voice.SelectedInstrument = InstrumentCatalog.Get("recorder");
        voice.Style = InstrumentStyle.Acoustic;
        voice.Enabled = true;
        vm.InstrumentMode = InstrumentMode.Phrase;
        vm.Loop = true;
        string before = RecipeFile.Serialize(vm.Current);
        var thrown = new List<Exception>();
        void OnException(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (args.Exception is ArgumentException or System.IO.InvalidDataException) thrown.Add(args.Exception);
        }
        AppDomain.CurrentDomain.FirstChanceException += OnException;
        try
        {
            Assert.Null(Record.Exception(() => vm.InstrumentKey = 11));
            Assert.Null(Record.Exception(() => voice.Octave = 3));
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= OnException; }
        Assert.Empty(thrown);
        Assert.Equal(before, RecipeFile.Serialize(vm.Current));
        Assert.Contains("voice 2", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        voice.Style = InstrumentStyle.Retro;
        vm.InstrumentKey = 11;
        Assert.Equal(11, vm.InstrumentKey);
        await WaitForRender(vm);
        Assert.Equal(new SoundRenderer().Render(vm.Current).Pcm16, vm.Sound!.Pcm16);
    });
}
