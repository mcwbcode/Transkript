using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace Transkript;

public partial class HomeWindow : Window
{
    public bool ShouldLogout { get; private set; }

    public HomeWindow()
    {
        InitializeComponent();
    }

    public void SetUser(string email, string plan)
    {
        TxtEmail.Text = email;

        switch (plan)
        {
            case "pro":
                TxtPlan.Text              = "PRO";
                PlanBadge.Background      = new SolidColorBrush(WpfColor.FromRgb(4, 26, 12));
                PlanBadge.BorderBrush     = new SolidColorBrush(WpfColor.FromRgb(15, 74, 37));
                PlanBadge.BorderThickness = new Thickness(1);
                TxtPlan.Foreground        = new SolidColorBrush(WpfColor.FromRgb(74, 222, 128));
                TxtStatusMsg.Text         = "Votre accès Pro est actif.";
                TxtStatusMsg.Foreground   = new SolidColorBrush(WpfColor.FromRgb(74, 222, 128));
                BtnLaunch.Visibility      = Visibility.Visible;
                PnlUpgrade.Visibility     = Visibility.Collapsed;
                break;

            case "beta":
                TxtPlan.Text              = "BETA";
                PlanBadge.Background      = new SolidColorBrush(WpfColor.FromRgb(29, 20, 0));
                PlanBadge.BorderBrush     = new SolidColorBrush(WpfColor.FromRgb(107, 72, 0));
                PlanBadge.BorderThickness = new Thickness(1);
                TxtPlan.Foreground        = new SolidColorBrush(WpfColor.FromRgb(251, 191, 36));
                TxtStatusMsg.Text         = "Accès Pro requis.";
                TxtStatusMsg.Foreground   = new SolidColorBrush(WpfColor.FromRgb(90, 90, 90));
                BtnLaunch.Visibility      = Visibility.Collapsed;
                PnlUpgrade.Visibility     = Visibility.Visible;
                break;

            default: // free
                TxtPlan.Text              = "FREE";
                PlanBadge.Background      = new SolidColorBrush(WpfColor.FromRgb(20, 20, 20));
                PlanBadge.BorderBrush     = new SolidColorBrush(WpfColor.FromRgb(50, 50, 50));
                PlanBadge.BorderThickness = new Thickness(1);
                TxtPlan.Foreground        = new SolidColorBrush(WpfColor.FromRgb(100, 100, 100));
                TxtStatusMsg.Text         = "Accès Pro requis.";
                TxtStatusMsg.Foreground   = new SolidColorBrush(WpfColor.FromRgb(80, 80, 80));
                BtnLaunch.Visibility      = Visibility.Collapsed;
                PnlUpgrade.Visibility     = Visibility.Visible;
                break;
        }
    }

    private void Root_MouseDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void BtnClose_Click(object sender, RoutedEventArgs e)    => DialogResult = false;
    private void BtnLaunch_Click(object sender, RoutedEventArgs e)   => DialogResult = true;

    private void BtnLogout_Click(object sender, RoutedEventArgs e)
    {
        ShouldLogout = true;
        DialogResult = false;
    }
}
