using System.Windows;
using System.Windows.Controls;
using NajmCard.Core.Models;

namespace NajmCard.Desktop.Views;

/// <summary>Places card elements by fractions of their available travel within the card.</summary>
internal sealed class CardElementPanel : Panel
{
    internal static readonly CardElementPosition DefaultTitle = new(0.53, 0.04);
    internal static readonly CardElementPosition DefaultCredentials = new(0.72, 0.50);
    internal static CardElementPosition PasswordBelow(CardElementPosition? credentials)
    {
        var username = credentials ?? DefaultCredentials;
        // Keep the password on the card even when an older template placed the
        // credential block close to the bottom edge.
        return new CardElementPosition(username.X, username.Y <= 0.8 ? username.Y + 0.2 : username.Y - 0.2);
    }

    internal static readonly CardElementPosition DefaultProfile = new(0.02, 0.64);
    private readonly Dictionary<UIElement, CardElementPosition> _positions = new();

    internal void Add(UIElement element, CardElementPosition position)
    {
        Children.Add(element);
        Place(element, position);
    }

    internal void Place(UIElement element, CardElementPosition position)
    {
        _positions[element] = position;
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (UIElement child in Children)
            child.Measure(availableSize);
        return new Size();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in Children)
        {
            var size = child.DesiredSize;
            var position = _positions[child];
            child.Arrange(new Rect(
                Math.Max(0, finalSize.Width - size.Width) * position.X,
                Math.Max(0, finalSize.Height - size.Height) * position.Y,
                Math.Min(size.Width, finalSize.Width),
                Math.Min(size.Height, finalSize.Height)));
        }
        return finalSize;
    }
}
