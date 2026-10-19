using System.Windows;
using ClaudeAccountWidget.Models;

namespace ClaudeAccountWidget;

public partial class SwitchConfirmationWindow : Window
{
    public SwitchConfirmationWindow(AccountProfile profile, int runningSessions)
    {
        InitializeComponent();
        DataContext = new
        {
            Profile = profile,
            RunningSessions = runningSessions,
            HasRunningSessions = runningSessions > 0
        };
        ContentRendered += (_, _) => EntranceAnimation.Play(this);
    }

    /// <summary>사용자가 실행 중인 Claude Code CLI 세션 종료에 동의했는지 여부입니다.</summary>
    public bool StopCliSessions { get; private set; }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnConfirmClicked(object sender, RoutedEventArgs e)
    {
        StopCliSessions = StopSessionsCheckBox.IsVisible && StopSessionsCheckBox.IsChecked == true;
        DialogResult = true;
    }
}
