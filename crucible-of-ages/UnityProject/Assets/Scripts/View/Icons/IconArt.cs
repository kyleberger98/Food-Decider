using System;
using System.Collections.Generic;
using Crucible.Core.Content;

namespace Crucible.View.Icons
{
    /// <summary>The symbol drawn on a unit's shield flag (Civ V / Humankind style).</summary>
    public enum UnitIcon
    {
        Sword, Spear, Bow, Crossbow, Horse, Catapult, Cannon, Rocket, Musket, Helmet, Tank,
        SailShip, Steamship, Carrier, Fighter, Bomber, Jet, Scout, Settler, Worker, Ram, SiegeTower,
        General, Scientist, Engineer, Merchant, Artist, Prophet,
    }

    /// <summary>
    /// Procedural vector art for unit symbols and the heraldic shield they sit on. Shapes are authored
    /// on a 100×100 canvas (y down) and rasterised with 4×4 supersampling into an alpha mask, so the
    /// art needs no imported assets and stays crisp at any size. Engine-free: the Unity side only
    /// copies the mask into a texture (see <see cref="IconTextures"/>).
    /// </summary>
    public static class IconArt
    {
        public static UnitIcon ForUnit(UnitDef def)
        {
            switch (def.Id)
            {
                case DefaultContent.SettlerUnit: return UnitIcon.Settler;
                case DefaultContent.WorkerUnit: return UnitIcon.Worker;
                case "crossbowman": return UnitIcon.Crossbow;
                case "battering_ram": return UnitIcon.Ram;
                case "siege_tower": return UnitIcon.SiegeTower;
                case "cannon": case "artillery": return UnitIcon.Cannon;
                case "rocket_artillery": return UnitIcon.Rocket;
                case "infantry": case "mech_infantry": return UnitIcon.Helmet;
                case "trireme": case "frigate": return UnitIcon.SailShip;
                case "jet_fighter": return UnitIcon.Jet;
            }
            switch (def.GreatPerson)
            {
                case GreatPersonType.General: return UnitIcon.General;
                case GreatPersonType.Scientist: return UnitIcon.Scientist;
                case GreatPersonType.Engineer: return UnitIcon.Engineer;
                case GreatPersonType.Merchant: return UnitIcon.Merchant;
                case GreatPersonType.Artist: return UnitIcon.Artist;
                case GreatPersonType.Prophet: return UnitIcon.Prophet;
            }
            switch (def.Class)
            {
                case UnitClass.Recon: return UnitIcon.Scout;
                case UnitClass.Ranged: return UnitIcon.Bow;
                case UnitClass.Mounted: return UnitIcon.Horse;
                case UnitClass.AntiCavalry: return UnitIcon.Spear;
                case UnitClass.Siege: return UnitIcon.Catapult;
                case UnitClass.Gunpowder: return UnitIcon.Musket;
                case UnitClass.Armor: return UnitIcon.Tank;
                case UnitClass.NavalMelee: case UnitClass.NavalRanged: return UnitIcon.Steamship;
                case UnitClass.Carrier: return UnitIcon.Carrier;
                case UnitClass.Fighter: return UnitIcon.Fighter;
                case UnitClass.Bomber: return UnitIcon.Bomber;
                case UnitClass.Missile: return UnitIcon.Rocket;
                case UnitClass.Civilian: return UnitIcon.Worker;
                default: return UnitIcon.Sword;
            }
        }

        // ------------------------------------------------------------------ rasteriser

        abstract class Shape
        {
            public bool Erase;
            public abstract bool Contains(double x, double y);
        }

        sealed class Poly : Shape
        {
            public double[] X, Y;
            public override bool Contains(double x, double y)
            {
                bool inside = false;
                for (int i = 0, j = X.Length - 1; i < X.Length; j = i++)
                    if ((Y[i] > y) != (Y[j] > y) && x < (X[j] - X[i]) * (y - Y[i]) / (Y[j] - Y[i]) + X[i]) inside = !inside;
                return inside;
            }
        }

