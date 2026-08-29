using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using CodexAccountWidget.Models;
using CodexAccountWidget.ViewModels;

namespace CodexAccountWidget;

public partial class AccountPanelWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly bool _diagnosticMode;
    private AccountProfile? _contextProfile;
    private bool _isConfirmationOpen;

    public AccountPanelWindow(MainViewModel viewModel, bool diagnosticMode = false)
    {
        InitializeComponent();
        _diagnosticMode = diagnosticMode;
        if (diagnosticMode) ShowInTaskbar = true;
        DataContext = _viewModel = viewModel;
        ContentRendered += (_, _) => RepositionToLastOwner();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private OverlayWindow? _lastOwner;

    public void Reposition(OverlayWindow overlay)
    {
        _lastOwner = overlay;
        RepositionToLastOwner();
    }

    private void RepositionToLastOwner()
    {
        if (_lastOwner is null) return;
        Left = _lastOwner.Left;
        Top = Math.Max(0, _lastOwner.Top - ActualHeight - 4);
    }

    private async void OnAddClicked(object sender, RoutedEventArgs e) => await _viewModel.AddAccountAsync();
    private async void OnRefreshClicked(object sender, RoutedEventArgs e) => await _viewModel.RefreshAllAsync();

    private async void OnAccountClicked(object sender, RoutedEventArgs e)
    {
        AccountMenuPopup.IsOpen = false;
        if (sender is not System.Windows.Controls.Button { DataContext: AccountProfile profile } ||
            profile.IsActive || _viewModel.IsSwitching)
            return;

        _isConfirmationOpen = true;
        try
        {
            var dialog = new SwitchConfirmationWindow(profile)
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true)
                await _viewModel.SwitchAsync(profile);
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

    private void OnDeactivated(object? sender, EventArgs e)
    {
        AccountMenuPopup.IsOpen = false;
        if (_diagnosticMode || _isConfirmationOpen || _viewModel.IsSwitching) return;
        if (IsVisible) Hide();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.IsSwitching)) return;
        Dispatcher.BeginInvoke(RepositionToLastOwner);
    }
}
