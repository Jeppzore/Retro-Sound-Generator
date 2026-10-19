using System.ComponentModel;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace NightmaresSound.App;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class UiCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

public sealed class AsyncUiCommand(Func<Task> execute, Action<Exception> onError, Func<bool>? canExecute = null) : ICommand
{
    private bool running;
    public bool CanExecute(object? parameter) => !running && (canExecute?.Invoke() ?? true);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        running = true; CommandManager.InvalidateRequerySuggested();
        try { await execute(); }
        catch (Exception ex) { onError(ex); }
        finally { running = false; CommandManager.InvalidateRequerySuggested(); }
    }
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

public sealed class NumberParameter(string label, string unit, double min, double max, double step,
    string description, Func<double> read, Action<double> write,
    Func<(double Minimum, double Maximum)>? bounds = null, SoundConcept concept = SoundConcept.Output,
    Func<bool>? isRefreshing = null) : ObservableObject, INotifyDataErrorInfo
{
    private string? text, inputError;
    private bool refreshing;
    public string Label { get; } = label;
    public string Unit { get; } = unit;
    public double Minimum => bounds?.Invoke().Minimum ?? min;
    public double Maximum => bounds?.Invoke().Maximum ?? max;
    public bool IsEditable => Minimum < Maximum;
    public double Step { get; } = step;
    public string Description { get; } = description;
    public SoundConcept Concept { get; } = concept;
    public string TeachingDescription => $"{Semantic.Describe(Concept)} · {Description}";
    public bool HasErrors => inputError is not null;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    public IEnumerable GetErrors(string? propertyName) =>
        inputError is not null && (string.IsNullOrEmpty(propertyName) || propertyName == nameof(Text)) ? new[] { inputError } : Array.Empty<string>();
    private string RangeMessage => $"Enter a number from {Minimum.ToString("0.###############", CultureInfo.InvariantCulture)} to {Maximum.ToString("0.###############", CultureInfo.InvariantCulture)}.";

    // Slider arithmetic can overshoot an endpoint by a floating-point unit.
    // Keep the strict model setter, but never send an invalid UI value to it.
    public double SliderValue
    {
        get => Value;
        set
        {
            if (refreshing || isRefreshing?.Invoke() == true) return;
            if (double.IsFinite(value)) TryWrite(Math.Clamp(value, Minimum, Maximum));
            Notify(nameof(SliderValue));
        }
    }

    public string Text
    {
        get => text ?? Value.ToString("G", CultureInfo.InvariantCulture);
        set
        {
            if (refreshing || isRefreshing?.Invoke() == true) return;
            text = value;
            if ((!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                && !double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out number))
                || !double.IsFinite(number) || number < Minimum || number > Maximum)
                SetError(RangeMessage);
            else if (TryWrite(number)) text = null;
            Notify(nameof(Text));
        }
    }

    private bool TryWrite(double value)
    {
        try { Value = value; SetError(null); text = null; Notify(nameof(Text)); return true; }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        { SetError(ex.Message); return false; }
    }

    private void SetError(string? message)
    {
        if (inputError == message) return;
        inputError = message;
        ErrorsChanged?.Invoke(this, new(nameof(Text)));
        Notify(nameof(HasErrors));
    }
    public double Value
    {
        get => read();
        set
        {
            if (!double.IsFinite(value) || value < Minimum || value > Maximum)
                throw new ArgumentException(RangeMessage);
            if (Math.Abs(read() - value) < 1e-9) return;
            write(value);
            Notify();
            Notify(nameof(SliderValue));
            Notify(nameof(Text));
        }
    }
    public void Refresh()
    {
        // Updating bound limits can coerce a WPF slider before its value binding
        // refreshes. Such model-to-view updates must not create another edit.
        bool previous = refreshing;
        refreshing = true;
        try
        {
            text = null; SetError(null);
            Notify(nameof(Minimum)); Notify(nameof(Maximum)); Notify(nameof(IsEditable));
            Notify(nameof(Value)); Notify(nameof(SliderValue)); Notify(nameof(Text));
        }
        finally { refreshing = previous; }
    }
}

public sealed record ParameterGroup(string Title, IReadOnlyList<NumberParameter> Parameters);
