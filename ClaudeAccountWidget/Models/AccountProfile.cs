using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace ClaudeAccountWidget.Models;

public sealed class AccountProfile : INotifyPropertyChanged
{
    public const int LowThreshold = 15;

    private string _email = "로그인 확인 중";
    private string _planType = "unknown";
    private string _organizationName = string.Empty;
    private int? _primaryRemaining;
    private int? _secondaryRemaining;
    private DateTimeOffset? _primaryResetsAt;
    private DateTimeOffset? _secondaryResetsAt;
    private string _extraUsageText = string.Empty;
    private string _status = "대기 중";
    private bool _isActive;
    private bool _isBusy;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string HomePath { get; set; } = string.Empty;
    public string AccountUuid { get; set; } = string.Empty;
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;

    public string Email { get => _email; set => Set(ref _email, value); }
    public string OrganizationName { get => _organizationName; set => Set(ref _organizationName, value); }

    public string PlanType
    {
        get => _planType;
        set { if (Set(ref _planType, value)) OnPropertyChanged(nameof(PlanLabel)); }
    }

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

    public DateTimeOffset? PrimaryResetsAt
    {
        get => _primaryResetsAt;
        set
        {
            if (!Set(ref _primaryResetsAt, value)) return;
            OnPropertyChanged(nameof(PrimaryResetText));
            OnPropertyChanged(nameof(PrimaryResetLine));
            OnPropertyChanged(nameof(PrimaryText));
            OnPropertyChanged(nameof(PrimaryPercentText));
            OnPropertyChanged(nameof(PrimaryGauge));
            OnPropertyChanged(nameof(IsLow));
        }
    }

    public DateTimeOffset? SecondaryResetsAt
    {
        get => _secondaryResetsAt;
        set
        {
            if (!Set(ref _secondaryResetsAt, value)) return;
            OnPropertyChanged(nameof(SecondaryResetText));
            OnPropertyChanged(nameof(SecondaryText));
            OnPropertyChanged(nameof(SecondaryPercentText));
            OnPropertyChanged(nameof(SecondaryGauge));
            OnPropertyChanged(nameof(IsLow));
        }
    }

    public string ExtraUsageText { get => _extraUsageText; set => Set(ref _extraUsageText, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }

    [JsonIgnore]
    public string PlanLabel => PlanType == "unknown" || string.IsNullOrWhiteSpace(PlanType)
        ? "CLAUDE"
        : PlanType.ToUpperInvariant();

    [JsonIgnore] public string PrimaryText => $"5시간 {PrimaryPercentText}";
    [JsonIgnore] public string SecondaryText => $"주간 {SecondaryPercentText}";
    [JsonIgnore] public string PrimaryPercentText => CurrentPrimary is null ? "--" : $"{CurrentPrimary}%";
    [JsonIgnore] public string SecondaryPercentText => CurrentSecondary is null ? "--" : $"{CurrentSecondary}%";

    /// <summary>게이지 컨트롤용 값입니다. 알 수 없으면 -1을 돌려줍니다.</summary>
    [JsonIgnore] public double PrimaryGauge => CurrentPrimary ?? -1;
    [JsonIgnore] public double SecondaryGauge => CurrentSecondary ?? -1;

    [JsonIgnore]
    public bool IsLow => CurrentPrimary is <= LowThreshold || CurrentSecondary is <= LowThreshold;

    // 초기화 시각이 지난 값은 다음 조회 전까지 "알 수 없음"으로 표시합니다.
    private int? CurrentPrimary => IsPast(PrimaryResetsAt) ? null : PrimaryRemaining;
    private int? CurrentSecondary => IsPast(SecondaryResetsAt) ? null : SecondaryRemaining;
    private static bool IsPast(DateTimeOffset? resetsAt) => resetsAt is { } at && at <= DateTimeOffset.Now;

    [JsonIgnore] public string PrimaryResetText => FormatReset(PrimaryResetsAt);
    [JsonIgnore] public string SecondaryResetText => FormatReset(SecondaryResetsAt);

    [JsonIgnore]
    public string PrimaryResetLine => PrimaryResetsAt is null ? string.Empty : $"  ·  5시간 창 {PrimaryResetText} 초기화";

    /// <summary>남은 시간 문구는 시간이 지나며 바뀌므로 주기적으로 다시 알립니다.</summary>
    public void NotifyClockTick()
    {
        OnPropertyChanged(nameof(PrimaryResetText));
        OnPropertyChanged(nameof(SecondaryResetText));
        OnPropertyChanged(nameof(PrimaryResetLine));
        OnPropertyChanged(nameof(PrimaryText));
        OnPropertyChanged(nameof(SecondaryText));
        OnPropertyChanged(nameof(PrimaryPercentText));
        OnPropertyChanged(nameof(SecondaryPercentText));
        OnPropertyChanged(nameof(PrimaryGauge));
        OnPropertyChanged(nameof(SecondaryGauge));
        OnPropertyChanged(nameof(IsLow));
    }

    private static string FormatReset(DateTimeOffset? resetsAt)
    {
        if (resetsAt is null) return string.Empty;
        var remaining = resetsAt.Value - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero) return "곧 초기화";
        if (remaining.TotalDays >= 1) return $"{(int)remaining.TotalDays}일 {remaining.Hours}시간 후";
        if (remaining.TotalHours >= 1) return $"{(int)remaining.TotalHours}시간 {remaining.Minutes}분 후";
        return $"{Math.Max(1, remaining.Minutes)}분 후";
    }

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
