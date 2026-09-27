using System;
using System.Collections.Generic;

namespace Crucible.Core.Hex
{
    /// <summary>
    /// Axial coordinate on a pointy-top hex grid. The implicit third cube axis is S = -Q - R.
    /// Direction indices run counter-clockwise starting East: 0=E, 1=NE, 2=NW, 3=W, 4=SW, 5=SE.
    /// </summary>
    [Serializable]
    public readonly struct HexCoord : IEquatable<HexCoord>
    {
        public readonly int Q;
        public readonly int R;
        public int S => -Q - R;

        public const int DirectionCount = 6;

        static readonly HexCoord[] Directions =
        {
            new HexCoord(1, 0), new HexCoord(1, -1), new HexCoord(0, -1),
            new HexCoord(-1, 0), new HexCoord(-1, 1), new HexCoord(0, 1),
        };

        static readonly double Sqrt3 = Math.Sqrt(3.0);

        public HexCoord(int q, int r)
        {
            Q = q;
            R = r;
        }

        public static HexCoord Direction(int dir) => Directions[((dir % 6) + 6) % 6];

        public static int OppositeDirection(int dir) => (dir + 3) % 6;

        public HexCoord Neighbor(int dir) => this + Direction(dir);

        public IEnumerable<HexCoord> Neighbors()
        {
            for (int i = 0; i < DirectionCount; i++)
                yield return this + Directions[i];
        }

        /// <summary>Direction index from this hex to an adjacent hex, or -1 if not adjacent.</summary>
        public int DirectionTo(HexCoord neighbor)
        {
            var delta = neighbor - this;
            for (int i = 0; i < DirectionCount; i++)
                if (Directions[i] == delta) return i;
            return -1;
        }

        public int DistanceTo(HexCoord other)
        {
            var d = this - other;
            return (Math.Abs(d.Q) + Math.Abs(d.R) + Math.Abs(d.S)) / 2;
        }

        /// <summary>All hexes exactly <paramref name="radius"/> steps away (just this hex for radius 0).</summary>
        public IEnumerable<HexCoord> Ring(int radius)
        {
            if (radius <= 0)
            {
                yield return this;
                yield break;
            }
            var hex = this + Direction(4) * radius;
            for (int side = 0; side < DirectionCount; side++)
            {
                for (int step = 0; step < radius; step++)
                {
                    yield return hex;
                    hex = hex.Neighbor(side);
                }
            }
        }

        /// <summary>All hexes within <paramref name="radius"/> steps, including this one.</summary>
        public IEnumerable<HexCoord> Range(int radius)
        {
            for (int dq = -radius; dq <= radius; dq++)
            {
                int rMin = Math.Max(-radius, -dq - radius);
                int rMax = Math.Min(radius, -dq + radius);
                for (int dr = rMin; dr <= rMax; dr++)
                    yield return new HexCoord(Q + dq, R + dr);
            }
        }

        /// <summary>Hexes on the straight line from this hex to <paramref name="target"/>, both endpoints included.</summary>
        public List<HexCoord> LineTo(HexCoord target)
        {
            int n = DistanceTo(target);
            var result = new List<HexCoord>(n + 1);
            // Nudge to break ties consistently when the line runs exactly along a hex edge.
            double aq = Q + 1e-6, ar = R + 1e-6;
            double bq = target.Q + 1e-6, br = target.R + 1e-6;
            for (int i = 0; i <= n; i++)
            {
                double t = n == 0 ? 0.0 : (double)i / n;
                result.Add(Round(aq + (bq - aq) * t, ar + (br - ar) * t));
            }
            return result;
        }

        /// <summary>Rounds fractional axial coordinates to the nearest hex.</summary>
        public static HexCoord Round(double q, double r)
        {
            double s = -q - r;
            double rq = Math.Round(q), rr = Math.Round(r), rs = Math.Round(s);
            double dq = Math.Abs(rq - q), dr = Math.Abs(rr - r), ds = Math.Abs(rs - s);
            if (dq > dr && dq > ds) rq = -rr - rs;
            else if (dr > ds) rr = -rq - rs;
            return new HexCoord((int)rq, (int)rr);
        }

        /// <summary>Centre of this hex in the plane (x right, y "forward") for a hex of the given outer radius.</summary>
        public (double x, double y) ToPoint(double size)
        {
            double x = size * (Sqrt3 * Q + Sqrt3 / 2.0 * R);
            double y = size * (1.5 * R);
            return (x, y);
        }

        public static HexCoord FromPoint(double x, double y, double size)
        {
            double q = (Sqrt3 / 3.0 * x - 1.0 / 3.0 * y) / size;
            double r = (2.0 / 3.0 * y) / size;
            return Round(q, r);
        }

        /// <summary>Converts "odd-r" offset coordinates (col, row) used for rectangular map storage.</summary>
        public static HexCoord FromOffset(int col, int row)
        {
            int q = col - (row - (row & 1)) / 2;
            return new HexCoord(q, row);
        }

        public (int col, int row) ToOffset()
        {
            int col = Q + (R - (R & 1)) / 2;
            return (col, R);
        }

        public static HexCoord operator +(HexCoord a, HexCoord b) => new HexCoord(a.Q + b.Q, a.R + b.R);
        public static HexCoord operator -(HexCoord a, HexCoord b) => new HexCoord(a.Q - b.Q, a.R - b.R);
        public static HexCoord operator *(HexCoord a, int k) => new HexCoord(a.Q * k, a.R * k);
        public static bool operator ==(HexCoord a, HexCoord b) => a.Q == b.Q && a.R == b.R;
        public static bool operator !=(HexCoord a, HexCoord b) => !(a == b);

        public bool Equals(HexCoord other) => this == other;
        public override bool Equals(object obj) => obj is HexCoord other && this == other;
        public override int GetHashCode() => unchecked((Q * 397) ^ R);
        public override string ToString() => $"({Q},{R})";
    }
}
