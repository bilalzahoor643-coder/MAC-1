using System;
using System.Windows.Input;

namespace MAC_1.Utils
{
    public class RelayCommand : ICommand
    {
        // Dono fields mein 'object?' hona lazmi hai
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        // Constructor mein bhi 'object?' use karein
        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        // ICommand Interface implementation
        public bool CanExecute(object? parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object? parameter)
        {
            _execute(parameter);
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}