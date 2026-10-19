using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ClaudeAccountWidget.Controls;
using ClaudeAccountWidget.Models;
using ClaudeAccountWidget.ViewModels;

namespace ClaudeAccountWidget;

public partial class AccountPanelWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly bool _diagnosticMode;
    private readonly Storyboard _shimmer;
    private readonly Storyboard _switching;
    private readonly Storyboard _refreshSpin;
    private AccountProfile? _contextProfile;
    private OverlayWindow? _lastOwner;
    private bool _isConfirmationOpen;
    private bool _isHiding;

    public AccountPanelWindow(MainViewModel viewModel, bool diagnosticMode = false)
    {
        InitializeComponent();
        _diagnosticMode = diagnosticMode;
        if (diagnosticMode) ShowInTaskbar = true;
        DataContext = _viewModel = viewModel;

        _shimmer = (Storyboard)Resources["ShimmerStoryboard"];
        _switching = (Storyboard)Resources["SwitchingStoryboard"];
        _refreshSpin = (Storyboard)Resources["RefreshSpinStoryboard"];

        ContentRendered += (_, _) => RepositionToLastOwner();
        SizeChanged += (_, _) => RepositionToLastOwner();
        IsVisibleChanged += (_, _) => UpdateAmbientAnimations();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    public void Reposition(OverlayWindow overlay)
    {
        _lastOwner = overlay;
        RepositionToLastOwner();
    }

    public void ShowAnimated(OverlayWindow overlay)
    {
        _lastOwner = overlay;
        _isHiding = false;
        PanelSurface.BeginAnimation(OpacityProperty, null);
        PanelSurface.Opacity = 0;
        Show();
        UpdateLayout();
        RepositionToLastOwner();
        Activate();

        EntranceAnimation.PlayPop(PanelSurface);
        PlayListEntrance();
    }

    public void HideAnimated()
    {
        if (_isHiding || !IsVisible) return;
        _isHiding = true;
        var fade = new DoubleAnimation(PanelSurface.Opacity, 0, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fade.Completed += (_, _) =>
        {
            if (!_isHiding) return;
            _isHiding = false;
            Hide();
        };
        PanelSurface.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>계정 카드가 차례로 떠오르고, 사용량 막대가 0에서부터 차오릅니다.</summary>
    private void PlayListEntrance()
    {
        Dispatcher.BeginInvoke(() =>
        {
            var containers = new List<FrameworkElement>();
            for (var index = 0; index < AccountList.Items.Count; index++)
            {
                if (AccountList.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container)
                    containers.Add(container);
            }

            EntranceAnimation.PlayStaggered(containers);
            for (var index = 0; index < containers.Count; index++)
            {
                var barIndex = 0;
                foreach (var bar in FindDescendants<UsageBar>(containers[index]))
                    bar.Replay(TimeSpan.FromMilliseconds(160 + index * 55 + barIndex++ * 70));
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void RepositionToLastOwner()
    {
        if (_lastOwner is null) return;
        Left = _lastOwner.Left;
        Top = Math.Max(0, _lastOwner.Top - ActualHeight - 2);
    }

    private bool _shimmerRunning;
    private bool _switchingRunning;
    private bool _refreshSpinRunning;

    private void UpdateAmbientAnimations()
    {
        // 보이지 않을 때는 반복 애니메이션을 멈춰 CPU 사용을 줄입니다.
        Toggle(_shimmer, IsVisible, ref _shimmerRunning);
        Toggle(_switching, IsVisible && _viewModel.IsSwitching, ref _switchingRunning);
        Toggle(_refreshSpin, IsVisible && _viewModel.IsBusy && !_viewModel.IsSwitching, ref _refreshSpinRunning);
    }

    private void Toggle(Storyboard storyboard, bool shouldRun, ref bool running)
    {
        if (shouldRun == running) return;
        running = shouldRun;
        if (shouldRun) storyboard.Begin(this, isControllable: true);
        else storyboard.Stop(this);
    }

    private async void OnAddClicked(object sender, RoutedEventArgs e) => await _viewModel.AddAccountAsync();
    private async void OnImportClicked(object sender, RoutedEventArgs e) => await _viewModel.ImportCurrentAsync();
    private void OnCancelLoginClicked(object sender, RoutedEventArgs e) => _viewModel.CancelLogin();

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshAllAsync();
        PlayListEntrance();
    }

    private async void OnAccountClicked(object sender, RoutedEventArgs e)
    {
        AccountMenuPopup.IsOpen = false;
        if (sender is not System.Windows.Controls.Button { DataContext: AccountProfile profile } ||
            profile.IsActive || _viewModel.IsSwitching)
            return;

        _isConfirmationOpen = true;
        try
        {
            var dialog = new SwitchConfirmationWindow(profile, _viewModel.CountRunningCliSessions()) { Owner = this };
            if (dialog.ShowDialog() == true)
                await _viewModel.SwitchAsync(profile, dialog.StopCliSessions);
        }
        finally
        {
            _isConfirmationOpen = false;
        }
    }

    private void OnAccountRightClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AccountProfile profile }) return;

        _contextProfile = profile;
        AccountMenuPopup.PlacementTarget = sender as UIElement;
        AccountMenuPopup.IsOpen = false;
        AccountMenuSurface.Opacity = 0;
        AccountMenuPopup.IsOpen = true;
        e.Handled = true;
    }

    private async void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        var profile = _contextProfile;
        AccountMenuPopup.IsOpen = false;
        _contextProfile = null;
        if (profile is not null) await _viewModel.RemoveAsync(profile);
    }

    private async void OnRefreshOneClicked(object sender, RoutedEventArgs e)
    {
        AccountMenuPopup.IsOpen = false;
        _contextProfile = null;
        await _viewModel.RefreshAllAsync();
    }

    private void OnAccountMenuOpened(object? sender, EventArgs e) =>
        EntranceAnimation.Play(AccountMenuSurface);

    private void OnDeactivated(object? sender, EventArgs e)
    {
        AccountMenuPopup.IsOpen = false;
        if (_diagnosticMode || _isConfirmationOpen || _viewModel.IsSwitching || _viewModel.IsLoginPending) return;
        HideAnimated();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsSwitching):
                Dispatcher.BeginInvoke(() =>
                {
                    UpdateAmbientAnimations();
                    RepositionToLastOwner();
                    if (!_viewModel.IsSwitching && IsVisible)
                    {
                        EntranceAnimation.PlayPop(PanelSurface, offset: 6);
                        PlayListEntrance();
                    }
                });
                break;
            case nameof(MainViewModel.IsBusy):
                Dispatcher.BeginInvoke(UpdateAmbientAnimations);
                break;
        }
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in FindDescendants<T>(child)) yield return nested;
        }
    }
}
