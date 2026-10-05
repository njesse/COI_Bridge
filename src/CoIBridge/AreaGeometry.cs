using System;
using System.Collections.Generic;
using Mafi;

namespace CoIBridge
{
    // Geometry only: clipping a simple polygon against a native raster cell.
    internal static class AreaGeometry
    {
        private struct Point { public double X, Y; public Point(double x, double y) { X = x; Y = y; } }
        private static long Cross(Vector2i a, Vector2i b, Vector2i c)
        { return (long)(b.X-a.X)*(c.Y-a.Y) - (long)(b.Y-a.Y)*(c.X-a.X); }
        private static bool OnSegment(Vector2i a, Vector2i b, Vector2i p)
        { return Cross(a,b,p) == 0 && p.X >= Math.Min(a.X,b.X) && p.X <= Math.Max(a.X,b.X) && p.Y >= Math.Min(a.Y,b.Y) && p.Y <= Math.Max(a.Y,b.Y); }
        private static bool Intersects(Vector2i a, Vector2i b, Vector2i c, Vector2i d)
        {
            long x = Cross(a,b,c), y = Cross(a,b,d), z = Cross(c,d,a), w = Cross(c,d,b);
            return (Math.Sign(x)*Math.Sign(y) < 0 && Math.Sign(z)*Math.Sign(w) < 0)
                || OnSegment(a,b,c) || OnSegment(a,b,d) || OnSegment(c,d,a) || OnSegment(c,d,b);
        }
        public static void Validate(List<Vector2i> points)
        {
            if (points.Count < 3) throw new ArgumentException("At least three polygon vertices required");
            long area = 0;
            for (int i = 0; i < points.Count; i++) {
                var a = points[i]; var b = points[(i+1)%points.Count];
                if (a.X == b.X && a.Y == b.Y) throw new ArgumentException("Duplicate polygon vertex");
                var c = points[(i+2)%points.Count];
                if (Cross(a,b,c) == 0 && (long)(b.X-a.X)*(c.X-b.X)+(long)(b.Y-a.Y)*(c.Y-b.Y) < 0)
                    throw new ArgumentException("Polygon edge doubles back");
                area += (long)a.X*b.Y - (long)b.X*a.Y;
                for (int j = i+1; j < points.Count; j++) {
                    if (j == i+1 || (i == 0 && j == points.Count-1)) continue;
                    if (Intersects(a,b,points[j],points[(j+1)%points.Count])) throw new ArgumentException("Self-intersecting polygon");
                }
            }
            if (area == 0) throw new ArgumentException("Polygon has no area");
        }
        public static bool OverlapsCell(List<Vector2i> polygon, int x, int y, int size)
        {
            var points = new List<Point>(); foreach (var p in polygon) points.Add(new Point(p.X,p.Y));
            points = Clip(points, true, x, true); points = Clip(points, true, x+size, false);
            points = Clip(points, false, y, true); points = Clip(points, false, y+size, false);
            double area = 0;
            for (int i = 0; i < points.Count; i++) { var a = points[i]; var b = points[(i+1)%points.Count]; area += a.X*b.Y-b.X*a.Y; }
            return Math.Abs(area) > 0.000001;
        }
        private static List<Point> Clip(List<Point> input, bool horizontal, double edge, bool greater)
        {
            var output = new List<Point>(); if (input.Count == 0) return output;
            var previous = input[input.Count-1]; double pv = horizontal ? previous.X : previous.Y;
            bool wasInside = greater ? pv >= edge : pv <= edge;
            foreach (var current in input) {
                double cv = horizontal ? current.X : current.Y; bool inside = greater ? cv >= edge : cv <= edge;
                if (inside != wasInside) {
                    double t = (edge-pv)/(cv-pv);
                    output.Add(new Point(previous.X+t*(current.X-previous.X), previous.Y+t*(current.Y-previous.Y)));
                }
                if (inside) output.Add(current);
                previous = current; pv = cv; wasInside = inside;
            }
            return output;
        }
    }
}
