using System;
using System.Collections.Generic;
using System.Linq;

namespace BeamShapeExplorer.ShearMoment
{
    public enum SupportType { Fixed, Pinned, Roller }

    public enum LoadType { Point, Distributed }

    /// <summary>A support at distance <see cref="X"/> along the span.</summary>
    public struct SolverSupport
    {
        public double X;
        public SupportType Type;

        public SolverSupport(double x, SupportType type) { X = x; Type = type; }
    }

    /// <summary>
    /// A load on the span, acting against the "up" direction (gravity) when positive.
    /// Point: force <see cref="Wa"/> (kN) at <see cref="A"/>.
    /// Distributed: intensity varying linearly from <see cref="Wa"/> at <see cref="A"/> to <see cref="Wb"/> at <see cref="B"/> (kN/m).
    /// </summary>
    public struct SolverLoad
    {
        public LoadType Type;
        public double A, B, Wa, Wb;
        /// <summary>Optional group name (e.g. "Self-weight"); only used to label the preview, ignored by the solver.</summary>
        public string Group;

        public static SolverLoad Point(double x, double p) =>
            new SolverLoad { Type = LoadType.Point, A = x, B = x, Wa = p, Wb = p };

        public static SolverLoad Distributed(double a, double b, double wa, double wb) =>
            new SolverLoad { Type = LoadType.Distributed, A = a, B = b, Wa = wa, Wb = wb };
    }

    /// <summary>
    /// Reactions and internal forces of a straight beam on any number of fixed, pinned and roller supports
    /// (statically determinate or indeterminate), under point loads and linearly varying distributed loads.
    ///
    /// The reactions come from a direct-stiffness (Euler-Bernoulli) analysis with uniform EI, nodes at the span ends
    /// and supports, and exact consistent nodal loads, which is exact at the nodes for any number of loads.
    /// Shear and moment then follow exactly from
    /// equilibrium of the part of the beam left of each section. Pinned and roller supports behave the same here,
    /// because only transverse loads are modelled (they differ only for axial forces).
    ///
    /// Sign convention (the same as the Beam Shape Explorer load envelopes):
    ///   * loads are positive downward (against "up"); reactions are positive upward
    ///   * shear V(x) = sum of the upward forces left of x (so V = +R just right of a left support)
    ///   * <see cref="MomentSagging"/> is positive for sagging; <see cref="Moment"/> (= -sagging) is the BSE "Mu"
    ///     convention, negative for sagging and positive for hogging
    ///   * reaction moments are positive counterclockwise, looking with the span running left to right and "up" up
    /// </summary>
    public class BeamSolution
    {
        public double Length { get; private set; }
        public IReadOnlyList<SolverSupport> Supports { get; private set; }
        public IReadOnlyList<SolverLoad> Loads { get; private set; }

        /// <summary>Vertical reaction of each support (input order), positive upward.</summary>
        public double[] Reactions { get; private set; }

        /// <summary>Reaction moment of each support (input order), positive counterclockwise; 0 unless fixed.</summary>
        public double[] ReactionMoments { get; private set; }

        private double _tol;

        /// <summary>Node locations of the stiffness model, and the vertical displacement (positive upward, m) and
        /// rotation (rad, counterclockwise positive) at each node. Meaningful when a bending stiffness was given.</summary>
        public double[] NodeX { get; private set; }
        public double[] NodeDisplacement { get; private set; }
        public double[] NodeRotation { get; private set; }

        private BeamSolution() { }

