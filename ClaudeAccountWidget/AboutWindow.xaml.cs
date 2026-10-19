using System.Diagnostics;
using System.Reflection;
using System.Windows;
using ClaudeAccountWidget.Services;

namespace ClaudeAccountWidget;

public partial class AboutWindow : Window
{
    private const string ReleasesUrl =
        $"https://github.com/{UpdateCheckService.RepositoryPath}/releases?q={UpdateCheckService.TagPrefix}&expanded=true";

    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "알 수 없음";
        VersionText.Text = $"버전 {version}";
        ContentRendered += (_, _) => EntranceAnimation.Play(this);
    }

    private void OnGitHubClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"GitHub를 열지 못했습니다.\n{exception.Message}",
                "링크 열기 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
