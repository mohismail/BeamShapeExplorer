using System;
using System.Collections.Generic;

namespace BeamShapeExplorer.Optimization
{
    /// <summary>
    /// Sampling methods for sampling plans. All methods return points in the unit hypercube [0, 1]^d,
    /// which the plan scales to the slider bounds. A seeded <see cref="Random"/> makes every plan repeatable.
    /// The names and behaviour match python/sampling_plan.py.
    /// </summary>
    internal static class PlanSampling
    {
        public static readonly string[] Methods =
            { "lhs", "lhs-optimized", "lhs-centered", "sobol", "halton", "random", "grid" };

        public static readonly string[] StartModes = { "auto", "sampled", "random", "center", "current" };

        /// <summary>Canonical method name, "" when not set, or null when unknown.</summary>
        public static string NormalizeMethod(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string key = name.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
            switch (key)
            {
                case "latin": case "latin-hypercube": key = "lhs"; break;
                case "lhs-maximin": case "optimized-lhs": key = "lhs-optimized"; break;
                case "centered-lhs": key = "lhs-centered"; break;
                case "monte-carlo": case "montecarlo": case "uniform": case "mc": key = "random"; break;
                case "factorial": case "full-factorial": key = "grid"; break;
            }
            return Array.IndexOf(Methods, key) >= 0 ? key : null;
        }

        /// <summary>Canonical start mode, "" when not set, or null when unknown ("lhs" is accepted for "sampled").</summary>
        public static string NormalizeStart(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string key = name.Trim().ToLowerInvariant();
            if (key == "lhs") key = "sampled";
            return Array.IndexOf(StartModes, key) >= 0 ? key : null;
        }

        /// <summary>Levels per dimension for a full-factorial grid: the given list, or the largest even split of n.</summary>
        public static int[] GridLevels(IList<int> given, int n, int d)
        {
            var levels = new int[d];
            if (given != null && given.Count > 0)
            {
                if (given.Count != 1 && given.Count != d)
                    throw new ArgumentException("Grid Levels needs one value or " + d + " values (one per sampled dimension).");
                for (int j = 0; j < d; j++)
                {
                    levels[j] = given.Count == 1 ? given[0] : given[j];
                    if (levels[j] < 1) throw new ArgumentException("Grid Levels must be at least 1.");
                }
                return levels;
            }
            int k = d == 0 ? 1 : Math.Max(2, (int)Math.Floor(Math.Pow(n, 1.0 / d) + 1e-9));
            for (int j = 0; j < d; j++) levels[j] = k;
            return levels;
        }

        /// <summary>
        /// Samples n points in [0, 1]^d. For "grid" the number of points is the product of
        /// <paramref name="levels"/> instead of n.
        /// </summary>
        public static double[][] Unit(string method, int n, int d, Random rng, int[] levels = null)
        {
            switch (method)
            {
                case "lhs": return LatinHypercube(n, d, rng, false);
                case "lhs-centered": return LatinHypercube(n, d, rng, true);
                case "lhs-optimized": return OptimizedLatinHypercube(n, d, rng);
                case "sobol": return Sobol(n, d, rng);
                case "halton": return Halton(n, d, rng);
                case "random": return UniformRandom(n, d, rng);
                case "grid": return Grid(levels ?? GridLevels(null, n, d));
                default: throw new ArgumentException("Unknown sampling method \"" + method + "\".");
            }
        }

        private static double[][] Matrix(int n, int d)
        {
            var m = new double[n][];
            for (int i = 0; i < n; i++) m[i] = new double[d];
            return m;
        }

        private static int[] Permutation(int n, Random rng)
        {
            var p = new int[n];
            for (int i = 0; i < n; i++) p[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = p[i]; p[i] = p[j]; p[j] = t;
            }
            return p;
        }

        public static double[][] UniformRandom(int n, int d, Random rng)
        {
            var m = Matrix(n, d);
            for (int i = 0; i < n; i++)
                for (int j = 0; j < d; j++) m[i][j] = rng.NextDouble();
            return m;
        }

        /// <summary>Latin Hypercube: each dimension split into n strata with exactly one point in each.</summary>
        public static double[][] LatinHypercube(int n, int d, Random rng, bool centered)
        {
            var m = Matrix(n, d);
            for (int j = 0; j < d; j++)
            {
                int[] perm = Permutation(n, rng);
                for (int i = 0; i < n; i++)
                    m[i][j] = (perm[i] + (centered ? 0.5 : rng.NextDouble())) / n;
            }
            return m;
        }

        /// <summary>
        /// Space-filling Latin Hypercube: the best of several random Latin Hypercubes by the maximin criterion
        /// (largest smallest distance between two points). The number of candidates drops for large plans so
        /// the component stays responsive.
        /// </summary>
        public static double[][] OptimizedLatinHypercube(int n, int d, Random rng)
        {
            double work = Math.Max(1.0, (double)n * n * Math.Max(1, d));
            int candidates = (int)Math.Max(1, Math.Min(100, 2e7 / work));
            double[][] best = null;
            double bestScore = double.NegativeInfinity;
            for (int c = 0; c < candidates; c++)
            {
                double[][] m = LatinHypercube(n, d, rng, false);
                double score = MinDistanceSquared(m, bestScore);
                if (score > bestScore) { bestScore = score; best = m; }
            }
            return best;
        }