        /// <summary>Solves the beam. Throws <see cref="ArgumentException"/> for bad input and
        /// <see cref="InvalidOperationException"/> when the supports cannot carry the loads (a mechanism).</summary>
        /// <param name="bendingStiffness">Optional EI(x) (kN-m2). Without it EI = 1, which gives the same reactions for a
        /// uniform beam; with it, the reactions of indeterminate beams and the displacements follow the varying stiffness.</param>
        /// <param name="minElements">Minimum number of elements along the span (for a varying stiffness and for
        /// smooth displacements). 0 places nodes only at the span ends and supports.</param>
        public static BeamSolution Solve(double length, IList<SolverSupport> supports, IList<SolverLoad> loads,
                                         Func<double, double> bendingStiffness = null, int minElements = 0)
        {
            if (!(length > 0)) throw new ArgumentException("The span length must be greater than zero.");
            if (supports == null || supports.Count == 0) throw new ArgumentException("Add at least one support.");
            double tol = 1e-9 * length;
            double merge = 1e-6 * length;

            var sups = supports.ToList();
            foreach (SolverSupport s in sups)
                if (s.X < -merge || s.X > length + merge)
                    throw new ArgumentException("A support at " + s.X.ToString("G4") + " is outside the span (0 to " + length.ToString("G4") + ").");
            for (int i = 0; i < sups.Count; i++)
                for (int j = i + 1; j < sups.Count; j++)
                    if (Math.Abs(sups[i].X - sups[j].X) <= merge)
                        throw new ArgumentException("Two supports are at the same location (" + sups[i].X.ToString("G4") + "). Use one support there.");
            for (int i = 0; i < sups.Count; i++)
                sups[i] = new SolverSupport(Clamp(sups[i].X, 0, length), sups[i].Type);

            var lds = new List<SolverLoad>();
            foreach (SolverLoad l in loads ?? new List<SolverLoad>())
            {
                if (double.IsNaN(l.A) || double.IsNaN(l.B) || double.IsNaN(l.Wa) || double.IsNaN(l.Wb))
                    throw new ArgumentException("A load has a missing location or magnitude.");
                if (l.Type == LoadType.Point)
                {
                    if (l.A < -merge || l.A > length + merge)
                        throw new ArgumentException("A point load at " + l.A.ToString("G4") + " is outside the span (0 to " + length.ToString("G4") + ").");
                    SolverLoad pl = SolverLoad.Point(Clamp(l.A, 0, length), l.Wa);
                    pl.Group = l.Group;
                    lds.Add(pl);
                }
                else
                {
                    double a = l.A, b = l.B, wa = l.Wa, wb = l.Wb;
                    if (b < a) { double t = a; a = b; b = t; t = wa; wa = wb; wb = t; }
                    if (b < -merge || a > length + merge)
                        throw new ArgumentException("A distributed load from " + a.ToString("G4") + " to " + b.ToString("G4") + " is outside the span.");
                    // clip to the span, keeping the linear profile
                    double ca = Clamp(a, 0, length), cb = Clamp(b, 0, length);
                    if (cb - ca <= merge) continue;
                    double slope = b - a > 0 ? (wb - wa) / (b - a) : 0;
                    SolverLoad dl = SolverLoad.Distributed(ca, cb, wa + slope * (ca - a), wa + slope * (cb - a));
                    dl.Group = l.Group;
                    lds.Add(dl);
                }
            }

            // ---- nodes only at the span ends and supports; loads inside an element are turned into exact
            //      equivalent nodal forces (consistent loads), so the nodal results stay exact for any number of loads
            var xs = new List<double> { 0, length };
            xs.AddRange(sups.Select(s => s.X));
            xs.Sort();
            var baseNodes = new List<double>();
            foreach (double x in xs)
                if (baseNodes.Count == 0 || x - baseNodes[baseNodes.Count - 1] > merge) baseNodes.Add(x);
            var nodes = new List<double> { baseNodes[0] };
            for (int i = 1; i < baseNodes.Count; i++)
            {
                double a0 = baseNodes[i - 1], b0 = baseNodes[i];
                int pieces = minElements > 0 ? Math.Max(1, (int)Math.Ceiling((b0 - a0) / length * minElements - 1e-9)) : 1;
                for (int k = 1; k <= pieces; k++) nodes.Add(a0 + (b0 - a0) * k / pieces);
            }
            Func<double, int> NodeOf = x =>
            {
                int best = 0;
                for (int i = 1; i < nodes.Count; i++)
                    if (Math.Abs(nodes[i] - x) < Math.Abs(nodes[best] - x)) best = i;
                return best;
            };

            int n = nodes.Count, ndof = 2 * n;
            var K = new double[ndof, ndof];
            var F = new double[ndof];
            for (int e = 0; e < n - 1; e++)
            {
                double x1 = nodes[e], x2 = nodes[e + 1], l = x2 - x1;
                double[,] k = ElementStiffness(l);
                if (bendingStiffness != null)
                {
                    double ei = bendingStiffness(0.5 * (x1 + x2));
                    if (!(ei > 0) || double.IsInfinity(ei))
                        throw new ArgumentException("The bending stiffness must be greater than zero (at " + (0.5 * (x1 + x2)).ToString("G4") + ").");
                    for (int r = 0; r < 4; r++)
                        for (int c = 0; c < 4; c++) k[r, c] *= ei;
                }
                int[] map = { 2 * e, 2 * e + 1, 2 * e + 2, 2 * e + 3 };
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 4; c++) K[map[r], map[c]] += k[r, c];

                foreach (SolverLoad ld in lds)
                {
                    if (ld.Type == LoadType.Point)
                    {
                        // a point load on a node belongs to the first element that touches it
                        bool inside = ld.A >= x1 - merge && (ld.A < x2 - merge || (e == n - 2 && ld.A <= x2 + merge));
                        if (!inside) continue;
                        double[] N = Shape(Clamp((ld.A - x1) / l, 0, 1), l);
                        for (int r = 0; r < 4; r++) F[map[r]] -= ld.Wa * N[r];
                        continue;
                    }
                    double a = Math.Max(ld.A, x1), b = Math.Min(ld.B, x2);
                    if (b - a <= 0) continue;
                    // integrate N_i(x) q(x) over [a, b]: degree <= 4, so 3-point Gauss-Legendre is exact
                    double half = 0.5 * (b - a), mid = 0.5 * (a + b);
                    for (int g = 0; g < 3; g++)
                    {
                        double x = mid + half * GaussPoints[g];
                        double q = -Intensity(ld, x); // upward positive
                        double[] N = Shape((x - x1) / l, l);
                        for (int r = 0; r < 4; r++) F[map[r]] += GaussWeights[g] * half * q * N[r];
                    }
                }
            }

