using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClaudeAccountWidget.Models;
using ClaudeAccountWidget.Services;

namespace ClaudeAccountWidget.ViewModels;

public sealed class MainViewModel(
    ProfileStore store,
    ClaudeAccountService accounts,
    ClaudeProcessMonitor processes) : INotifyPropertyChanged
{
    private ProfileSettings _settings = new();
    private AccountProfile? _activeProfile;
    private AccountProfile? _switchingProfile;
    private string _message = "계정을 추가해 주세요";
    private string _switchStage = string.Empty;
    private int _switchStep;
    private bool _isBusy;
    private bool _isSwitching;
    private bool _hasSwitchError;
    private bool _canImportCurrent;
    private bool _showOnlyWhileClaudeIsRunning = true;
    private bool _startWithWindows = true;
    private bool _autoAdjustWidgetTextColor = true;
    private readonly SemaphoreSlim _accountOperationGate = new(1, 1);
    private CancellationTokenSource? _refreshCancellation;
    private CancellationTokenSource? _loginCancellation;

    public ObservableCollection<AccountProfile> Profiles { get; } = [];
    public AccountProfile? ActiveProfile
    {
        get => _activeProfile;
        private set
        {
            if (ReferenceEquals(_activeProfile, value)) return;
            _activeProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasActiveProfile));
            OnPropertyChanged(nameof(HasNoActiveProfile));
        }
    }

    public bool HasActiveProfile => ActiveProfile is not null;
    public bool HasNoActiveProfile => ActiveProfile is null;
    public AccountProfile? SwitchingProfile { get => _switchingProfile; private set => Set(ref _switchingProfile, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public string SwitchStage { get => _switchStage; private set => Set(ref _switchStage, value); }
    /// <summary>전환 진행 단계(0~4). 진행 화면의 단계 표시에 사용합니다.</summary>
    public int SwitchStep { get => _switchStep; private set => Set(ref _switchStep, value); }
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }
    public bool IsLoginPending => _loginCancellation is not null;
    public bool IsSwitching { get => _isSwitching; private set => Set(ref _isSwitching, value); }
    public bool HasSwitchError { get => _hasSwitchError; private set => Set(ref _hasSwitchError, value); }
    public bool CanImportCurrent { get => _canImportCurrent; private set => Set(ref _canImportCurrent, value); }
    public bool ShowOnlyWhileClaudeIsRunning { get => _showOnlyWhileClaudeIsRunning; private set => Set(ref _showOnlyWhileClaudeIsRunning, value); }
    public bool StartWithWindows { get => _startWithWindows; private set => Set(ref _startWithWindows, value); }
    public bool AutoAdjustWidgetTextColor { get => _autoAdjustWidgetTextColor; private set => Set(ref _autoAdjustWidgetTextColor, value); }

    public async Task InitializeAsync(bool refreshUsage = true)
    {
        _settings = await store.LoadAsync();
        ShowOnlyWhileClaudeIsRunning = _settings.ShowOnlyWhileClaudeIsRunning;
        StartWithWindows = _settings.StartWithWindows;
        AutoAdjustWidgetTextColor = _settings.AutoAdjustWidgetTextColor;
        foreach (var profile in _settings.Profiles) Profiles.Add(profile);

        SyncActiveWithClaudeCode();
        if (!refreshUsage || Profiles.Count == 0) return;
        await RefreshAllAsync();
    }

    /// <summary>
    /// 사용자가 터미널에서 /login으로 계정을 바꿨을 수도 있으므로, 실제 Claude Code 로그인 계정을 기준으로
    /// 활성 계정을 다시 맞춥니다.
    /// </summary>
    public void SyncActiveWithClaudeCode()
    {
        if (IsSwitching) return;
        var currentUuid = accounts.CurrentAccountUuid();
        var active = currentUuid is null
            ? null
            : Profiles.FirstOrDefault(profile =>
                string.Equals(profile.AccountUuid, currentUuid, StringComparison.OrdinalIgnoreCase));

        if (!ReferenceEquals(active, ActiveProfile))
        {
            ActiveProfile = active;
            _settings.ActiveProfileId = active?.Id;
            UpdateActiveFlags();
        }

        CanImportCurrent = active is null && currentUuid is not null && accounts.HasCurrentLogin();
        if (Profiles.Count == 0 && !CanImportCurrent) Message = "계정을 추가해 주세요";
        else if (CanImportCurrent && Profiles.Count == 0) Message = "현재 Claude Code 로그인을 등록할 수 있습니다";
    }

    public async Task SetShowOnlyWhileClaudeIsRunningAsync(bool value)
    {
        ShowOnlyWhileClaudeIsRunning = value;
        _settings.ShowOnlyWhileClaudeIsRunning = value;
        await PersistAsync();
    }

    public async Task SetStartWithWindowsAsync(bool value)
    {
        StartWithWindows = value;
        _settings.StartWithWindows = value;
        await PersistAsync();
    }

    public async Task SetAutoAdjustWidgetTextColorAsync(bool value)
    {
        AutoAdjustWidgetTextColor = value;
        _settings.AutoAdjustWidgetTextColor = value;
        await PersistAsync();
    }

    public void NotifyClockTick()
    {
        foreach (var profile in Profiles) profile.NotifyClockTick();
    }

    public Task RefreshAllAsync() => RefreshCoreAsync(activeOnly: false);

    public Task RefreshActiveAsync() => RefreshCoreAsync(activeOnly: true);

    private async Task RefreshCoreAsync(bool activeOnly)
    {
        if (IsSwitching || !await _accountOperationGate.WaitAsync(0)) return;

        using var refreshCancellation = new CancellationTokenSource();
        _refreshCancellation = refreshCancellation;
        IsBusy = true;
        if (!activeOnly) Message = "계정 사용량을 확인하고 있습니다";

        try
        {
            SyncActiveWithClaudeCode();
            var targets = activeOnly
                ? (ActiveProfile is null ? [] : new[] { ActiveProfile })
                : Profiles.ToArray();

            foreach (var profile in targets)
                await accounts.RefreshAsync(profile, refreshCancellation.Token);

            if (!activeOnly) Message = "최신 사용량";
            await PersistAsync();
        }
        catch (OperationCanceledException) when (refreshCancellation.IsCancellationRequested)
        {
            Message = "다른 작업을 위해 새로고침을 중지했습니다";
        }
        catch (Exception exception)
        {
            Message = $"사용량 새로고침 실패: {ShortError(exception)}";
        }
        finally
        {
            if (ReferenceEquals(_refreshCancellation, refreshCancellation))
                _refreshCancellation = null;
            IsBusy = false;
            _accountOperationGate.Release();
        }
    }

    public async Task AddAccountAsync()
    {
        if (IsSwitching || IsLoginPending) return;

        Message = "새 계정 로그인을 준비하고 있습니다";
        _refreshCancellation?.Cancel();
        var gateAcquired = false;
        AccountProfile? profile = null;

        try
        {
            await _accountOperationGate.WaitAsync();
            gateAcquired = true;
            IsBusy = true;
            profile = store.CreatePendingProfile();
            Profiles.Add(profile);

            using var loginCancellation = new CancellationTokenSource();
            _loginCancellation = loginCancellation;
            OnPropertyChanged(nameof(IsLoginPending));
            Message = "열린 창과 브라우저에서 로그인을 완료해 주세요";

            var success = await accounts.LoginAsync(profile, loginCancellation.Token);
            if (!success)
            {
                var failure = profile.Status;
                DiscardProfile(profile);
                Message = loginCancellation.IsCancellationRequested
                    ? "계정 추가를 취소했습니다"
                    : $"계정 추가 실패: {failure}";
                return;
            }

            if (MergeDuplicate(profile, out var existing))
            {
                await accounts.RefreshAsync(existing);
                Message = $"{existing.Email} 계정의 로그인을 새로 고쳤습니다";
                return;
            }

            SyncActiveWithClaudeCode();
            await accounts.RefreshAsync(profile);
            Message = "계정을 등록했습니다. 목록에서 눌러 전환하세요";
        }
        catch (Exception exception)
        {
            if (profile is not null) DiscardProfile(profile);
            Message = $"계정 추가 실패: {ShortError(exception)}";
        }
        finally
        {
            _loginCancellation = null;
            OnPropertyChanged(nameof(IsLoginPending));
            IsBusy = false;
            try { await PersistAsync(); }
            catch (Exception exception) { Message = $"계정 목록 저장 실패: {ShortError(exception)}"; }
            finally { if (gateAcquired) _accountOperationGate.Release(); }
        }
    }

    public async Task ImportCurrentAsync()
    {
        if (IsSwitching || IsLoginPending || !CanImportCurrent) return;
        _refreshCancellation?.Cancel();
        await _accountOperationGate.WaitAsync();
        AccountProfile? profile = null;

        try
        {
            IsBusy = true;
            profile = store.CreatePendingProfile();
            await accounts.ImportCurrentAsync(profile);
            if (MergeDuplicate(profile, out _))
            {
                Message = "이미 등록된 계정입니다";
                return;
            }

            Profiles.Add(profile);
            SyncActiveWithClaudeCode();
            await accounts.RefreshAsync(profile);
            Message = "현재 로그인된 계정을 등록했습니다";
        }
        catch (Exception exception)
        {
            if (profile is not null) DiscardProfile(profile);
            Message = $"가져오기 실패: {ShortError(exception)}";
        }
        finally
        {
            IsBusy = false;
            try { await PersistAsync(); }
            finally { _accountOperationGate.Release(); }
        }
    }

    public void CancelLogin()
    {
        if (_loginCancellation is null) return;
        Message = "로그인을 취소하고 있습니다";
        _loginCancellation.Cancel();
    }

    public int CountRunningCliSessions() => processes.FindCliSessions().Count;

    public async Task SwitchAsync(AccountProfile profile, bool stopCliSessions)
    {
        if (profile.IsActive || IsSwitching) return;

        SwitchingProfile = profile;
        IsSwitching = true;
        HasSwitchError = false;
        SetStage(0, "전환을 준비하고 있습니다");
        _refreshCancellation?.Cancel();
        var gateAcquired = false;

        try
        {
            await _accountOperationGate.WaitAsync();
            gateAcquired = true;
            IsBusy = true;
            // 진행 화면 연출이 보이도록 아주 짧게 쉬어 갑니다.
            await Task.Delay(280);

            var stopped = 0;
            if (stopCliSessions)
            {
                SetStage(1, "Claude Code 세션을 종료하고 있습니다");
                stopped = await processes.StopCliSessionsAsync();
            }

            SetStage(2, "현재 로그인을 안전하게 보관하고 있습니다");
            // 등록되지 않은 로그인 위에 덮어쓰는 경우에는 Activate가 백업 파일 한 개를 남깁니다.
            if (ActiveProfile is not null) accounts.SaveActiveLoginBack(ActiveProfile);
            await Task.Delay(180);

            SetStage(3, "인증을 전환하고 있습니다");
            accounts.Activate(profile);
            ActiveProfile = profile;
            _settings.ActiveProfileId = profile.Id;
            UpdateActiveFlags();
            await PersistAsync();

            SetStage(4, "전환 상태를 확인하고 있습니다");
            await Task.Delay(1200);
            if (!accounts.IsActivated(profile))
                throw new InvalidOperationException(
                    "실행 중인 Claude Code가 인증 파일을 이전 계정으로 되돌렸습니다. 세션을 종료한 뒤 다시 시도해 주세요.");

            CanImportCurrent = false;
            await accounts.RefreshAsync(profile);
            Message = stopped > 0
                ? $"전환 완료 · 종료한 세션 {stopped}개는 claude --continue 로 이어가세요"
                : "전환 완료 · 새로 여는 Claude Code부터 이 계정을 사용합니다";
        }
        catch (Exception exception)
        {
            HasSwitchError = true;
            Message = $"전환 실패: {exception.Message}";
            SyncActiveWithClaudeCode();
        }
        finally
        {
            IsBusy = false;
            IsSwitching = false;
            SwitchingProfile = null;
            SetStage(0, string.Empty);
            if (gateAcquired) _accountOperationGate.Release();
        }
    }

    public async Task RemoveAsync(AccountProfile profile)
    {
        if (profile.IsActive)
        {
            Message = "사용 중인 계정은 다른 계정으로 전환한 뒤 제거하세요";
            return;
        }

        DiscardProfile(profile);
        Message = "계정을 이 위젯에서 제거했습니다";
        SyncActiveWithClaudeCode();
        await PersistAsync();
    }

    public void ClearSwitchError()
    {
        if (HasSwitchError) HasSwitchError = false;
    }

    private bool MergeDuplicate(AccountProfile profile, out AccountProfile existing)
    {
        existing = Profiles.FirstOrDefault(other =>
            !ReferenceEquals(other, profile) &&
            !string.IsNullOrEmpty(profile.AccountUuid) &&
            string.Equals(other.AccountUuid, profile.AccountUuid, StringComparison.OrdinalIgnoreCase))!;
        if (existing is null) return false;

        // 같은 계정을 다시 로그인한 경우 새 인증으로 기존 항목을 갱신합니다(활성 계정이면 기본 인증은 그대로 둡니다).
        if (!existing.IsActive)
        {
            ClaudeCredentialStore.CopyCredentials(
                ClaudePaths.ProfileCredentialsPath(profile.HomePath),
                ClaudePaths.ProfileCredentialsPath(existing.HomePath));
        }

        DiscardProfile(profile);
        return true;
    }

    private void DiscardProfile(AccountProfile profile)
    {
        Profiles.Remove(profile);
        try { store.RemoveProfileDirectory(profile); }
        catch { }
    }

    private void SetStage(int step, string text)
    {
        SwitchStep = step;
        SwitchStage = text;
        if (!string.IsNullOrEmpty(text)) Message = text;
    }

    private void UpdateActiveFlags()
    {
        foreach (var profile in Profiles) profile.IsActive = ReferenceEquals(profile, ActiveProfile);
    }

    private Task PersistAsync()
    {
        _settings.Profiles = Profiles.ToList();
        _settings.ActiveProfileId = ActiveProfile?.Id;
        return store.SaveAsync(_settings);
    }

    private static string ShortError(Exception exception) =>
        exception.Message.Length > 80 ? exception.Message[..80] + "…" : exception.Message;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }
}
