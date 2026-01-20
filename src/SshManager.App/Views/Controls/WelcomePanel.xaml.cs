using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace SshManager.App.Views.Controls;

/// <summary>
/// Welcome panel displayed when no terminal sessions are active.
/// Contains quick connect circle, add host button, and import options.
/// </summary>
public partial class WelcomePanel : UserControl
{
    /// <summary>
    /// Event raised when the quick connect circle is clicked.
    /// </summary>
    public event EventHandler? QuickConnectRequested;

    /// <summary>
    /// Event raised when import from file is requested.
    /// </summary>
    public event EventHandler? ImportFromFileRequested;

    /// <summary>
    /// Event raised when import from SSH config is requested.
    /// </summary>
    public event EventHandler? ImportFromSshConfigRequested;

    /// <summary>
    /// Event raised when import from PuTTY is requested.
    /// </summary>
    public event EventHandler? ImportFromPuttyRequested;

    public WelcomePanel()
    {
        InitializeComponent();
    }

    private void QuickConnectCircle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        PlayQuickConnectClickAnimation();
        QuickConnectRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ImportHostsButton_Click(object sender, RoutedEventArgs e)
    {
        ImportFromFileRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ImportFromSshConfigButton_Click(object sender, RoutedEventArgs e)
    {
        ImportFromSshConfigRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ImportFromPuttyButton_Click(object sender, RoutedEventArgs e)
    {
        ImportFromPuttyRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PlayQuickConnectClickAnimation()
    {
        var scaleDown = new DoubleAnimation
        {
            To = 0.92,
            Duration = TimeSpan.FromMilliseconds(100),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        var scaleUp = new DoubleAnimation
        {
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(150),
            BeginTime = TimeSpan.FromMilliseconds(100),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        var glowBright = new DoubleAnimation
        {
            To = 50,
            Duration = TimeSpan.FromMilliseconds(100)
        };

        var glowNormal = new DoubleAnimation
        {
            To = 20,
            Duration = TimeSpan.FromMilliseconds(200),
            BeginTime = TimeSpan.FromMilliseconds(100)
        };

        var storyboard = new Storyboard();

        Storyboard.SetTarget(scaleDown, QuickConnectCircle);
        Storyboard.SetTargetProperty(scaleDown,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
        storyboard.Children.Add(scaleDown);

        var scaleDownY = new DoubleAnimation
        {
            To = 0.92,
            Duration = TimeSpan.FromMilliseconds(100),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(scaleDownY, QuickConnectCircle);
        Storyboard.SetTargetProperty(scaleDownY,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
        storyboard.Children.Add(scaleDownY);

        Storyboard.SetTarget(scaleUp, QuickConnectCircle);
        Storyboard.SetTargetProperty(scaleUp,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
        storyboard.Children.Add(scaleUp);

        var scaleUpY = new DoubleAnimation
        {
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(150),
            BeginTime = TimeSpan.FromMilliseconds(100),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(scaleUpY, QuickConnectCircle);
        Storyboard.SetTargetProperty(scaleUpY,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
        storyboard.Children.Add(scaleUpY);

        Storyboard.SetTarget(glowBright, QuickConnectCircle);
        Storyboard.SetTargetProperty(glowBright,
            new PropertyPath("(UIElement.Effect).(DropShadowEffect.BlurRadius)"));
        storyboard.Children.Add(glowBright);

        Storyboard.SetTarget(glowNormal, QuickConnectCircle);
        Storyboard.SetTargetProperty(glowNormal,
            new PropertyPath("(UIElement.Effect).(DropShadowEffect.BlurRadius)"));
        storyboard.Children.Add(glowNormal);

        storyboard.Begin();
    }
}