            // ---- restraints
            var restrained = new bool[ndof];
            foreach (SolverSupport s in sups)
            {
                int node = NodeOf(s.X);
                restrained[2 * node] = true;
                if (s.Type == SupportType.Fixed) restrained[2 * node + 1] = true;
            }
            int[] free = Enumerable.Range(0, ndof).Where(i => !restrained[i]).ToArray();

            double[] d = new double[ndof];
            if (free.Length > 0)
            {
                var A = new double[free.Length, free.Length];
                var b = new double[free.Length];
                for (int i = 0; i < free.Length; i++)
                {
                    b[i] = F[free[i]];
                    for (int j = 0; j < free.Length; j++) A[i, j] = K[free[i], free[j]];
                }
                double[] u = SolveLinear(A, b);
                if (u == null)
                    throw new InvalidOperationException(
                        "The beam is unstable: the supports cannot hold it in place (for example a single pinned or roller " +
                        "support). Add supports, or make one of them fixed.");
                for (int i = 0; i < free.Length; i++) d[free[i]] = u[i];
            }

            // ---- reactions R = K d - F at the restrained degrees of freedom
            var sol = new BeamSolution
            {
                Length = length,
                Supports = sups,
                Loads = lds,
                Reactions = new double[sups.Count],
                ReactionMoments = new double[sups.Count],
                _tol = tol,
                NodeX = nodes.ToArray(),
                NodeDisplacement = Enumerable.Range(0, n).Select(i => d[2 * i]).ToArray(),
                NodeRotation = Enumerable.Range(0, n).Select(i => d[2 * i + 1]).ToArray(),
            };
            for (int i = 0; i < sups.Count; i++)
            {
                int node = NodeOf(sups[i].X);
                sol.Reactions[i] = Residual(K, d, F, 2 * node);
                if (sups[i].Type == SupportType.Fixed) sol.ReactionMoments[i] = Residual(K, d, F, 2 * node + 1);
            }

            // reject results that do not balance (e.g. a near-mechanism that slipped past the pivot check)
            double sumF = sol.Reactions.Sum() - lds.Sum(TotalForce);
            double scale = Math.Max(1e-12, lds.Sum(l => Math.Abs(TotalForce(l))) + sol.Reactions.Sum(Math.Abs));
            if (Math.Abs(sumF) > 1e-5 * scale)
                throw new InvalidOperationException("The beam is unstable or badly conditioned; check the supports.");
            return sol;
        }

        // ================================================================== internal forces