        private static double MinDistanceSquared(double[][] m, double stopBelow)
        {
            double min = double.PositiveInfinity;
            for (int a = 0; a < m.Length; a++)
                for (int b = a + 1; b < m.Length; b++)
                {
                    double s = 0;
                    for (int j = 0; j < m[a].Length; j++) { double t = m[a][j] - m[b][j]; s += t * t; }
                    if (s < min)
                    {
                        min = s;
                        if (min <= stopBelow) return min; // cannot beat the current best
                    }
                }
            return m.Length < 2 ? 0.0 : min;
        }

        private const int SobolBits = 30;

        /// <summary>
        /// Sobol sequence (Joe-Kuo direction numbers). With a <paramref name="rng"/>, a random digital shift is
        /// applied (randomized Sobol); with null, the plain sequence is returned (identical to SciPy's
        /// <c>qmc.Sobol(d, scramble=False)</c>).
        /// </summary>
        public static double[][] Sobol(int n, int d, Random rng)
        {
            if (d > SobolDirectionNumbers.MaxDimensions)
                throw new ArgumentException("Sobol sampling supports up to " + SobolDirectionNumbers.MaxDimensions +
                                            " dimensions; this plan has " + d + ".");
            var v = new uint[d, SobolBits];
            for (int k = 0; k < d; k++)
            {
                if (k == 0)
                {
                    for (int j = 0; j < SobolBits; j++) v[0, j] = 1;
                }
                else
                {
                    uint poly = SobolDirectionNumbers.Poly[k];
                    uint[] mInit = SobolDirectionNumbers.M[k];
                    int s = BitLength(poly) - 1;
                    for (int j = 0; j < s; j++) v[k, j] = mInit[j];
                    for (int j = s; j < SobolBits; j++)
                    {
                        uint newv = v[k, j - s];
                        uint pow2 = 1;
                        for (int q = 0; q < s; q++)
                        {
                            pow2 <<= 1;
                            if (((poly >> (s - 1 - q)) & 1) != 0) newv ^= pow2 * v[k, j - q - 1];
                        }
                        v[k, j] = newv;
                    }
                }
            }
            // scale column j by 2^(bits-1-j)
            for (int j = 0; j < SobolBits; j++)
                for (int k = 0; k < d; k++) v[k, j] <<= (SobolBits - 1 - j);

            var shift = new uint[d];
            if (rng != null)
                for (int k = 0; k < d; k++) shift[k] = (uint)rng.Next(1 << 15) << 15 | (uint)rng.Next(1 << 15);

            var m = Matrix(n, d);
            var x = new uint[d];
            double scale = 1.0 / (1u << SobolBits);
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < d; k++) m[i][k] = (x[k] ^ shift[k]) * scale;
                int c = 0;
                for (int t = i; (t & 1) != 0; t >>= 1) c++; // lowest zero bit of i (Gray code order)
                if (c >= SobolBits) throw new ArgumentException("Too many Sobol points.");
                for (int k = 0; k < d; k++) x[k] ^= v[k, c];
            }
            return m;
        }

        private static int BitLength(uint x)
        {
            int b = 0;
            while (x != 0) { b++; x >>= 1; }
            return b;
        }

        /// <summary>
        /// Halton sequence with one prime base per dimension. With a <paramref name="rng"/>, the digits are
        /// scrambled with a random permutation per dimension; with null, the plain sequence is returned
        /// (identical to SciPy's <c>qmc.Halton(d, scramble=False)</c>).
        /// </summary>
        public static double[][] Halton(int n, int d, Random rng)
        {
            int[] bases = Primes(d);
            var m = Matrix(n, d);
            for (int k = 0; k < d; k++)
            {
                int b = bases[k];
                int[] perm = rng != null ? Permutation(b, rng) : null;
                for (int i = 0; i < n; i++)
                {
                    double value = 0, f = 1.0 / b;
                    for (long idx = i; idx > 0; idx /= b, f /= b)
                    {
                        int digit = (int)(idx % b);
                        value += (perm != null ? perm[digit] : digit) * f;
                    }
                    // the infinitely many leading-zero digits map to perm[0]
                    if (perm != null) value += perm[0] * f * b / (b - 1);
                    m[i][k] = Math.Min(value, 1.0 - 1e-16);
                }
            }
            return m;
        }

        private static int[] Primes(int count)
        {
            var primes = new List<int>();
            for (int c = 2; primes.Count < count; c++)
            {
                bool prime = true;
                foreach (int p in primes)
                {
                    if (p * p > c) break;
                    if (c % p == 0) { prime = false; break; }
                }
                if (prime) primes.Add(c);
            }
            return primes.ToArray();
        }

        /// <summary>Full-factorial grid including 0 and 1 (a single level sits at 0.5). The last dimension varies fastest.</summary>
        public static double[][] Grid(int[] levels)
        {
            int d = levels.Length;
            long total = 1;
            foreach (int k in levels) total *= k;
            if (total > 1000000) throw new ArgumentException("The grid would have " + total + " runs; reduce Grid Levels.");
            var m = Matrix((int)total, d);
            for (int i = 0; i < total; i++)
            {
                long rest = i;
                for (int j = d - 1; j >= 0; j--)
                {
                    int k = levels[j];
                    int idx = (int)(rest % k);
                    rest /= k;
                    m[i][j] = k > 1 ? (double)idx / (k - 1) : 0.5;
                }
            }
            return m;
        }
    }
}
