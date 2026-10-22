using System.Drawing.Drawing2D;

namespace BluetoothAutoReconnect.Tray;

/// <summary>
/// Draws the tray glyph: a gamepad filled with the battery color, with a dark outline so it stays
/// legible on both light and dark taskbars. Coordinates are in a 32×32 design space scaled to the
/// requested size.
/// </summary>
public static class GamepadIconRenderer
{
    private const float DesignSize = 32f;

    public static Color OutlineColor { get; } = Color.FromArgb(0x1E, 0x1E, 0x1E);

    public static Bitmap Render(Color fill, int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 8);

        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.ScaleTransform(size / DesignSize, size / DesignSize);

        using var body = BodyPath();

        // Outline first with a wide pen, then the fill on top: only the outer edge of the union
        // stays visible, not the seams between body and grips.
        using (var outline = new Pen(OutlineColor, 3.5f) { LineJoin = LineJoin.Round })
        {
            g.DrawPath(outline, body);
        }

        using (var brush = new SolidBrush(fill))
        {
            g.FillPath(brush, body);
        }

        using var detail = new SolidBrush(OutlineColor);

        // D-pad.
        g.FillRectangle(detail, 6.5f, 14f, 7f, 2.5f);
        g.FillRectangle(detail, 8.75f, 11.75f, 2.5f, 7f);

        // Face buttons.
        g.FillEllipse(detail, 19.5f, 11.5f, 3f, 3f);
        g.FillEllipse(detail, 23f, 15f, 3f, 3f);

        return bitmap;
    }

    private static GraphicsPath BodyPath()
    {
        // Winding fill mode makes overlapping figures render as their union.
        var path = new GraphicsPath(FillMode.Winding);
        AddRoundedRectangle(path, new RectangleF(3f, 8f, 26f, 13f), radius: 6f);
        path.AddEllipse(3f, 13f, 11f, 15f);
        path.AddEllipse(18f, 13f, 11f, 15f);
        return path;
    }

    private static void AddRoundedRectangle(GraphicsPath path, RectangleF r, float radius)
    {
        var d = radius * 2;
        path.StartFigure();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
    }
}
