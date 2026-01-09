using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SshManager.Core.Models;

namespace SshManager.App.Behaviors;

/// <summary>
/// Adorner that provides visual feedback during drag operations.
/// Shows a semi-transparent representation of the dragged host entry.
/// </summary>
internal sealed class DragAdorner : Adorner
{
    private readonly Border _visual;
    private Point _position;

    /// <summary>
    /// Initializes a new instance of the DragAdorner class.
    /// </summary>
    /// <param name="adornedElement">The element to adorn.</param>
    /// <param name="data">The data to display in the adorner.</param>
    public DragAdorner(UIElement adornedElement, object data)
        : base(adornedElement)
    {
        // Create a visual representation of the dragged item
        var displayText = data is HostEntry host ? host.DisplayName : data?.ToString() ?? "Item";

        var textBlock = new TextBlock
        {
            Text = displayText,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 6, 8, 6)
        };

        _visual = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 0, 120, 212)), // Semi-transparent blue
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 90, 158)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = textBlock,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 10,
                ShadowDepth = 3,
                Opacity = 0.5
            }
        };

        AddVisualChild(_visual);
        IsHitTestVisible = false;
    }

    /// <summary>
    /// Updates the position of the adorner.
    /// </summary>
    public void UpdatePosition(Point position)
    {
        _position = position;
        var layer = Parent as AdornerLayer;
        layer?.Update(AdornedElement);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        _visual.Measure(constraint);
        return _visual.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _visual.Arrange(new Rect(_visual.DesiredSize));
        return _visual.DesiredSize;
    }

    protected override Visual GetVisualChild(int index)
    {
        return _visual;
    }

    protected override int VisualChildrenCount => 1;

    public override GeneralTransform GetDesiredTransform(GeneralTransform transform)
    {
        var result = new GeneralTransformGroup();
        result.Children.Add(base.GetDesiredTransform(transform));
        result.Children.Add(new TranslateTransform(_position.X, _position.Y));
        return result;
    }
}
