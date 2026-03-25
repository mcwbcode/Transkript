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
                PlanBadge.Background      = new SolidColorBrush(WpfColor.FromRgb(240, 253, 244));
                PlanBadge.BorderBrush     = new SolidColorBrush(WpfColor.FromRgb(187, 247, 208));
                PlanBadge.BorderThickness = new Thickness(1);
                TxtPlan.Foreground        = new SolidColorBrush(WpfColor.FromRgb(22, 163, 74));
                TxtStatusMsg.Text         = "Votre accès Pro est actif.";
                TxtStatusMsg.Foreground   = new SolidColorBrush(WpfColor.FromRgb(22, 163, 74));
                BtnLaunch.Visibility      = Visibility.Visible;
                PnlUpgrade.Visibility     = Visibility.Collapsed;
                break;

            case "beta":
                TxtPlan.Text              = "BETA";
                PlanBadge.Background      = new SolidColorBrush(WpfColor.FromRgb(255, 251, 235));
                PlanBadge.BorderBrush     = new SolidColorBrush(WpfColor.FromRgb(253, 230, 138));
                PlanBadge.BorderThickness = new Thickness(1);
                TxtPlan.Foreground        = new SolidColorBrush(WpfColor.FromRgb(217, 119, 6));
                TxtStatusMsg.Text         = "Accès Pro requis.";
                TxtStatusMsg.Foreground   = new SolidColorBrush(WpfColor.FromRgb(155, 155, 155));
                BtnLaunch.Visibility      = Visibility.Collapsed;
                PnlUpgrade.Visibility     = Visibility.Visible;
                break;

            default: // free
                TxtPlan.Text              = "FREE";
                PlanBadge.Background      = new SolidColorBrush(WpfColor.FromRgb(245, 245, 245));
                PlanBadge.BorderBrush     = new SolidColorBrush(WpfColor.FromRgb(235, 235, 235));
                PlanBadge.BorderThickness = new Thickness(1);
                TxtPlan.Foreground        = new SolidColorBrush(WpfColor.FromRgb(107, 107, 107));
                TxtStatusMsg.Text         = "Accès Pro requis.";
                TxtStatusMsg.Foreground   = new SolidColorBrush(WpfColor.FromRgb(155, 155, 155));
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
