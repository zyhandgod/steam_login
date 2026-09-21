using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SteamLoginLite
{
    internal static class UiStyle
    {
        public static GraphicsPath RoundedPath(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            if (bounds.Width <= 0 || bounds.Height <= 0) return path;
            var diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Round(Control control, int radius)
        {
            Action apply = () =>
            {
                if (control.Width <= 0 || control.Height <= 0) return;
                using (var path = RoundedPath(new Rectangle(0, 0, control.Width, control.Height), radius))
                {
                    var oldRegion = control.Region;
                    control.Region = new Region(path);
                    oldRegion?.Dispose();
                }
            };
            control.HandleCreated += (_, __) => apply();
            control.Resize += (_, __) => apply();
            apply();
        }

        public static void DrawRoundedBorder(Graphics graphics, Rectangle bounds, int radius, Color color, float width = 1F)
        {
            var previous = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundedPath(bounds, radius))
            using (var pen = new Pen(color, width))
                graphics.DrawPath(pen, path);
            graphics.SmoothingMode = previous;
        }
    }
}