        /// <summary>Shear at <paramref name="x"/>: side &lt; 0 gives the value just left of x, side &gt;= 0 just right of x.</summary>
        public double Shear(double x, int side = 1)
        {
            double v = 0;
            for (int i = 0; i < Supports.Count; i++)
                if (LeftOf(Supports[i].X, x, side)) v += Reactions[i];
            foreach (SolverLoad l in Loads)
            {
                if (l.Type == LoadType.Point) { if (LeftOf(l.A, x, side)) v -= l.Wa; }
                else v -= DistributedForceLeftOf(l, x);
            }
            return v;
        }

        /// <summary>Bending moment at x, positive for sagging.</summary>
        public double MomentSagging(double x, int side = 1)
        {
            double m = 0;
            for (int i = 0; i < Supports.Count; i++)
                if (LeftOf(Supports[i].X, x, side))
                    m += Reactions[i] * (x - Supports[i].X) - ReactionMoments[i];
            foreach (SolverLoad l in Loads)
            {
                if (l.Type == LoadType.Point) { if (LeftOf(l.A, x, side)) m -= l.Wa * (x - l.A); }
                else m -= DistributedMomentLeftOf(l, x);
            }
            return m;
        }

        /// <summary>Vertical displacement at x (m, positive upward), interpolated with the element shape functions.</summary>
        public double Displacement(double x)
        {
            int e = ElementAt(x, out double xi, out double l);
            double[] N = Shape(xi, l);
            return N[0] * NodeDisplacement[e] + N[1] * NodeRotation[e] + N[2] * NodeDisplacement[e + 1] + N[3] * NodeRotation[e + 1];
        }

        /// <summary>Rotation (slope) at x (rad, counterclockwise positive).</summary>
        public double Rotation(double x)
        {
            int e = ElementAt(x, out double xi, out double l);
            double x2 = xi * xi;
            double d1 = -6 * xi + 6 * x2, d2 = l * (1 - 4 * xi + 3 * x2), d3 = 6 * xi - 6 * x2, d4 = l * (3 * x2 - 2 * xi);
            return (d1 * NodeDisplacement[e] + d2 * NodeRotation[e] + d3 * NodeDisplacement[e + 1] + d4 * NodeRotation[e + 1]) / l;
        }

        private int ElementAt(double x, out double xi, out double l)
        {
            int e = 0;
            while (e < NodeX.Length - 2 && x > NodeX[e + 1]) e++;
            l = NodeX[e + 1] - NodeX[e];
            xi = Clamp((x - NodeX[e]) / l, 0, 1);
            return e;
        }

        /// <summary>Bending moment in the Beam Shape Explorer convention ("Mu"): negative for sagging.</summary>
        public double Moment(double x, int side = 1) => -MomentSagging(x, side);

        /// <summary>Locations where shear or moment can jump (supports and point loads).</summary>
        public List<double> Discontinuities()
        {
            var xs = Supports.Select(s => s.X).Concat(Loads.Where(l => l.Type == LoadType.Point).Select(l => l.A))
                             .Where(x => x > _tol && x < Length - _tol).Distinct().ToList();
            xs.Sort();
            return xs;
        }

        /// <summary>
        /// Stations for plotting: <paramref name="samples"/> evenly spaced points plus every support, point load and
        /// distributed load boundary. Each support and point load inside the span appears twice (left and right
        /// value), so jumps in the diagrams are drawn vertically. Returns (x, side) pairs in order.
        /// </summary>
        public List<KeyValuePair<double, int>> PlotStations(int samples)
        {
            var xs = new List<double>();
            samples = Math.Max(2, samples);
            for (int i = 0; i < samples; i++) xs.Add(Length * i / (samples - 1));
            foreach (SolverLoad l in Loads) { xs.Add(l.A); xs.Add(l.B); }
            xs.AddRange(Supports.Select(s => s.X));
            xs.Sort();
            var jumps = new HashSet<double>(Discontinuities());
            var stations = new List<KeyValuePair<double, int>>();
            double last = double.NegativeInfinity;
            foreach (double x in xs)
            {
                if (x - last <= _tol) continue;
                last = x;
                bool jump = jumps.Any(j => Math.Abs(j - x) <= _tol);
                if (jump) stations.Add(new KeyValuePair<double, int>(x, -1));
                stations.Add(new KeyValuePair<double, int>(x, x >= Length - _tol ? -1 : 1));
            }
            return stations;
        }

        private bool LeftOf(double xi, double x, int side) => side < 0 ? xi < x - _tol : xi <= x + _tol;

        // ================================================================== helpers

