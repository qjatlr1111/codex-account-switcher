using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace CodexAccountWidget.Models;

public sealed class AccountProfile : INotifyPropertyChanged
{
    public const int LowThreshold = 15;

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

    public int? PrimaryRemaining
    {
        get => _primaryRemaining;
        set
        {
            if (!Set(ref _primaryRemaining, value)) return;
            OnPropertyChanged(nameof(PrimaryText));
            OnPropertyChanged(nameof(PrimaryGauge));
            OnPropertyChanged(nameof(PrimaryPercentText));
            OnPropertyChanged(nameof(IsLow));
        }
    }

    public int? SecondaryRemaining
    {
        get => _secondaryRemaining;
        set
        {
            if (!Set(ref _secondaryRemaining, value)) return;
            OnPropertyChanged(nameof(SecondaryText));
            OnPropertyChanged(nameof(SecondaryGauge));
            OnPropertyChanged(nameof(SecondaryPercentText));
            OnPropertyChanged(nameof(IsLow));
        }
    }

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

    [JsonIgnore] public string PrimaryPercentText => PrimaryRemaining is null ? "--" : $"{PrimaryRemaining}%";
    [JsonIgnore] public string SecondaryPercentText => SecondaryRemaining is null ? "--" : $"{SecondaryRemaining}%";

    /// <summary>게이지 컨트롤용 값입니다. 알 수 없으면 -1을 돌려줍니다.</summary>
    [JsonIgnore] public double PrimaryGauge => PrimaryRemaining ?? -1;
    [JsonIgnore] public double SecondaryGauge => SecondaryRemaining ?? -1;

    [JsonIgnore]
    public bool IsLow => PrimaryRemaining is <= LowThreshold || SecondaryRemaining is <= LowThreshold;

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