        sealed class Circle : Shape
        {
            public double Cx, Cy, R;
            public override bool Contains(double x, double y) => (x - Cx) * (x - Cx) + (y - Cy) * (y - Cy) <= R * R;
        }

        sealed class Capsule : Shape
        {
            public double Ax, Ay, Bx, By, HalfWidth;
            public override bool Contains(double x, double y)
            {
                double dx = Bx - Ax, dy = By - Ay;
                double t = Math.Max(0, Math.Min(1, ((x - Ax) * dx + (y - Ay) * dy) / (dx * dx + dy * dy + 1e-9)));
                double px = Ax + t * dx - x, py = Ay + t * dy - y;
                return px * px + py * py <= HalfWidth * HalfWidth;
            }
        }

        /// <summary>Collects shapes; an optional rotation (degrees, about the canvas centre) applies to everything added after it.</summary>
        sealed class Canvas
        {
            public readonly List<Shape> Shapes = new List<Shape>();
            double _cos = 1, _sin;

            public void Rotate(double degrees)
            {
                double r = degrees * Math.PI / 180;
                _cos = Math.Cos(r);
                _sin = Math.Sin(r);
            }

            (double, double) T(double x, double y)
            {
                double dx = x - 50, dy = y - 50;
                return (50 + dx * _cos - dy * _sin, 50 + dx * _sin + dy * _cos);
            }

            public void Poly(params double[] xy) => AddPoly(false, xy);
            public void Cut(params double[] xy) => AddPoly(true, xy);

            void AddPoly(bool erase, double[] xy)
            {
                var p = new Poly { Erase = erase, X = new double[xy.Length / 2], Y = new double[xy.Length / 2] };
                for (int i = 0; i < p.X.Length; i++) (p.X[i], p.Y[i]) = T(xy[2 * i], xy[2 * i + 1]);
                Shapes.Add(p);
            }

            public void Rect(double x0, double y0, double x1, double y1, bool erase = false) =>
                AddPoly(erase, new[] { x0, y0, x1, y0, x1, y1, x0, y1 });

            public void Circle(double cx, double cy, double r, bool erase = false)
            {
                var (x, y) = T(cx, cy);
                Shapes.Add(new Circle { Cx = x, Cy = y, R = r, Erase = erase });
            }

            public void Line(double ax, double ay, double bx, double by, double width, bool erase = false)
            {
                var (x0, y0) = T(ax, ay);
                var (x1, y1) = T(bx, by);
                Shapes.Add(new Capsule { Ax = x0, Ay = y0, Bx = x1, By = y1, HalfWidth = width / 2, Erase = erase });
            }

            /// <summary>Arc of a circle as a thick polyline (angles in degrees, 0 = +x, clockwise on screen).</summary>
            public void Arc(double cx, double cy, double r, double fromDeg, double toDeg, double width, bool erase = false)
            {
                const int steps = 18;
                for (int i = 0; i < steps; i++)
                {
                    double a0 = (fromDeg + (toDeg - fromDeg) * i / steps) * Math.PI / 180;
                    double a1 = (fromDeg + (toDeg - fromDeg) * (i + 1) / steps) * Math.PI / 180;
                    Line(cx + r * Math.Cos(a0), cy + r * Math.Sin(a0), cx + r * Math.Cos(a1), cy + r * Math.Sin(a1), width, erase);
                }
            }

            /// <summary>Filled pie/segment: centre plus arc points.</summary>
            public void Sector(double cx, double cy, double r, double fromDeg, double toDeg, bool erase = false)
            {
                const int steps = 24;
                var pts = new List<double> { cx, cy };
                for (int i = 0; i <= steps; i++)
                {
                    double a = (fromDeg + (toDeg - fromDeg) * i / steps) * Math.PI / 180;
                    pts.Add(cx + r * Math.Cos(a));
                    pts.Add(cy + r * Math.Sin(a));
                }
                AddPoly(erase, pts.ToArray());
            }

