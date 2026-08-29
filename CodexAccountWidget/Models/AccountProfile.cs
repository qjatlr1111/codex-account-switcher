using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace CodexAccountWidget.Models;

public sealed class AccountProfile : INotifyPropertyChanged
{
    private string _email = "로그인 확인 중";
    private string _planType = "unknown";
    private int? _primaryRemaining;
    private int? _secondaryRemaining;
    private string _primaryLabel = "단기";
    private string _secondaryLabel = "주간";
    private string _status = "대기 중";
    private bool _isActive;
    private bool _isBusy;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "Codex 계정";
    public string HomePath { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;

    public string Email { get => _email; set => Set(ref _email, value); }
    public string PlanType { get => _planType; set { if (Set(ref _planType, value)) OnPropertyChanged(nameof(PlanLabel)); } }
    public int? PrimaryRemaining { get => _primaryRemaining; set { if (Set(ref _primaryRemaining, value)) OnPropertyChanged(nameof(PrimaryText)); } }
    public int? SecondaryRemaining { get => _secondaryRemaining; set { if (Set(ref _secondaryRemaining, value)) OnPropertyChanged(nameof(SecondaryText)); } }
    public string PrimaryLabel { get => _primaryLabel; set { if (Set(ref _primaryLabel, value)) OnPropertyChanged(nameof(PrimaryText)); } }
    public string SecondaryLabel { get => _secondaryLabel; set { if (Set(ref _secondaryLabel, value)) OnPropertyChanged(nameof(SecondaryText)); } }
    public string Status { get => _status; set => Set(ref _status, value); }
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }

    [JsonIgnore]
    public string PlanLabel => PlanType == "unknown" ? "" : PlanType.ToUpperInvariant();

    [JsonIgnore]
    public string PrimaryText => PrimaryRemaining is null ? $"{PrimaryLabel} --" : $"{PrimaryLabel} {PrimaryRemaining}%";

    [JsonIgnore]
    public string SecondaryText => SecondaryRemaining is null ? $"{SecondaryLabel} --" : $"{SecondaryLabel} {SecondaryRemaining}%";

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
