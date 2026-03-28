using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Transkript.Views;

public partial class LoginWindow : Window
{
    private bool _pwdVisible = false;

    public string PrefilledEmail { get; set; } = "";

    public LoginWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (!string.IsNullOrEmpty(PrefilledEmail))
                TxtEmail.Text = PrefilledEmail;
            TxtEmail.Focus();
        };
        PointerPressed += (_, e) => BeginMoveDrag(e);
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Tag = false;
        Close();
    }

    private void BtnEye_Click(object? sender, RoutedEventArgs e)
    {
        _pwdVisible = !_pwdVisible;
        TxtPassword.PasswordChar = _pwdVisible ? '\0' : '•';
    }

    private void Field_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) BtnSubmit_Click(sender, e);
    }

    private async void BtnSubmit_Click(object? sender, RoutedEventArgs e)
    {
        TxtError.IsVisible = false;

        var email = TxtEmail.Text?.Trim() ?? "";
        var pwd   = TxtPassword.Text ?? "";

        if (string.IsNullOrEmpty(email)) { ShowError("Saisis ton email."); return; }
        if (string.IsNullOrEmpty(pwd))   { ShowError("Saisis ton mot de passe."); return; }

        SetLoading(true);
        var (ok, error) = await AuthService.SignInAsync(email, pwd);
        SetLoading(false);

        if (ok)
        {
            Logger.Write($"Login réussi : {email}");
            Tag = true;
            Close();
        }
        else
        {
            Logger.Write($"Login échoué : {error}");
            ShowError(FriendlyError(error));
        }
    }

    private void SetLoading(bool loading)
    {
        BtnSubmit.IsEnabled  = !loading;
        TxtEmail.IsEnabled   = !loading;
        TxtPassword.IsEnabled = !loading;
        BtnSubmit.Content    = loading ? "Connexion…" : "Se connecter";
    }

    private void ShowError(string msg)
    {
        TxtError.Text      = msg;
        TxtError.IsVisible = true;
    }

    private static string FriendlyError(string raw) => raw.ToLower() switch
    {
        var s when s.Contains("invalid login")      => "Email ou mot de passe incorrect.",
        var s when s.Contains("invalid credential") => "Email ou mot de passe incorrect.",
        var s when s.Contains("email not confirm")  => "Confirme ton email avant de te connecter.",
        var s when s.Contains("rate limit")         => "Trop de tentatives. Réessaie dans quelques instants.",
        _ => raw
    };
}