        private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));

        private static double Intensity(SolverLoad d, double x) =>
            d.B - d.A > 0 ? d.Wa + (d.Wb - d.Wa) * (x - d.A) / (d.B - d.A) : d.Wa;

        private static double TotalForce(SolverLoad l) =>
            l.Type == LoadType.Point ? l.Wa : 0.5 * (l.Wa + l.Wb) * (l.B - l.A);

        private static double DistributedForceLeftOf(SolverLoad l, double x)
        {
            if (x <= l.A) return 0;
            double xe = Math.Min(x, l.B), dd = xe - l.A;
            return dd * (l.Wa + Intensity(l, xe)) * 0.5;
        }

        /// <summary>Moment about x of the part of a distributed load left of x: integral of w(s) (x - s) ds.</summary>
        private static double DistributedMomentLeftOf(SolverLoad l, double x)
        {
            if (x <= l.A) return 0;
            double dd = Math.Min(x, l.B) - l.A;
            double k = l.B - l.A > 0 ? (l.Wb - l.Wa) / (l.B - l.A) : 0;
            double xa = x - l.A;
            return l.Wa * (xa * dd - dd * dd / 2) + k * (xa * dd * dd / 2 - dd * dd * dd / 3);
        }

        private static double Residual(double[,] K, double[] d, double[] F, int row)
        {
            double r = -F[row];
            for (int j = 0; j < d.Length; j++) r += K[row, j] * d[j];
            return r;
        }

        private static readonly double[] GaussPoints = { -Math.Sqrt(0.6), 0.0, Math.Sqrt(0.6) };
        private static readonly double[] GaussWeights = { 5.0 / 9.0, 8.0 / 9.0, 5.0 / 9.0 };

        /// <summary>Hermite shape functions of a beam element at xi = (x - x1) / l: (v1, theta1, v2, theta2).</summary>
        private static double[] Shape(double xi, double l)
        {
            double x2 = xi * xi, x3 = x2 * xi;
            return new[] { 1 - 3 * x2 + 2 * x3, l * (xi - 2 * x2 + x3), 3 * x2 - 2 * x3, l * (x3 - x2) };
        }

        /// <summary>Euler-Bernoulli beam element stiffness with EI = 1 (reactions do not depend on a uniform EI).</summary>
        private static double[,] ElementStiffness(double l)
        {
            double l2 = l * l, l3 = l2 * l;
            return new double[,]
            {
                { 12 / l3, 6 / l2, -12 / l3, 6 / l2 },
                { 6 / l2, 4 / l, -6 / l2, 2 / l },
                { -12 / l3, -6 / l2, 12 / l3, -6 / l2 },
                { 6 / l2, 2 / l, -6 / l2, 4 / l },
            };
        }

        /// <summary>
        /// Solves the reduced stiffness system. The matrix is symmetric positive (semi-)definite and banded (each node
        /// only couples to its neighbours), so Gaussian elimination without pivoting, limited to the band, is stable and
        /// costs O(n b^2). Returns null if the matrix is singular (the supports allow a rigid-body motion).
        /// </summary>
        private static double[] SolveLinear(double[,] A, double[] b)
        {
            int n = b.Length;
            int band = 0;
            double maxDiag = 0;
            for (int i = 0; i < n; i++)
            {
                maxDiag = Math.Max(maxDiag, Math.Abs(A[i, i]));
                for (int j = 0; j < n; j++)
                    if (A[i, j] != 0) band = Math.Max(band, Math.Abs(i - j));
            }
            double eps = 1e-11 * Math.Max(maxDiag, 1e-300);
            for (int c = 0; c < n; c++)
            {
                if (A[c, c] < eps) return null; // a positive-definite system never gets a (near-)zero pivot
                int last = Math.Min(n - 1, c + band);
                for (int r = c + 1; r <= last; r++)
                {
                    double f = A[r, c] / A[c, c];
                    if (f == 0) continue;
                    for (int j = c; j <= last; j++) A[r, j] -= f * A[c, j];
                    b[r] -= f * b[c];
                }
            }
            var x = new double[n];
            for (int r = n - 1; r >= 0; r--)
            {
                double sum = b[r];
                int last = Math.Min(n - 1, r + band);
                for (int j = r + 1; j <= last; j++) sum -= A[r, j] * x[j];
                x[r] = sum / A[r, r];
            }
            return x;
        }
    }
}