            public void Star(double cx, double cy, double outer, double inner, int points = 5)
            {
                var pts = new List<double>();
                for (int i = 0; i < points * 2; i++)
                {
                    double a = -Math.PI / 2 + i * Math.PI / points;
                    double r = i % 2 == 0 ? outer : inner;
                    pts.Add(cx + r * Math.Cos(a));
                    pts.Add(cy + r * Math.Sin(a));
                }
                AddPoly(false, pts.ToArray());
            }
        }

        /// <summary>Rasterises shapes to an alpha mask (row 0 = top). <paramref name="pad"/> is the margin as a fraction of size.</summary>
        static float[] Raster(Canvas c, int size, double pad)
        {
            const int ss = 4;
            var alpha = new float[size * size];
            double scale = 100.0 / (size * (1 - 2 * pad)), offset = pad * size;
            for (int py = 0; py < size; py++)
                for (int px = 0; px < size; px++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < ss; sy++)
                        for (int sx = 0; sx < ss; sx++)
                        {
                            double x = (px + (sx + 0.5) / ss - offset) * scale;
                            double y = (py + (sy + 0.5) / ss - offset) * scale;
                            bool on = false;
                            foreach (var s in c.Shapes)
                                if (s.Contains(x, y)) on = !s.Erase;
                            if (on) hits++;
                        }
                    alpha[py * size + px] = hits / (float)(ss * ss);
                }
            return alpha;
        }

        /// <summary>Alpha mask of the unit symbol.</summary>
        public static float[] Glyph(UnitIcon icon, int size, double pad = 0.08) => Raster(Draw(icon), size, pad);

        /// <summary>Heater-shield silhouette; <paramref name="inset"/> shrinks it (for the coloured field inside a rim).</summary>
        public static float[] Shield(int size, double inset = 0)
        {
            var c = new Canvas();
            var pts = new List<double>();
            double l = 6 + inset, r = 94 - inset, top = 4 + inset, bottom = 97 - inset * 1.4, shoulder = 46;
            pts.AddRange(new[] { l, top, r, top, r, shoulder });
            // Right flank curves to the point, then mirror up the left flank (quadratic Béziers).
            for (int i = 1; i <= 12; i++)
            {
                double t = i / 12.0;
                pts.Add((1 - t) * (1 - t) * r + 2 * (1 - t) * t * r + t * t * 50);
                pts.Add((1 - t) * (1 - t) * shoulder + 2 * (1 - t) * t * (bottom - 18) + t * t * bottom);
            }
            for (int i = 1; i <= 12; i++)
            {
                double t = i / 12.0;
                pts.Add((1 - t) * (1 - t) * 50 + 2 * (1 - t) * t * l + t * t * l);
                pts.Add((1 - t) * (1 - t) * bottom + 2 * (1 - t) * t * (bottom - 18) + t * t * shoulder);
            }
            c.Poly(pts.ToArray());
            return Raster(c, size, 0);
        }

        // ------------------------------------------------------------------ the art

