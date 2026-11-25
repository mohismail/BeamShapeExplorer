using System;
using System.Collections.Generic;
using System.Linq;

namespace BeamShapeExplorer.ShearMoment
{
    /// <summary>A lump of reinforcement in a section: its area (m2) and the height v (m) of its centroid.</summary>
    public struct SteelLump
    {
        public double Area, V;

        public SteelLump(double area, double v) { Area = area; V = v; }
    }

    /// <summary>Effective moment of inertia formulas.</summary>
    public enum EffectiveInertiaMethod
    {
        /// <summary>Branson: Ie = (Mcr/Ma)^3 Iu + [1 - (Mcr/Ma)^3] Icr.</summary>
        Branson = 0,
        /// <summary>Bischoff (sectional form): Ie = Icr / [1 - (Mcr/Ma)^2 (1 - Icr/Iu)].</summary>
        Bischoff = 1,
    }

    /// <summary>
    /// Elastic section properties of a reinforced concrete section of any shape, for serviceability. The concrete
    /// outline is a polygon in section coordinates (u across the width, v upward); reinforcement is given as lumps.
    ///
    /// For each bending direction (sagging = tension at the bottom, hogging = tension at the top) it gives:
    ///   * the uncracked transformed moment of inertia Iu (steel counted as (n - 1) As, about the transformed centroid)
    ///   * the cracking moment Mcr = fr Ig / yt (gross concrete section, as in IS 456 and ACI 318)
    ///   * the cracked moment of inertia Icr about the elastic cracked neutral axis, found from the actual shape of
    ///     the compression zone (tension steel counted as n As, compression steel as (n - 1) As, tension concrete ignored)
    /// Units: lengths in m, fr in MPa, moments in kN-m.
    /// </summary>
    public class SectionMechanics
    {
        public double Area { get; private set; }          // gross concrete area (m2)
        public double Centroid { get; private set; }      // gross centroid height v (m)
        public double Ig { get; private set; }            // gross moment of inertia about the centroid (m4)
        public double VMin { get; private set; }
        public double VMax { get; private set; }
        public double Depth => VMax - VMin;
        public double SteelArea { get; private set; }

        public double Iu { get; private set; }            // uncracked transformed (m4)
        public double TransformedCentroid { get; private set; }

        public double McrSagging { get; private set; }    // kN-m, positive
        public double McrHogging { get; private set; }
        public double IcrSagging { get; private set; }    // m4
        public double IcrHogging { get; private set; }
        /// <summary>Depth of the cracked neutral axis below the top fiber (sagging) or above the bottom fiber (hogging).</summary>
        public double NeutralAxisSagging { get; private set; }
        public double NeutralAxisHogging { get; private set; }
        public bool HasTensionSteelSagging { get; private set; }
        public bool HasTensionSteelHogging { get; private set; }

        public static SectionMechanics Compute(IList<double[]> outline, IList<SteelLump> steel, double modularRatio, double frMPa)
        {
            if (outline == null || outline.Count < 3) throw new ArgumentException("A concrete section outline needs at least 3 points.");
            if (!(modularRatio > 0)) throw new ArgumentException("The modular ratio Es/Ec must be greater than zero.");
            List<double[]> poly = outline.Select(p => new[] { p[0], p[1] }).ToList();
            if (SignedArea(poly) < 0) poly.Reverse();
            var bars = (steel ?? new List<SteelLump>()).Where(s => s.Area > 0).ToList();

            Integrals(poly, out double a, out double qv, out double ivv);
            if (!(a > 0)) throw new ArgumentException("The concrete section has no area.");
            var sm = new SectionMechanics
            {
                Area = a,
                Centroid = qv / a,
                VMin = poly.Min(p => p[1]),
                VMax = poly.Max(p => p[1]),
                SteelArea = bars.Sum(b => b.Area),
            };
            sm.Ig = ivv - a * sm.Centroid * sm.Centroid;

            // uncracked transformed section
            double n1 = modularRatio - 1;
            double at = a + n1 * sm.SteelArea;
            double vt = (qv + n1 * bars.Sum(b => b.Area * b.V)) / at;
            sm.TransformedCentroid = vt;
            sm.Iu = sm.Ig + a * Math.Pow(sm.Centroid - vt, 2) + n1 * bars.Sum(b => b.Area * Math.Pow(b.V - vt, 2));

            // cracking moments from the gross section: fr (MPa) * Ig / yt * 1000 -> kN-m
            sm.McrSagging = 1000 * frMPa * sm.Ig / (sm.Centroid - sm.VMin);
            sm.McrHogging = 1000 * frMPa * sm.Ig / (sm.VMax - sm.Centroid);

            // cracked sections: sagging = compression at the top; hogging = mirror the section and do the same
            Cracked(poly, bars, modularRatio, out double icr, out double na, out bool tension);
            sm.IcrSagging = icr; sm.NeutralAxisSagging = sm.VMax - na; sm.HasTensionSteelSagging = tension;
            var mirrored = poly.Select(p => new[] { p[0], -p[1] }).Reverse().ToList();
            var mirroredBars = bars.Select(b => new SteelLump(b.Area, -b.V)).ToList();
            Cracked(mirrored, mirroredBars, modularRatio, out icr, out na, out tension);
            sm.IcrHogging = icr; sm.NeutralAxisHogging = -sm.VMin - na; sm.HasTensionSteelHogging = tension;
            return sm;
        }

