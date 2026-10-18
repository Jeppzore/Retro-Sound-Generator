using System.ComponentModel;
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
    string description, Func<double> read, Action<double> write) : ObservableObject
{
    public string Label { get; } = label;
    public string Unit { get; } = unit;
    public double Minimum { get; } = min;
    public double Maximum { get; } = max;
    public double Step { get; } = step;
    public string Description { get; } = description;
    public double Value
    {
        get => read();
        set
        {
            if (!double.IsFinite(value) || value < Minimum || value > Maximum)
                throw new ArgumentException($"Enter a number from {Minimum} to {Maximum}.");
            if (Math.Abs(read() - value) < 1e-9) return;
            write(value);
            Notify();
        }
    }
    public void Refresh() => Notify(nameof(Value));
}

public sealed record ParameterGroup(string Title, IReadOnlyList<NumberParameter> Parameters);