        static Canvas Draw(UnitIcon icon)
        {
            var c = new Canvas();
            switch (icon)
            {
                case UnitIcon.Sword:
                    c.Rotate(45);
                    c.Poly(41, 16, 50, 0, 59, 16, 59, 64, 41, 64);
                    c.Line(50, 14, 50, 58, 3, erase: true); // fuller
                    c.Rect(26, 62, 74, 72);
                    c.Rect(44, 72, 56, 88);
                    c.Circle(50, 92, 8);
                    break;

                case UnitIcon.Spear:
                    c.Rotate(40);
                    c.Line(50, 26, 50, 98, 10);
                    c.Poly(50, 0, 65, 24, 58, 34, 42, 34, 35, 24);
                    c.Rect(40, 34, 60, 40);
                    break;

                case UnitIcon.Bow:
                    c.Rotate(-40);
                    c.Arc(8, 50, 50, -58, 58, 10);
                    c.Line(34.5, 7.6, 34.5, 92.4, 2.5);
                    c.Line(20, 50, 84, 50, 6);
                    c.Poly(98, 50, 80, 40, 84, 50, 80, 60);
                    c.Poly(14, 42, 28, 42, 34, 50, 28, 58, 14, 58, 20, 50);
                    break;

                case UnitIcon.Crossbow:
                    c.Rect(44, 30, 56, 96);
                    c.Arc(50, 92, 58, 238, 302, 8);
                    c.Line(19, 43, 81, 43, 2.5);
                    c.Line(50, 8, 50, 42, 4.5);
                    c.Poly(50, 0, 58, 14, 42, 14);
                    c.Rect(40, 68, 60, 74, erase: true);
                    break;

                case UnitIcon.Horse:
                    c.Poly(46, 4, 52, 18, 60, 4, 66, 20, 78, 30, 88, 50, 92, 74, 90, 96, 50, 96, 50, 80, 42, 68,
                           30, 72, 16, 76, 8, 68, 10, 56, 26, 36, 38, 22);
                    c.Circle(36, 38, 4.5, erase: true);
                    c.Line(70, 26, 86, 60, 3, erase: true); // mane line
                    c.Circle(14, 64, 2.5, erase: true);     // nostril
                    break;

                case UnitIcon.Catapult:
                    c.Rect(10, 70, 90, 79);
                    c.Line(30, 72, 48, 42, 6);
                    c.Line(66, 72, 48, 42, 6);
                    c.Line(24, 74, 80, 16, 6);
                    c.Sector(84, 14, 12, 110, 290);
                    c.Circle(26, 84, 11); c.Circle(26, 84, 5, erase: true);
                    c.Circle(74, 84, 11); c.Circle(74, 84, 5, erase: true);
                    break;

                case UnitIcon.Cannon:
                    c.Rotate(-18);
                    c.Poly(18, 40, 86, 36, 86, 56, 18, 60);
                    c.Rect(84, 32, 94, 60);
                    c.Circle(14, 50, 9);
                    c.Rotate(0);
                    c.Circle(44, 70, 22); c.Circle(44, 70, 16, erase: true);
                    for (int i = 0; i < 4; i++)
                    {
                        double a = i * Math.PI / 4;
                        c.Line(44 - 17 * Math.Cos(a), 70 - 17 * Math.Sin(a), 44 + 17 * Math.Cos(a), 70 + 17 * Math.Sin(a), 3.5);
                    }
                    c.Circle(44, 70, 6);
                    break;

                case UnitIcon.Rocket:
                    c.Rotate(45);
                    c.Poly(50, 0, 62, 20, 62, 72, 38, 72, 38, 20);
                    c.Poly(38, 52, 24, 80, 24, 90, 38, 76);
                    c.Poly(62, 52, 76, 80, 76, 90, 62, 76);
                    c.Poly(42, 72, 58, 72, 54, 84, 46, 84);
                    c.Poly(46, 88, 54, 88, 50, 100);
                    c.Circle(50, 32, 6, erase: true);
                    break;

                case UnitIcon.Musket:
                    c.Rotate(-45);
                    c.Line(34, 46, 94, 46, 8);
                    c.Poly(0, 50, 36, 41, 46, 42, 46, 53, 32, 58, 4, 68);
                    c.Line(88, 44, 100, 44, 3.5);
                    c.Rect(50, 49, 56, 58);
                    break;

                case UnitIcon.Helmet:
                    c.Sector(50, 62, 38, 180, 360);
                    c.Rect(4, 58, 96, 68);
                    c.Rect(22, 68, 30, 84);
                    c.Line(24, 84, 50, 92, 4);
                    c.Line(12, 50, 88, 50, 3, erase: true);
                    break;

                case UnitIcon.Tank:
                    c.Poly(4, 60, 96, 60, 88, 86, 12, 86);
                    for (int i = 0; i < 5; i++) c.Circle(22 + i * 14, 76, 5, erase: true);
                    c.Poly(28, 40, 64, 40, 72, 58, 22, 58);
                    c.Rect(66, 45, 98, 51);
                    c.Rect(40, 32, 50, 40);
                    break;

                case UnitIcon.SailShip:
                    c.Poly(4, 62, 96, 62, 82, 82, 18, 82);
                    c.Line(50, 8, 50, 64, 4.5);
                    c.Poly(54, 10, 84, 52, 54, 56);
                    c.Poly(46, 16, 46, 56, 18, 52);
                    c.Poly(50, 2, 64, 6, 50, 10);
                    for (int i = 0; i < 5; i++) c.Line(28 + i * 11, 82, 22 + i * 11, 96, 3);
                    break;

                case UnitIcon.Steamship:
                    c.Poly(2, 62, 98, 62, 86, 84, 14, 84);
                    c.Rect(30, 46, 72, 62);
                    c.Rect(38, 34, 60, 46);
                    c.Rect(62, 20, 72, 46);
                    c.Rect(74, 52, 94, 56);
                    c.Rect(10, 52, 28, 56);
                    for (int i = 0; i < 4; i++) c.Circle(36 + i * 10, 54, 2.5, erase: true);
                    c.Circle(70, 12, 4); c.Circle(78, 6, 3);
                    break;

                case UnitIcon.Carrier:
                    c.Poly(2, 56, 98, 56, 90, 80, 10, 80);
                    c.Rect(66, 36, 80, 56);
                    c.Rect(70, 24, 74, 36);
                    c.Rect(14, 60, 90, 62, erase: true);
                    break;

                case UnitIcon.Fighter:
                    c.Line(50, 10, 50, 90, 11);
                    c.Poly(50, 0, 56, 12, 44, 12);
                    c.Poly(50, 34, 98, 56, 98, 64, 50, 54, 2, 64, 2, 56);
                    c.Poly(50, 78, 72, 92, 72, 98, 50, 92, 28, 98, 28, 92);
                    c.Circle(50, 24, 3.5, erase: true);
                    break;

                case UnitIcon.Bomber:
                    c.Line(50, 6, 50, 92, 13);
                    c.Poly(50, 30, 99, 48, 99, 56, 50, 48, 1, 56, 1, 48);
                    c.Poly(50, 80, 76, 92, 76, 98, 50, 94, 24, 98, 24, 92);
                    foreach (var x in new[] { 18.0, 34, 66, 82 }) c.Rect(x - 3.5, 38, x + 3.5, 54);
                    break;

                case UnitIcon.Jet:
                    c.Poly(50, 0, 58, 22, 94, 72, 94, 80, 60, 74, 66, 92, 66, 98, 34, 98, 34, 92, 40, 74, 6, 80, 6, 72, 42, 22);
                    c.Cut(50, 18, 54, 32, 46, 32);
                    break;

                case UnitIcon.Scout:
                    c.Sector(50, 88, 58, 222, 318);
                    c.Sector(50, 12, 58, 42, 138);
                    c.Circle(50, 50, 16, erase: true);
                    c.Circle(50, 50, 9);
                    c.Circle(46, 46, 3, erase: true);
                    break;

                case UnitIcon.Settler:
                    c.Poly(50, 10, 94, 86, 6, 86);
                    c.Cut(50, 44, 66, 86, 34, 86);
                    c.Line(50, 10, 44, 0, 4);
                    c.Line(50, 10, 56, 0, 4);
                    c.Rect(2, 86, 98, 94);
                    break;

                case UnitIcon.Worker:
                    c.Rotate(-45);
                    c.Line(50, 30, 50, 98, 8);
                    c.Poly(22, 14, 78, 14, 78, 34, 22, 34);
                    c.Poly(22, 14, 10, 24, 22, 34);
                    break;

                case UnitIcon.Ram:
                    c.Poly(10, 44, 50, 14, 90, 44, 84, 48, 50, 24, 16, 48);
                    c.Line(16, 46, 16, 72, 5); c.Line(84, 46, 84, 72, 5);
                    c.Line(2, 58, 88, 58, 12);
                    c.Circle(94, 58, 8);
                    c.Rect(10, 72, 90, 78);
                    c.Circle(26, 86, 9); c.Circle(74, 86, 9);
                    break;

                case UnitIcon.SiegeTower:
                    c.Poly(26, 18, 74, 18, 82, 84, 18, 84);
                    for (int i = 0; i < 4; i++) c.Rect(22 + i * 16, 6, 30 + i * 16, 18);
                    c.Rect(40, 30, 60, 46, erase: true);
                    c.Line(30, 56, 70, 56, 3, erase: true);
                    c.Line(28, 68, 72, 68, 3, erase: true);
                    c.Circle(30, 88, 9); c.Circle(70, 88, 9);
                    break;

                case UnitIcon.General:
                    c.Star(50, 44, 34, 14);
                    c.Arc(50, 50, 44, 110, 200, 6);
                    c.Arc(50, 50, 44, -20, 70, 6);
                    for (int i = 0; i < 4; i++)
                    {
                        double a = (120 + i * 22) * Math.PI / 180, b = (60 - i * 22) * Math.PI / 180;
                        c.Circle(50 + 44 * Math.Cos(a) - 5, 50 + 44 * Math.Sin(a), 5);
                        c.Circle(50 + 44 * Math.Cos(b) + 5, 50 + 44 * Math.Sin(b), 5);
                    }
                    break;

                case UnitIcon.Scientist:
                    c.Poly(40, 6, 60, 6, 60, 38, 88, 88, 84, 96, 16, 96, 12, 88, 40, 38);
                    c.Cut(44, 12, 56, 12, 56, 40, 76, 76, 24, 76, 44, 40);
                    c.Circle(44, 64, 5); c.Circle(58, 70, 4); c.Circle(50, 54, 3);
                    c.Rect(34, 2, 66, 8);
                    break;

                case UnitIcon.Engineer:
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        c.Line(50 + 30 * Math.Cos(a), 50 + 30 * Math.Sin(a), 50 + 44 * Math.Cos(a), 50 + 44 * Math.Sin(a), 13);
                    }
                    c.Circle(50, 50, 34);
                    c.Circle(50, 50, 14, erase: true);
                    break;

                case UnitIcon.Merchant:
                    c.Circle(50, 50, 44);
                    c.Circle(50, 50, 36, erase: true);
                    c.Circle(50, 50, 32);
                    c.Cut(50, 26, 68, 50, 50, 74, 32, 50);
                    c.Poly(50, 36, 60, 50, 50, 64, 40, 50);
                    break;

                case UnitIcon.Artist:
                    c.Circle(46, 52, 42);
                    c.Circle(64, 70, 10, erase: true);
                    c.Circle(28, 36, 7, erase: true);
                    c.Circle(48, 26, 7, erase: true);
                    c.Circle(24, 60, 7, erase: true);
                    c.Circle(70, 36, 7, erase: true);
                    c.Line(92, 4, 58, 50, 5);
                    break;

                case UnitIcon.Prophet:
                    c.Poly(50, 2, 70, 34, 76, 58, 70, 80, 50, 96, 30, 80, 24, 58, 30, 34, 40, 44);
                    c.Cut(50, 46, 60, 64, 58, 80, 50, 88, 42, 80, 40, 64);
                    c.Poly(50, 60, 55, 72, 50, 82, 45, 72);
                    break;
            }
            return c;
        }
    }
}