        /// <summary>Cracking moment (kN-m, positive) for a moment of the given sign (BSE convention: negative = sagging).</summary>
        public double Mcr(double moment) => moment > 0 ? McrHogging : McrSagging;

        /// <summary>Cracked moment of inertia for a moment of the given sign (BSE convention: negative = sagging).</summary>
        public double Icr(double moment) => moment > 0 ? IcrHogging : IcrSagging;

        public bool HasTensionSteel(double moment) => moment > 0 ? HasTensionSteelHogging : HasTensionSteelSagging;

        /// <summary>Effective moment of inertia (m4) for a service moment Ma (kN-m, either sign). Never more than Iu.
        /// A cracked section without tension steel gets Iu (Mcr/Ma)^3, since it has no cracked stiffness.</summary>
        public double EffectiveInertia(double moment, EffectiveInertiaMethod method)
        {
            double ma = Math.Abs(moment), mcr = Mcr(moment), icr = Icr(moment);
            if (ma <= mcr || ma <= 0) return Iu;
            double r = mcr / ma;
            double ie;
            if (!HasTensionSteel(moment))
                ie = r * r * r * Iu; // no tension steel: no cracked stiffness to fall back on (reported as a warning)
            else if (method == EffectiveInertiaMethod.Branson)
                ie = r * r * r * Iu + (1 - r * r * r) * icr;
            else
                ie = icr / (1 - r * r * (1 - icr / Iu));
            return Math.Min(Iu, Math.Max(ie, 1e-12 * Iu));
        }

        // ================================================================== geometry

        private static double SignedArea(List<double[]> p)
        {
            double s = 0;
            for (int i = 0; i < p.Count; i++)
            {
                double[] a = p[i], b = p[(i + 1) % p.Count];
                s += a[0] * b[1] - b[0] * a[1];
            }
            return 0.5 * s;
        }

        /// <summary>Area, first moment about v = 0, and second moment about v = 0 of a polygon (Green's theorem).</summary>
        private static void Integrals(List<double[]> p, out double area, out double qv, out double ivv)
        {
            area = qv = ivv = 0;
            for (int i = 0; i < p.Count; i++)
            {
                double[] a = p[i], b = p[(i + 1) % p.Count];
                double cross = a[0] * b[1] - b[0] * a[1];
                area += cross;
                qv += (a[1] + b[1]) * cross;
                ivv += (a[1] * a[1] + a[1] * b[1] + b[1] * b[1]) * cross;
            }
            area *= 0.5;
            qv /= 6.0;
            ivv /= 12.0;
        }

        /// <summary>Part of a polygon above the line v = c (Sutherland-Hodgman; exact for the area integrals of any simple polygon).</summary>
        private static List<double[]> ClipAbove(List<double[]> p, double c)
        {
            var result = new List<double[]>();
            for (int i = 0; i < p.Count; i++)
            {
                double[] a = p[i], b = p[(i + 1) % p.Count];
                bool ina = a[1] >= c, inb = b[1] >= c;
                if (ina) result.Add(a);
                if (ina != inb)
                {
                    double t = (c - a[1]) / (b[1] - a[1]);
                    result.Add(new[] { a[0] + t * (b[0] - a[0]), c });
                }
            }
            return result;
        }

        /// <summary>
        /// Cracked section with compression at the top: finds the neutral axis height c where the first moment of the
        /// transformed cracked section is zero, and returns Icr about it.
        /// </summary>
        private static void Cracked(List<double[]> poly, List<SteelLump> bars, double n, out double icr, out double neutralAxis, out bool hasTensionSteel)
        {
            double vmin = poly.Min(p => p[1]), vmax = poly.Max(p => p[1]);
            Func<double, double> firstMoment = c =>
            {
                var comp = ClipAbove(poly, c);
                double q = 0;
                if (comp.Count >= 3)
                {
                    Integrals(comp, out double ac, out double qc, out double _);
                    q = qc - c * ac; // integral of (v - c) dA over the compression zone
                }
                foreach (SteelLump b in bars)
                    q += (b.V > c ? n - 1 : n) * b.Area * (b.V - c);
                return q;
            };
            // tension reinforcement = steel in the tension half of the depth (steel near the compression face cannot
            // make a cracked section work). firstMoment decreases as c rises: positive at the bottom fiber, negative at the top.
            hasTensionSteel = bars.Any(b => b.V < 0.5 * (vmin + vmax));
            double lo = vmin, hi = vmax;
            if (!hasTensionSteel)
            {
                // no tension reinforcement: a cracked section cannot carry moment; report the neutral axis at the tension face
                lo = hi = vmin;
            }
            else
            {
                for (int it = 0; it < 200 && hi - lo > 1e-12 * Math.Max(1, vmax - vmin); it++)
                {
                    double mid = 0.5 * (lo + hi);
                    if (firstMoment(mid) > 0) lo = mid; else hi = mid;
                }
            }
            double na = 0.5 * (lo + hi);
            neutralAxis = na;
            double i = 0;
            var zone = ClipAbove(poly, na);
            if (zone.Count >= 3)
            {
                Integrals(zone, out double ac, out double qc, out double ic);
                i = ic - 2 * na * qc + na * na * ac; // integral of (v - na)^2 dA
            }
            foreach (SteelLump b in bars)
                i += (b.V > na ? n - 1 : n) * b.Area * Math.Pow(b.V - na, 2);
            icr = hasTensionSteel ? i : 0.0;
        }
    }
}
