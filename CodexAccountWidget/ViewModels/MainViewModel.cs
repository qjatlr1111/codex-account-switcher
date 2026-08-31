using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using CodexAccountWidget.Models;
using CodexAccountWidget.Services;

namespace CodexAccountWidget.ViewModels;

public sealed class MainViewModel(
    ProfileStore store,
    CodexAccountService accounts,
    CodexDesktopRestartService restarter) : INotifyPropertyChanged
{
    private ProfileSettings _settings = new();
    private AccountProfile? _activeProfile;
    private AccountProfile? _switchingProfile;
    private string _message = "계정을 추가해 주세요";
    private bool _isBusy;
    private bool _isSwitching;
    private bool _hasSwitchError;
    private bool _showOnlyWhileCodexIsRunning = true;
    private bool _startWithWindows = true;
    private readonly SemaphoreSlim _accountOperationGate = new(1, 1);
    private CancellationTokenSource? _refreshCancellation;

    public ObservableCollection<AccountProfile> Profiles { get; } = [];
    public AccountProfile? ActiveProfile { get => _activeProfile; private set => Set(ref _activeProfile, value); }
    public AccountProfile? SwitchingProfile { get => _switchingProfile; private set => Set(ref _switchingProfile, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }
    public bool IsSwitching { get => _isSwitching; private set => Set(ref _isSwitching, value); }
    public bool HasSwitchError { get => _hasSwitchError; private set => Set(ref _hasSwitchError, value); }
    public bool ShowOnlyWhileCodexIsRunning { get => _showOnlyWhileCodexIsRunning; private set => Set(ref _showOnlyWhileCodexIsRunning, value); }
    public bool StartWithWindows { get => _startWithWindows; private set => Set(ref _startWithWindows, value); }

    public async Task InitializeAsync(bool refreshUsage = true)
    {
        _settings = await store.LoadAsync();
        ShowOnlyWhileCodexIsRunning = _settings.ShowOnlyWhileCodexIsRunning;
        StartWithWindows = _settings.StartWithWindows;
        foreach (var profile in _settings.Profiles) Profiles.Add(profile);

        ActiveProfile = Profiles.FirstOrDefault(p => p.Id == _settings.ActiveProfileId)
                        ?? Profiles.FirstOrDefault();
        UpdateActiveFlags();

        if (!refreshUsage || Profiles.Count == 0) return;
        await RefreshAllAsync();
    }

    public async Task SetShowOnlyWhileCodexIsRunningAsync(bool value)
    {
        ShowOnlyWhileCodexIsRunning = value;
        _settings.ShowOnlyWhileCodexIsRunning = value;
        await PersistAsync();
    }

    public async Task SetStartWithWindowsAsync(bool value)
    {
        StartWithWindows = value;
        _settings.StartWithWindows = value;
        await PersistAsync();
    }

    public async Task RefreshAllAsync()
    {
        if (!await _accountOperationGate.WaitAsync(0)) return;

        using var refreshCancellation = new CancellationTokenSource();
        _refreshCancellation = refreshCancellation;
        IsBusy = true;
        Message = "계정 사용량을 확인하고 있습니다";

        try
        {
            foreach (var profile in Profiles)
                await accounts.RefreshAsync(profile, refreshCancellation.Token);

            Message = "최신 사용량";
            await PersistAsync();
        }
        catch (OperationCanceledException) when (refreshCancellation.IsCancellationRequested)
        {
            Message = "계정 전환을 준비하고 있습니다";
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

    public async Task RefreshActiveFromCurrentAccountAsync(
        CodexAppServerClient client,
        CancellationToken cancellationToken = default)
    {
        var profile = ActiveProfile;
        if (profile is null || IsSwitching ||
            !await _accountOperationGate.WaitAsync(0, cancellationToken)) return;

        try
        {
            await accounts.RefreshCurrentIfMatchingAsync(profile, client, cancellationToken);
        }
        finally
        {
            _accountOperationGate.Release();
        }
    }

    public async Task AddAccountAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        AccountProfile? profile = null;

        try
        {
            profile = store.CreatePendingProfile();
            Profiles.Add(profile);
            var success = await accounts.LoginAsync(profile);
            if (!success)
            {
                Profiles.Remove(profile);
                store.RemoveProfileDirectory(profile);
                Message = "로그인이 완료되지 않았습니다";
                return;
            }

            await accounts.RefreshAsync(profile);
            Message = "계정을 등록했습니다. 전환하려면 목록에서 계정을 선택하세요";
        }
        catch (Exception exception)
        {
            if (profile is not null) Profiles.Remove(profile);
            Message = $"계정 추가 실패: {ShortError(exception)}";
        }
        finally
        {
            IsBusy = false;
            try
            {
                await PersistAsync();
            }
            catch (Exception exception)
            {
                HasSwitchError = true;
                Message = $"계정 목록 저장 실패: {ShortError(exception)}";
            }
        }
    }

    public async Task SwitchAsync(AccountProfile profile)
    {
        if (profile.IsActive || IsSwitching) return;
        if (IsBusy && _refreshCancellation is null) return;

        Message = _refreshCancellation is null
            ? "계정 전환을 준비하고 있습니다"
            : "진행 중인 사용량 확인을 중지하고 있습니다";
        SwitchingProfile = profile;
        IsSwitching = true;
        HasSwitchError = false;
        _refreshCancellation?.Cancel();

        var gateAcquired = false;
        CodexLaunchTarget? launchTarget = null;
        var launchAttempted = false;

        try
        {
            await _accountOperationGate.WaitAsync();
            gateAcquired = true;
            if (profile.IsActive) return;

            IsBusy = true;
            Message = "Codex를 종료하고 있습니다";
            launchTarget = await restarter.StopAsync();

            Message = "계정 인증을 전환하고 있습니다";
            await accounts.ActivateForNextCodexLaunchAsync(profile);
            ActiveProfile = profile;
            _settings.ActiveProfileId = profile.Id;
            UpdateActiveFlags();
            await PersistAsync();

            Message = "새 계정으로 Codex를 다시 실행합니다";
            launchAttempted = true;
            await restarter.StartAsync(launchTarget);
            Message = "계정 전환 및 Codex 재시작 완료";
        }
        catch (Exception exception)
        {
            HasSwitchError = true;
            Message = $"전환 실패: {exception.Message}";

            if (launchTarget is not null && !launchAttempted)
            {
                try
                {
                    launchAttempted = true;
                    await restarter.StartAsync(launchTarget);
                    Message += " · Codex는 다시 실행했습니다";
                }
                catch (Exception restartException)
                {
                    Message += $" · 재실행도 실패: {restartException.Message}";
                }
            }
        }
        finally
        {
            IsBusy = false;
            IsSwitching = false;
            SwitchingProfile = null;
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

        Profiles.Remove(profile);
        store.RemoveProfileDirectory(profile);
        Message = "계정을 이 위젯에서 제거했습니다";
        await PersistAsync();
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

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
