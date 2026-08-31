using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CodexAccountWidget.Models;

public sealed class ModelProviderOption(string id, string displayName) : INotifyPropertyChanged
{
    private bool _isActive;

    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
