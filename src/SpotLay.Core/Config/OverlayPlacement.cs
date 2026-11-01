namespace SpotLay.Core.Config;

/// <summary>
/// Posición del overlay en píxeles reales del monitor. El modo "anclado" guarda
/// el borde más cercano (izquierda/centro/derecha, arriba/centro/abajo) y la
/// distancia a él: si el overlay crece sigue pegado a su borde, y moverlo con
/// las flechas es exacto al píxel en cualquier resolución.
/// </summary>
public static class OverlayPlacement
{
    /// <summary>Margen de las posiciones rápidas (esquinas), en píxeles de 100 % de escala.</summary>
    public const int QuickMargin = 24;

    public readonly record struct Screen(int Left, int Top, int Width, int Height);

    /// <summary>Esquina superior izquierda del overlay (w x h px) en ese monitor.</summary>
    public static (int X, int Y) Compute(OverlayLayout l, Screen s, int w, int h, double dpiScale)
    {
        int x, y;
        if (l.Position == OverlayPositions.Anchored)
        {
            x = Axis(l.AnchorX, l.AnchorOffsetX, s.Left, s.Width, w);
            y = Axis(l.AnchorY, l.AnchorOffsetY, s.Top, s.Height, h);
        }
        else
        {
            // Diseños anteriores (esquina + margen, o libre en %), tal cual se veían.
            var ox = (int)Math.Round(l.OffsetX * dpiScale);
            var oy = (int)Math.Round(l.OffsetY * dpiScale);
            (x, y) = l.Position switch
            {
                OverlayPositions.TopCenter => (s.Left + (s.Width - w) / 2, s.Top + oy),
                OverlayPositions.TopRight => (s.Left + s.Width - w - ox, s.Top + oy),
                OverlayPositions.Center => (s.Left + (s.Width - w) / 2, s.Top + (s.Height - h) / 2),
                OverlayPositions.BottomLeft => (s.Left + ox, s.Top + s.Height - h - oy),
                OverlayPositions.BottomCenter => (s.Left + (s.Width - w) / 2, s.Top + s.Height - h - oy),
                OverlayPositions.BottomRight => (s.Left + s.Width - w - ox, s.Top + s.Height - h - oy),
                OverlayPositions.Custom => (s.Left + (int)Math.Round(Math.Clamp(l.FreeX, 0, 1) * Math.Max(0, s.Width - w)),
                                            s.Top + (int)Math.Round(Math.Clamp(l.FreeY, 0, 1) * Math.Max(0, s.Height - h))),
                _ => (s.Left + ox, s.Top + oy)
            };
        }
        // Nunca fuera de la pantalla.
        x = Math.Clamp(x, s.Left, s.Left + Math.Max(0, s.Width - w));
        y = Math.Clamp(y, s.Top, s.Top + Math.Max(0, s.Height - h));
        return (x, y);
    }

    /// <summary>Pasa una posición (esquina superior izquierda) a anclada en el borde más cercano.</summary>
    public static void SetAnchored(OverlayLayout l, Screen s, int w, int h, int x, int y)
    {
        x = Math.Clamp(x, s.Left, s.Left + Math.Max(0, s.Width - w));
        y = Math.Clamp(y, s.Top, s.Top + Math.Max(0, s.Height - h));
        (l.AnchorX, l.AnchorOffsetX) = FromAxis(x - s.Left, s.Width, w);
        (l.AnchorY, l.AnchorOffsetY) = FromAxis(y - s.Top, s.Height, h);
        l.Position = OverlayPositions.Anchored;
    }

    /// <summary>Imán: cerca de un borde queda al margen de siempre; cerca del centro, centrado.</summary>
    public static void Snap(OverlayLayout l, double dpiScale)
    {
        var margin = (int)Math.Round(QuickMargin * dpiScale);
        var reach = margin * 3;
        l.AnchorOffsetX = SnapAxis(l.AnchorX, l.AnchorOffsetX, margin, reach);
        l.AnchorOffsetY = SnapAxis(l.AnchorY, l.AnchorOffsetY, margin, reach);
    }

    private static int SnapAxis(int anchor, int offset, int margin, int reach) =>
        anchor == 1 ? (Math.Abs(offset) <= reach ? 0 : offset) : (offset <= reach ? margin : offset);

    /// <summary>Posición rápida: ax/ay = 0 izquierda/arriba, 1 centro, 2 derecha/abajo.</summary>
    public static void SetQuick(OverlayLayout l, int ax, int ay, double dpiScale)
    {
        var margin = (int)Math.Round(QuickMargin * dpiScale);
        l.AnchorX = ax;
        l.AnchorY = ay;
        l.AnchorOffsetX = ax == 1 ? 0 : margin;
        l.AnchorOffsetY = ay == 1 ? 0 : margin;
        l.Position = OverlayPositions.Anchored;
    }

    // 0: distancia al borde izquierdo; 1: desplazamiento del centro; 2: distancia al borde derecho.
    private static int Axis(int anchor, int offset, int start, int size, int length) => anchor switch
    {
        1 => start + (size - length) / 2 + offset,
        2 => start + size - length - offset,
        _ => start + offset
    };

    private static (int Anchor, int Offset) FromAxis(int pos, int size, int length)
    {
        var center = pos + length / 2.0;
        if (center < size / 3.0) return (0, pos);
        if (center > size * 2 / 3.0) return (2, size - length - pos);
        return (1, pos - (size - length) / 2);
    }
}
