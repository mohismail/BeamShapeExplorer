using System;
using System.Collections.Generic;
using System.Linq;

namespace BeamShapeExplorer.ShearMoment
{
    /// <summary>
    /// Short-term (immediate) deflection of a reinforced concrete beam under service loads, with cracking.
    ///
    /// The beam is analysed with a bending stiffness EI(x) = Ec Ieff(x), where Ieff is computed at each analysis
    /// section from its service moment and interpolated linearly between sections. Because the moments of a
    /// statically indeterminate beam depend on the stiffness, and Ieff depends on the moments, the analysis repeats
    /// until Ieff stops changing. Statically determinate beams converge in two passes.
    /// </summary>
    public class ServiceDeflectionResult
    {
        public BeamSolution Solution;
        public double[] SectionX;
        public double[] Moment;        // service moment, BSE convention (negative = sagging), kN-m
        public double[] Mcr;           // kN-m, positive
        public double[] Iu, Icr, Ieff; // m4
        public bool[] Cracked;
        public bool[] MissingTensionSteel;
        public int Iterations;
        public bool Converged;
        public double Ec;              // MPa

        /// <summary>Deflection at x (m), positive downward (in the direction of gravity).</summary>
        public double Deflection(double x) => -Solution.Displacement(x);

        /// <summary>Rotation at x (rad), counterclockwise positive.</summary>
        public double Rotation(double x) => Solution.Rotation(x);
    }

    public static class ServiceDeflection
    {
        /// <summary>For testing: when set, the largest relative change of Ieff in each iteration is added to it.</summary>
        public static List<double> History;

        public static ServiceDeflectionResult Analyze(double length, IList<SolverSupport> supports, IList<SolverLoad> loads,
                                                      IList<double> sectionX, IList<SectionMechanics> sections, double ecMPa,
                                                      EffectiveInertiaMethod method, int minElements = 400, int maxIterations = 200)
        {
            if (sections == null || sections.Count == 0) throw new ArgumentException("Add at least one section.");
            if (sectionX.Count != sections.Count) throw new ArgumentException("Each section needs a location.");
            if (!(ecMPa > 0)) throw new ArgumentException("The concrete E-modulus must be greater than zero.");

            // sort the sections along the span
            int[] order = Enumerable.Range(0, sections.Count).OrderBy(i => sectionX[i]).ToArray();
            double[] xs = order.Select(i => sectionX[i]).ToArray();
            SectionMechanics[] secs = order.Select(i => sections[i]).ToArray();
            int m = secs.Length;
            double ec = ecMPa * 1000; // kN/m2

            var ieff = secs.Select(s => s.Iu).ToArray(); // start uncracked
            Func<double, double> stiffness = x => ec * Interpolate(xs, ieff, x);

            BeamSolution sol = null;
            var moments = new double[m];
            int iter = 0;
            bool converged = false;
            while (iter < maxIterations)
            {
                iter++;
                double[] current = (double[])ieff.Clone();
                stiffness = x => ec * Interpolate(xs, current, x);
                sol = BeamSolution.Solve(length, supports, loads, stiffness, minElements);
                double change = 0;
                // under-relaxation after the first pass: cracking makes the plain fixed-point iteration of an
                // indeterminate beam oscillate between cracked and uncracked states
                double relax = iter == 1 ? 1.0 : 0.5;
                for (int i = 0; i < m; i++)
                {
                    moments[i] = sol.Moment(xs[i]);
                    double next = secs[i].EffectiveInertia(moments[i], method);
                    change = Math.Max(change, Math.Abs(next - current[i]) / secs[i].Iu);
                    ieff[i] = current[i] + relax * (next - current[i]);
                }
                if (History != null) History.Add(change);
                if (change < 1e-6) { converged = true; break; }
            }

            // put the results back in the input order
            var result = new ServiceDeflectionResult
            {
                Solution = sol,
                SectionX = new double[m], Moment = new double[m], Mcr = new double[m],
                Iu = new double[m], Icr = new double[m], Ieff = new double[m],
                Cracked = new bool[m], MissingTensionSteel = new bool[m],
                Iterations = iter, Converged = converged, Ec = ecMPa,
            };
            for (int k = 0; k < m; k++)
            {
                int i = order[k];
                SectionMechanics s = secs[k];
                double mo = moments[k];
                result.SectionX[i] = xs[k];
                result.Moment[i] = Math.Abs(mo) < 1e-9 ? 0 : mo;
                result.Mcr[i] = s.Mcr(mo);
                result.Iu[i] = s.Iu;
                result.Icr[i] = s.Icr(mo);
                result.Ieff[i] = ieff[k];
                result.Cracked[i] = Math.Abs(mo) > s.Mcr(mo);
                result.MissingTensionSteel[i] = result.Cracked[i] && !s.HasTensionSteel(mo);
            }
            return result;
        }

        /// <summary>Piecewise-linear interpolation of values at sorted stations; constant beyond the first and last station.</summary>
        public static double Interpolate(double[] xs, double[] values, double x)
        {
            if (xs.Length == 1 || x <= xs[0]) return values[0];
            if (x >= xs[xs.Length - 1]) return values[values.Length - 1];
            int i = Array.BinarySearch(xs, x);
            if (i >= 0) return values[i];
            i = ~i; // xs[i - 1] < x < xs[i]
            double t = (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
            return values[i - 1] + t * (values[i] - values[i - 1]);
        }
    }
}
