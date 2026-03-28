using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Transkript.Views;

public partial class HomeWindow : Window
{
    public bool ShouldLogout { get; private set; }

    public HomeWindow()
    {
        InitializeComponent();
        PointerPressed += (_, e) => BeginMoveDrag(e);
    }

    public void SetUser(string email, string plan)
    {
        TxtEmail.Text = email;

        switch (plan)
        {
            case "pro":
                TxtPlan.Text            = "PRO";
                PlanBadge.Background    = new SolidColorBrush(Color.Parse("#F0FDF4"));
                PlanBadge.BorderBrush   = new SolidColorBrush(Color.Parse("#BBF7D0"));
                PlanBadge.BorderThickness = new Avalonia.Thickness(1);
                TxtPlan.Foreground      = new SolidColorBrush(Color.Parse("#16A34A"));
                TxtStatusMsg.Text       = "Votre accès Pro est actif.";
                TxtStatusMsg.Foreground = new SolidColorBrush(Color.Parse("#16A34A"));
                BtnLaunch.IsVisible     = true;
                PnlUpgrade.IsVisible    = false;
                break;

            case "beta":
                TxtPlan.Text            = "BETA";
                PlanBadge.Background    = new SolidColorBrush(Color.Parse("#FFFBEB"));
                PlanBadge.BorderBrush   = new SolidColorBrush(Color.Parse("#FDE68A"));
                PlanBadge.BorderThickness = new Avalonia.Thickness(1);
                TxtPlan.Foreground      = new SolidColorBrush(Color.Parse("#D97706"));
                TxtStatusMsg.Text       = "Accès Pro requis.";
                TxtStatusMsg.Foreground = new SolidColorBrush(Color.Parse("#9B9B9B"));
                BtnLaunch.IsVisible     = false;
                PnlUpgrade.IsVisible    = true;
                break;

            default:
                TxtPlan.Text            = "FREE";
                PlanBadge.Background    = new SolidColorBrush(Color.Parse("#F5F5F5"));
                PlanBadge.BorderBrush   = new SolidColorBrush(Color.Parse("#EBEBEB"));
                PlanBadge.BorderThickness = new Avalonia.Thickness(1);
                TxtPlan.Foreground      = new SolidColorBrush(Color.Parse("#6B6B6B"));
                TxtStatusMsg.Text       = "Accès Pro requis.";
                TxtStatusMsg.Foreground = new SolidColorBrush(Color.Parse("#9B9B9B"));
                BtnLaunch.IsVisible     = false;
                PnlUpgrade.IsVisible    = true;
                break;
        }
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Tag = false;
        Close();
    }

    private void BtnLaunch_Click(object? sender, RoutedEventArgs e)
    {
        Tag = true;
        Close();
    }

    private void BtnLogout_Click(object? sender, RoutedEventArgs e)
    {
        ShouldLogout = true;
        Tag = false;
        Close();
    }
}
