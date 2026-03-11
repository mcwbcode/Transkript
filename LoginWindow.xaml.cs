using System.Windows;
using System.Windows.Input;
using WinKey     = System.Windows.Input.Key;
using WinKeyArgs = System.Windows.Input.KeyEventArgs;

namespace Transkript;

public partial class LoginWindow : Window
{
    private bool _pwdVisible = false;

    public string PrefilledEmail { get; set; } = "";

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (!string.IsNullOrEmpty(PrefilledEmail))
                TxtEmail.Text = PrefilledEmail;
            TxtEmail.Focus();
        };
    }

    private void Root_MouseDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void BtnClose_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ── Afficher/masquer mot de passe ─────────────────────────────────────────

    private void BtnEye_Click(object sender, RoutedEventArgs e)
    {
        _pwdVisible = !_pwdVisible;
        if (_pwdVisible)
        {
            TxtPwdVisible.Text       = PwdPassword.Password;
            TxtPwdVisible.Visibility = Visibility.Visible;
            PwdPassword.Visibility   = Visibility.Collapsed;
            TxtPwdVisible.Focus();
            TxtPwdVisible.CaretIndex = TxtPwdVisible.Text.Length;
        }
        else
        {
            PwdPassword.Password     = TxtPwdVisible.Text;
            PwdPassword.Visibility   = Visibility.Visible;
            TxtPwdVisible.Visibility = Visibility.Collapsed;
            PwdPassword.Focus();
        }
    }

    private void Field_KeyDown(object sender, WinKeyArgs e)
    {
        if (e.Key == WinKey.Enter) BtnSubmit_Click(sender, e);
    }

    // ── Connexion ─────────────────────────────────────────────────────────────

    private async void BtnSubmit_Click(object sender, RoutedEventArgs e)
    {
        TxtError.Visibility = Visibility.Collapsed;

        var email = TxtEmail.Text.Trim();
        var pwd   = _pwdVisible ? TxtPwdVisible.Text : PwdPassword.Password;

        if (string.IsNullOrEmpty(email)) { ShowError("Saisis ton email."); return; }
        if (string.IsNullOrEmpty(pwd))   { ShowError("Saisis ton mot de passe."); return; }

        SetLoading(true);
        var (ok, error) = await AuthService.SignInAsync(email, pwd);
        SetLoading(false);

        if (ok)
        {
            Logger.Write($"Login réussi : {email}");
            DialogResult = true;
        }
        else
        {
            Logger.Write($"Login échoué : {error}");
            ShowError(FriendlyError(error));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetLoading(bool loading)
    {
        BtnSubmit.IsEnabled     = !loading;
        TxtEmail.IsEnabled      = !loading;
        PwdPassword.IsEnabled   = !loading;
        TxtPwdVisible.IsEnabled = !loading;
        BtnSubmit.Content       = loading ? "Connexion…" : "Se connecter";
    }

    private void ShowError(string msg)
    {
        TxtError.Text       = msg;
        TxtError.Visibility = Visibility.Visible;
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
