using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SshManager.App.Behaviors;

/// <summary>
/// Adorner that provides visual feedback during drag operations.
/// Shows a semi-transparent copy of the dragged element.
/// </summary>
internal sealed class DragAdorner : Adorner
{
    private readonly ContentPresenter _contentPresenter;
    private Point _position;

    /// <summary>
    /// Initializes a new instance of the DragAdorner class.
    /// </summary>
    /// <param name="adornedElement">The element to adorn.</param>
    /// <param name="data">The data to display in the adorner.</param>
    public DragAdorner(UIElement adornedElement, object data)
        : base(adornedElement)
    {
        _contentPresenter = new ContentPresenter
        {
            Content = data,
            Opacity = 0.7
        };

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
        _contentPresenter.Measure(constraint);
        return _contentPresenter.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _contentPresenter.Arrange(new Rect(finalSize));
        return finalSize;
    }

    protected override Visual GetVisualChild(int index)
    {
        return _contentPresenter;
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
