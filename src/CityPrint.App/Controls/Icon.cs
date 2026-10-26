using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace CityPrint.App.Controls;

/// <summary>
/// Draws a Lucide stroke icon (24×24 path data) in the inherited text colour, so icons follow
/// the theme and hover/disabled states like text does.
/// </summary>
public sealed class Icon : Control
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<Icon, Geometry?>(nameof(Data));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Icon>();

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(Size), 16);

    public static readonly StyledProperty<double> StrokeWidthProperty =
        AvaloniaProperty.Register<Icon, double>(nameof(StrokeWidth), 2);

    static Icon()
    {
        AffectsRender<Icon>(DataProperty, ForegroundProperty, StrokeWidthProperty);
        AffectsMeasure<Icon>(SizeProperty);
    }

    public Geometry? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    /// <summary>Stroke width in the icon's 24-unit space.</summary>
    public double StrokeWidth { get => GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext ctx)
    {
        if (Data is null || Foreground is null) return;
        double k = Size / 24;
        var offset = new Point((Bounds.Width - Size) / 2, (Bounds.Height - Size) / 2);
        using (ctx.PushTransform(Matrix.CreateScale(k, k) * Matrix.CreateTranslation(offset.X, offset.Y)))
            ctx.DrawGeometry(null, new Pen(Foreground, StrokeWidth, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Data);
    }
}
