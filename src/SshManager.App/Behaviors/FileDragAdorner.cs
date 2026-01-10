using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace SshManager.App.Behaviors;

/// <summary>
/// Transfer direction for file drag operations.
/// </summary>
public enum TransferDirection
{
    None,
    Upload,   // Local -> Remote (right arrow)
    Download  // Remote -> Local (left arrow)
}

/// <summary>
/// Adorner for file drag operations showing ghost preview with file count badge
/// and transfer direction indicator.
/// </summary>
internal sealed class FileDragAdorner : Adorner
{
    private readonly Border _container;
    private Point _position;

    /// <summary>
    /// The number of files being dragged.
    /// </summary>
    public int FileCount { get; }

    /// <summary>
    /// The transfer direction.
    /// </summary>
    public TransferDirection Direction { get; set; }

    /// <summary>
    /// Creates a new file drag adorner.
    /// </summary>
    /// <param name="adornedElement">The element to adorn.</param>
    /// <param name="filePaths">The file paths being dragged.</param>
    /// <param name="direction">The transfer direction.</param>
    public FileDragAdorner(UIElement adornedElement, IReadOnlyList<string> filePaths, TransferDirection direction)
        : base(adornedElement)
    {
        FileCount = filePaths.Count;
        Direction = direction;

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };

        // Direction arrow
        var arrowText = new TextBlock
        {
            Text = direction switch
            {
                TransferDirection.Upload => "\u2192",   // Right arrow →
                TransferDirection.Download => "\u2190", // Left arrow ←
                _ => ""
            },
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = direction == TransferDirection.Upload
                ? new SolidColorBrush(Color.FromRgb(46, 204, 113))  // Green for upload
                : new SolidColorBrush(Color.FromRgb(52, 152, 219)), // Blue for download
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };

        // File icon using Segoe Fluent Icons
        var icon = new TextBlock
        {
            Text = FileCount > 1 ? "\uE8B7" : "\uE8A5", // Multiple docs or single doc
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 14,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };

        // File name or count description
        var displayName = FileCount == 1
            ? Path.GetFileName(filePaths[0])
            : $"{FileCount} files";

        var text = new TextBlock
        {
            Text = displayName,
            FontSize = 12,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 180,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        panel.Children.Add(arrowText);
        panel.Children.Add(icon);
        panel.Children.Add(text);

        // File count badge for multiple files
        if (FileCount > 1)
        {
            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
                CornerRadius = new CornerRadius(8),
                MinWidth = 18,
                Height = 18,
                Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = FileCount.ToString(),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            panel.Children.Add(badge);
        }

        _container = new Border
        {
            Child = panel,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6, 10, 6),
            Background = new SolidColorBrush(Color.FromArgb(230, 45, 45, 48)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 2,
                Opacity = 0.4,
                Color = Colors.Black
            }
        };

        AddVisualChild(_container);
        IsHitTestVisible = false;
    }

    /// <summary>
    /// Updates the position of the adorner.
    /// </summary>
    public void UpdatePosition(Point position)
    {
        _position = position;
        InvalidateVisual();
        (Parent as AdornerLayer)?.Update(AdornedElement);
    }

    /// <summary>
    /// Sets visual feedback for invalid drop zone.
    /// </summary>
    public void SetInvalidDropZone(bool invalid)
    {
        _container.BorderBrush = invalid
            ? new SolidColorBrush(Color.FromRgb(231, 76, 60)) // Red
            : new SolidColorBrush(Color.FromArgb(100, 255, 255, 255));
        _container.BorderThickness = invalid ? new Thickness(2) : new Thickness(1);
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _container;

    protected override Size MeasureOverride(Size constraint)
    {
        _container.Measure(constraint);
        return _container.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _container.Arrange(new Rect(_container.DesiredSize));
        return _container.DesiredSize;
    }

    public override GeneralTransform GetDesiredTransform(GeneralTransform transform)
    {
        var result = new GeneralTransformGroup();
        result.Children.Add(base.GetDesiredTransform(transform));
        // Offset to position adorner near cursor but not under it
        result.Children.Add(new TranslateTransform(_position.X + 12, _position.Y + 12));
        return result;
    }
}
