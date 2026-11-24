using System;
using System.Globalization;

using Rhino.Geometry;

namespace BeamShapeExplorer.ShearMoment
{
    /// <summary>
    /// A load defined by the Beam Loads component. Locations are measured along the span curve from its start
    /// (in model units, or as fractions of the span when <see cref="Relative"/> is true). Positive magnitudes act
    /// downward (against the diagram component's Up direction).
    /// </summary>
    public class BeamLoad
    {
        public LoadType Type;
        /// <summary>Point load (kN), or distributed intensity at the start (kN/m).</summary>
        public double Magnitude;
        /// <summary>Distributed intensity at the end (kN/m); equals <see cref="Magnitude"/> for a uniform load.</summary>
        public double EndMagnitude;
        /// <summary>Point load position, or start of a distributed load.</summary>
        public double Location;
        /// <summary>End of a distributed load; NaN means the end of the span.</summary>
        public double EndLocation = double.NaN;
        public bool Relative;

        /// <summary>The load in absolute coordinates for a span of the given length.</summary>
        public SolverLoad Resolve(double spanLength)
        {
            double f = Relative ? spanLength : 1.0;
            double a = Location * f;
            if (Type == LoadType.Point) return SolverLoad.Point(a, Magnitude);
            double b = double.IsNaN(EndLocation) ? spanLength : EndLocation * f;
            return SolverLoad.Distributed(a, b, Magnitude, EndMagnitude);
        }

        public static LoadType? ParseType(string text)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "point": case "p": case "concentrated": case "pt": return LoadType.Point;
                case "distributed": case "d": case "udl": case "line": case "uniform": case "dist": case "linear":
                case "trapezoidal": case "triangular": case "w": case "q": return LoadType.Distributed;
                default: return null;
            }
        }

        private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        public override string ToString()
        {
            string unit = Relative ? " (of span)" : " m";
            if (Type == LoadType.Point)
                return "Point load " + Num(Magnitude) + " kN at " + Num(Location) + unit;
            string end = double.IsNaN(EndLocation) ? "end of span" : Num(EndLocation) + unit;
            string w = Math.Abs(EndMagnitude - Magnitude) < 1e-12
                ? Num(Magnitude) + " kN/m"
                : Num(Magnitude) + " to " + Num(EndMagnitude) + " kN/m";
            return "Distributed load " + w + " from " + Num(Location) + unit + " to " + end;
        }
    }

    /// <summary>A support defined by the Beam Supports component: a point (projected onto the span curve) and a type.</summary>
    public class BeamSupport
    {
        public Point3d Point;
        public SupportType Type;

        public static SupportType? ParseType(string text)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "fixed": case "f": case "clamped": case "built-in": case "encastre": return SupportType.Fixed;
                case "pinned": case "pin": case "p": case "hinge": case "hinged": return SupportType.Pinned;
                case "roller": case "rolling": case "r": case "roll": case "slider": return SupportType.Roller;
                default: return null;
            }
        }

        public override string ToString()
        {
            return Type + " support at (" + Point.X.ToString("0.###", CultureInfo.InvariantCulture) + ", " +
                   Point.Y.ToString("0.###", CultureInfo.InvariantCulture) + ", " +
                   Point.Z.ToString("0.###", CultureInfo.InvariantCulture) + ")";
        }
    }
}
