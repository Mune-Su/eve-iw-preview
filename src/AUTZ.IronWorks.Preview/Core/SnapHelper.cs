namespace AUTZ.IronWorks.Preview.Core;

/// <summary>Edge snapping between thumbnail windows. Everything is in screen pixels.</summary>
internal static class SnapHelper
{
    public static (int X, int Y) Snap(int x, int y, int width, int height,
                                      IReadOnlyList<NativeMethods.RECT> others, int distance)
    {
        if (distance <= 0 || others.Count == 0) return (x, y);

        int left = x, right = x + width, top = y, bottom = y + height;
        int bestDx = int.MaxValue, bestDy = int.MaxValue;

        foreach (var o in others)
        {
            // only snap along an axis when the windows are actually near each other on the other axis
            bool nearVertically = top < o.Bottom + distance && bottom > o.Top - distance;
            bool nearHorizontally = left < o.Right + distance && right > o.Left - distance;

            if (nearVertically)
            {
                Consider(o.Right - left, ref bestDx);   // my left edge to their right edge (side by side)
                Consider(o.Left - right, ref bestDx);   // my right edge to their left edge
                Consider(o.Left - left, ref bestDx);    // left edges line up
                Consider(o.Right - right, ref bestDx);  // right edges line up
            }
            if (nearHorizontally)
            {
                Consider(o.Bottom - top, ref bestDy);   // my top to their bottom (stacked)
                Consider(o.Top - bottom, ref bestDy);   // my bottom to their top
                Consider(o.Top - top, ref bestDy);      // top edges line up
                Consider(o.Bottom - bottom, ref bestDy);// bottom edges line up
            }
        }

        if (bestDx != int.MaxValue && Math.Abs(bestDx) <= distance) x += bestDx;
        if (bestDy != int.MaxValue && Math.Abs(bestDy) <= distance) y += bestDy;
        return (x, y);
    }

    private static void Consider(int delta, ref int best)
    {
        if (best == int.MaxValue || Math.Abs(delta) < Math.Abs(best)) best = delta;
    }
}


