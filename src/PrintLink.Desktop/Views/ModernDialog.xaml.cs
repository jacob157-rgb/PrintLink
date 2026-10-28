using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;

namespace PrintLink.Desktop.Views;

public enum ModernDialogType
{
    Information,
    Success,
    Warning,
    Error,
    Question
}

public partial class ModernDialog : Window
{
    public ModernDialog()
    {
        InitializeComponent();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
            }
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }

    private static Window? GetActiveOwner(Window? owner)
    {
        if (owner != null && owner.IsLoaded) return owner;
        if (WpfApplication.Current != null)
        {
            foreach (Window win in WpfApplication.Current.Windows)
            {
                if (win.IsActive) return win;
            }
            return WpfApplication.Current.MainWindow;
        }
        return null;
    }

    public static bool ShowConfirmation(
        string message,
        string title = "Confirmation",
        string confirmText = "Yes, Continue",
        string cancelText = "Cancel",
        bool isDestructive = false,
        Window? owner = null)
    {
        var targetOwner = GetActiveOwner(owner);
        var dialog = new ModernDialog
        {
            Owner = targetOwner,
            Title = title
        };

        dialog.TitleTextBlock.Text = title;
        dialog.MessageTextBlock.Text = message;
        dialog.PrimaryButton.Content = confirmText;
        dialog.SecondaryButton.Content = cancelText;
        dialog.SecondaryButton.Visibility = Visibility.Visible;

        if (isDestructive)
        {
            dialog.PrimaryButton.Style = (Style)dialog.FindResource("DialogDangerButton");
            dialog.IconTextBlock.Text = "⚠️";
            dialog.IconBadgeBorder.Background = new SolidColorBrush(WpfColor.FromRgb(69, 10, 10)); // #450A0A
        }
        else
        {
            dialog.IconTextBlock.Text = "❓";
            dialog.IconBadgeBorder.Background = new SolidColorBrush(WpfColor.FromRgb(30, 41, 59)); // #1E293B
        }

        return dialog.ShowDialog() == true;
    }

    public static void ShowInformation(string message, string title = "Information", string okText = "OK", Window? owner = null)
    {
        var targetOwner = GetActiveOwner(owner);
        var dialog = new ModernDialog
        {
            Owner = targetOwner,
            Title = title
        };

        dialog.TitleTextBlock.Text = title;
        dialog.MessageTextBlock.Text = message;
        dialog.PrimaryButton.Content = okText;
        dialog.SecondaryButton.Visibility = Visibility.Collapsed;
        dialog.IconTextBlock.Text = "ℹ️";
        dialog.IconBadgeBorder.Background = new SolidColorBrush(WpfColor.FromRgb(30, 27, 75)); // #1E1B4B

        dialog.ShowDialog();
    }

    public static void ShowSuccess(string message, string title = "Success", string okText = "OK", Window? owner = null)
    {
        var targetOwner = GetActiveOwner(owner);
        var dialog = new ModernDialog
        {
            Owner = targetOwner,
            Title = title
        };

        dialog.TitleTextBlock.Text = title;
        dialog.MessageTextBlock.Text = message;
        dialog.PrimaryButton.Content = okText;
        dialog.SecondaryButton.Visibility = Visibility.Collapsed;
        dialog.IconTextBlock.Text = "✅";
        dialog.IconBadgeBorder.Background = new SolidColorBrush(WpfColor.FromRgb(6, 78, 59)); // #064E3B

        dialog.ShowDialog();
    }

    public static void ShowWarning(string message, string title = "Warning", string okText = "OK", Window? owner = null)
    {
        var targetOwner = GetActiveOwner(owner);
        var dialog = new ModernDialog
        {
            Owner = targetOwner,
            Title = title
        };

        dialog.TitleTextBlock.Text = title;
        dialog.MessageTextBlock.Text = message;
        dialog.PrimaryButton.Content = okText;
        dialog.SecondaryButton.Visibility = Visibility.Collapsed;
        dialog.IconTextBlock.Text = "⚠️";
        dialog.IconBadgeBorder.Background = new SolidColorBrush(WpfColor.FromRgb(69, 26, 3)); // #451A03

        dialog.ShowDialog();
    }

    public static void ShowError(string message, string title = "Error", string okText = "OK", Window? owner = null)
    {
        var targetOwner = GetActiveOwner(owner);
        var dialog = new ModernDialog
        {
            Owner = targetOwner,
            Title = title
        };

        dialog.TitleTextBlock.Text = title;
        dialog.MessageTextBlock.Text = message;
        dialog.PrimaryButton.Content = okText;
        dialog.PrimaryButton.Style = (Style)dialog.FindResource("DialogDangerButton");
        dialog.SecondaryButton.Visibility = Visibility.Collapsed;
        dialog.IconTextBlock.Text = "❌";
        dialog.IconBadgeBorder.Background = new SolidColorBrush(WpfColor.FromRgb(69, 10, 10)); // #450A0A

        dialog.ShowDialog();
    }
}
