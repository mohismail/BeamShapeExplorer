using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

namespace BeamShapeExplorer.ShearMoment
{
    /// <summary>
    /// Calculates and plots the shear and bending moment along a beam from Beam Loads, Beam Supports and the
    /// Beam Builder's span curve. Vu and Mu at the analysis sections use the same convention as the other Beam Shape
    /// Explorer load envelopes, so they can go straight into Flexural Analysis and Shear Analysis.
    /// </summary>
    public class ShearMomentComponent : GH_Component
    {
        private static readonly Color ShearColor = Color.FromArgb(40, 100, 190);
        private static readonly Color MomentColor = Color.FromArgb(205, 80, 35);
        private static readonly Color LoadColor = Color.FromArgb(150, 20, 20);
        private static readonly Color SupportColor = Color.FromArgb(40, 40, 40);

        private sealed class Label
        {
            public string Text;
            public Point3d Point;
            public Color Color;
        }

        /// <summary>Everything drawn in the viewport for one solution.</summary>
        private sealed class Preview
        {
            public Mesh ShearFill, MomentFill;
            public Polyline ShearOutline, MomentOutline;
            public readonly List<Line> Baselines = new List<Line>();
            public readonly List<Line> LoadArrows = new List<Line>();
            public readonly List<Polyline> LoadProfiles = new List<Polyline>();
            public readonly List<Polyline> SupportGlyphs = new List<Polyline>();
            public readonly List<Line> SupportLines = new List<Line>();
            public readonly List<Circle> SupportCircles = new List<Circle>();
            public readonly List<Label> Labels = new List<Label>();

            public BoundingBox Box()
            {
                var box = BoundingBox.Empty;
                if (ShearFill != null) box.Union(ShearFill.GetBoundingBox(false));
                if (MomentFill != null) box.Union(MomentFill.GetBoundingBox(false));
                foreach (Line l in LoadArrows) box.Union(l.BoundingBox);
                foreach (Polyline p in SupportGlyphs) box.Union(p.BoundingBox);
                foreach (Polyline p in LoadProfiles) box.Union(p.BoundingBox);
                return box;
            }
        }

        private readonly List<Preview> _previews = new List<Preview>();

        public ShearMomentComponent()
          : base("Shear and Moment Diagram", "SMDiag",
              "Calculates and plots the shear and bending moment along a beam from Beam Loads, Beam Supports and a span " +
              "curve (e.g. the Beam Builder's Span Curve). Works for any number of fixed, pinned and roller supports, " +
              "including continuous and fixed-end beams. Vu and Mu at the analysis sections use the Beam Shape Explorer " +
              "load envelope convention, so they can be connected to Flexural Analysis and Shear Analysis.",
              "Beam Shape Explorer", "Loads")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Span Curve", "SpnCrv",
                "Curve representing the span of the beam, e.g. the Beam Builder's Span Curve. Load locations are measured " +
                "along it from its start.", GH_ParamAccess.item);
            pManager.AddGenericParameter("Loads", "Loads", "Loads from Beam Loads (several Beam Loads components can be connected).", GH_ParamAccess.list);
            pManager.AddGenericParameter("Supports", "Sup", "Supports from Beam Supports.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Number of Analysis Sections", "n",
                "Number of evenly spaced analysis sections along the span for the Vu and Mu outputs (the same as the " +
                "other load envelopes and the analysis components).", GH_ParamAccess.item, 10);
            pManager.AddNumberParameter("Shear Scale", "VScale",
                "(Optional) Drawing scale of the shear diagram (model units per kN). Default: automatic.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Moment Scale", "MScale",
                "(Optional) Drawing scale of the moment diagram (model units per kN-m). Default: automatic. Mu is drawn " +
                "along Up, so sagging (negative Mu) is drawn on the tension side, below the baseline; use a negative " +
                "scale to draw sagging upward instead.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Diagram Offset", "Off",
                "(Optional) Distance from the beam to the diagram baselines: the shear diagram is drawn above the beam and " +
                "the moment diagram below it. Use 0 to draw both on the beam. Default: 0.35 x span.", GH_ParamAccess.item);
            pManager.AddVectorParameter("Up Direction", "Up",
                "Direction opposite to gravity. Loads with a positive magnitude act against it, and the diagrams are drawn " +
                "in the plane of the span and this direction.", GH_ParamAccess.item, Vector3d.ZAxis);

            pManager[1].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Shear Envelope (kN)", "Vu",
                "Shear (kN) at the analysis sections: the sum of the upward forces to the left of each section. At a " +
                "support or point load, the side with the larger magnitude is given.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Moment Envelope (kN-m)", "Mu",
                "Bending moment (kN-m) at the analysis sections, Beam Shape Explorer convention: negative for sagging, " +
                "positive for hogging.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Section Locations (m)", "x", "Distance of each analysis section along the span.", GH_ParamAccess.list);
            pManager.AddPointParameter("Section Points", "Pts", "Analysis section points on the span curve.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Reactions (kN)", "R",
                "Vertical reaction of each support, in input order. Positive acts upward.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Reaction Moments (kN-m)", "RM",
                "Moment reaction of each support, in input order (0 unless fixed). Positive is counterclockwise, looking " +
                "with the span running left to right and Up pointing up.", GH_ParamAccess.list);
            pManager.AddCurveParameter("Shear Diagram", "VDiag", "Closed outline of the shear diagram.", GH_ParamAccess.item);
            pManager.AddCurveParameter("Moment Diagram", "MDiag", "Closed outline of the moment diagram.", GH_ParamAccess.item);
            pManager.AddMeshParameter("Diagram Fills", "Fill", "Filled shear and moment diagrams (for baking or rendering).", GH_ParamAccess.list);
            pManager.AddTextParameter("Summary", "S", "Reactions and maximum shear and moments.", GH_ParamAccess.list);

            // drawn by the component's own preview instead
            ((IGH_PreviewObject)pManager[3]).Hidden = true;
            ((IGH_PreviewObject)pManager[6]).Hidden = true;
            ((IGH_PreviewObject)pManager[7]).Hidden = true;
            ((IGH_PreviewObject)pManager[8]).Hidden = true;
        }

        protected override void BeforeSolveInstance()
        {
            _previews.Clear();
            base.BeforeSolveInstance();
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve span = null;
            var loadObjects = new List<object>();
            var supportObjects = new List<object>();
            int nSections = 10;
            double vScale = 0, mScale = 0, offset = double.NaN;
            Vector3d up = Vector3d.ZAxis;

            if (!DA.GetData(0, ref span) || span == null) return;
            DA.GetDataList(1, loadObjects);
            if (!DA.GetDataList(2, supportObjects)) return;
            DA.GetData(3, ref nSections);
            bool vGiven = DA.GetData(4, ref vScale);
            bool mGiven = DA.GetData(5, ref mScale);
            bool offGiven = DA.GetData(6, ref offset);
            DA.GetData(7, ref up);

            if (nSections < 2) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Number of Analysis Sections must be at least 2."); return; }
            double length = span.GetLength();
            if (!(length > Rhino.RhinoMath.ZeroTolerance)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The span curve has no length."); return; }
            if (!up.Unitize()) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Up Direction cannot be a zero vector."); return; }

            // ---- loads and supports from the generic inputs
            var loads = new List<BeamLoad>();
            foreach (object o in loadObjects)
            {
                BeamLoad l = Unwrap<BeamLoad>(o);
                if (l == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Loads must come from the Beam Loads component."); return; }
                loads.Add(l);
            }
            var supports = new List<BeamSupport>();
            foreach (object o in supportObjects)
            {
                BeamSupport s = Unwrap<BeamSupport>(o);
                if (s == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Supports must come from the Beam Supports component."); return; }
                supports.Add(s);
            }
            if (loads.Count == 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No loads connected: shear and moment are zero.");

            // ---- project the supports onto the span curve
            var solverSupports = new List<SolverSupport>();
            double tolerance = DocumentTolerance();
            for (int i = 0; i < supports.Count; i++)
            {
                if (!span.ClosestPoint(supports[i].Point, out double t)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not project a support onto the span curve."); return; }
                double x = span.GetLength(new Interval(span.Domain.Min, t));
                double distance = supports[i].Point.DistanceTo(span.PointAt(t));
                if (distance > Math.Max(tolerance, 0.01 * length))
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Support " + (i + 1) + " is " + Num(distance) +
                                      " from the span curve; it is projected onto the curve at " + Num(x) + ".");
                solverSupports.Add(new SolverSupport(x, supports[i].Type));
            }
            List<SolverLoad> solverLoads = loads.Select(l => l.Resolve(length)).ToList();

            BeamSolution sol;
            try { sol = BeamSolution.Solve(length, solverSupports, solverLoads); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }

            // ---- analysis sections (evenly spaced by length, like the other load envelopes)
            var xs = new List<double>();
            var vu = new List<double>();
            var mu = new List<double>();
            var sectionPts = new List<Point3d>();
            for (int i = 0; i < nSections; i++)
            {
                double x = length * i / (nSections - 1);
                xs.Add(x);
                double vLeft = sol.Shear(x, -1), vRight = sol.Shear(x, 1);
                double v = i == 0 ? vRight : i == nSections - 1 ? vLeft : (Math.Abs(vLeft) > Math.Abs(vRight) ? vLeft : vRight);
                vu.Add(Clean(v));
                mu.Add(Clean(sol.Moment(x, i == nSections - 1 ? -1 : 1)));
                sectionPts.Add(PointAtLength(span, x));
            }

            // ---- diagrams
            List<KeyValuePair<double, int>> stations = sol.PlotStations(Math.Max(201, 4 * nSections));
            var sx = stations.Select(s => s.Key).ToList();
            var sv = stations.Select(s => Clean(sol.Shear(s.Key, s.Value))).ToList();
            var sm = stations.Select(s => Clean(sol.Moment(s.Key, s.Value))).ToList();
            double maxV = sv.Max(Math.Abs), maxM = sm.Max(Math.Abs);
            if (!vGiven || vScale == 0) vScale = maxV > 0 ? 0.12 * length / maxV : 0;
            if (!mGiven || mScale == 0) mScale = maxM > 0 ? 0.12 * length / maxM : 0;
            if (!offGiven) offset = 0.35 * length;

            var basePts = new List<Point3d>();
            var normals = new List<Vector3d>();
            var tangents = new List<Vector3d>();
            foreach (double x in sx)
            {
                if (!Frame(span, x, up, out Point3d p, out Vector3d tan, out Vector3d nrm))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Up Direction is parallel to the span curve; choose a direction across the span.");
                    return;
                }
                basePts.Add(p); tangents.Add(tan); normals.Add(nrm);
            }

            var preview = new Preview();
            preview.ShearOutline = Diagram(basePts, normals, sv, offset, vScale, out preview.ShearFill, out Line vBase);
            preview.MomentOutline = Diagram(basePts, normals, sm, -offset, mScale, out preview.MomentFill, out Line mBase);
            if (Math.Abs(offset) > 0) { preview.Baselines.Add(vBase); preview.Baselines.Add(mBase); }

            // labels at the extremes
            int iVmax = IndexOfMax(sv, v => v), iVmin = IndexOfMax(sv, v => -v);
            int iMhog = IndexOfMax(sm, m => m), iMsag = IndexOfMax(sm, m => -m);
            AddExtremeLabel(preview, sv, iVmax, basePts, normals, offset, vScale, "V = ", " kN", ShearColor, maxV);
            if (iVmin != iVmax) AddExtremeLabel(preview, sv, iVmin, basePts, normals, offset, vScale, "V = ", " kN", ShearColor, maxV);
            AddExtremeLabel(preview, sm, iMsag, basePts, normals, -offset, mScale, "M = ", " kN-m", MomentColor, maxM);
            if (iMhog != iMsag) AddExtremeLabel(preview, sm, iMhog, basePts, normals, -offset, mScale, "M = ", " kN-m", MomentColor, maxM);
            if (Math.Abs(offset) > 0)
            {
                preview.Labels.Add(new Label { Text = "Shear (kN)", Point = basePts[0] + normals[0] * offset - tangents[0] * 0.04 * length, Color = ShearColor });
                preview.Labels.Add(new Label { Text = "Moment (kN-m)", Point = basePts[0] - normals[0] * offset - tangents[0] * 0.04 * length, Color = MomentColor });
            }

            DrawLoads(preview, sol, span, up, length);
            DrawSupports(preview, sol, span, up, length);
            _previews.Add(preview);

            // ---- summary
            var summary = new List<string> { "Span length: " + Num(length) + " m" };
            for (int i = 0; i < supports.Count; i++)
            {
                string line = "Support " + (i + 1) + " (" + supports[i].Type.ToString().ToLowerInvariant() + " at " +
                              Num(sol.Supports[i].X) + " m): R = " + Num(Clean(sol.Reactions[i])) + " kN";
                if (supports[i].Type == SupportType.Fixed) line += ", M = " + Num(Clean(sol.ReactionMoments[i])) + " kN-m";
                summary.Add(line);
            }
            summary.Add("Max shear: " + Num(sv[iVmax]) + " kN at " + Num(sx[iVmax]) + " m; min shear: " + Num(sv[iVmin]) + " kN at " + Num(sx[iVmin]) + " m");
            summary.Add("Max sagging moment: " + Num(-sm[iMsag]) + " kN-m at " + Num(sx[iMsag]) + " m (Mu = " + Num(sm[iMsag]) + ")");
            summary.Add("Max hogging moment: " + Num(sm[iMhog]) + " kN-m at " + Num(sx[iMhog]) + " m (Mu = " + Num(sm[iMhog]) + ")");
            if (supports.Count > 2 || supports.Count(s => s.Type == SupportType.Fixed) > 0 && supports.Count > 1)
                summary.Add("Statically indeterminate beam: reactions assume a uniform bending stiffness (EI) along the span.");
            Message = supports.Count + " support(s), " + loads.Count + " load(s)";

            var fills = new List<Mesh>();
            if (preview.ShearFill != null) fills.Add(preview.ShearFill);
            if (preview.MomentFill != null) fills.Add(preview.MomentFill);

            DA.SetDataList(0, vu);
            DA.SetDataList(1, mu);
            DA.SetDataList(2, xs);
            DA.SetDataList(3, sectionPts);
            DA.SetDataList(4, sol.Reactions.Select(Clean));
            DA.SetDataList(5, sol.ReactionMoments.Select(Clean));
            DA.SetData(6, preview.ShearOutline.ToNurbsCurve());
            DA.SetData(7, preview.MomentOutline.ToNurbsCurve());
            DA.SetDataList(8, fills);
            DA.SetDataList(9, summary);
        }

        // ================================================================== geometry helpers

        private static T Unwrap<T>(object o) where T : class
        {
            if (o is T t) return t;
            if (o is Grasshopper.Kernel.Types.GH_ObjectWrapper w) return w.Value as T;
            if (o is Grasshopper.Kernel.Types.IGH_Goo goo) return goo.ScriptVariable() as T;
            return null;
        }

        private static double Clean(double v) => Math.Abs(v) < 1e-9 ? 0.0 : v;

        private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static Point3d PointAtLength(Curve c, double s)
        {
            if (!c.LengthParameter(Math.Max(0, Math.Min(c.GetLength(), s)), out double t)) t = s <= 0 ? c.Domain.Min : c.Domain.Max;
            return c.PointAt(t);
        }

        /// <summary>Point, tangent and in-plane "up" normal at arc length s.</summary>
        private static bool Frame(Curve c, double s, Vector3d up, out Point3d p, out Vector3d tangent, out Vector3d normal)
        {
            if (!c.LengthParameter(Math.Max(0, Math.Min(c.GetLength(), s)), out double t)) t = s <= 0 ? c.Domain.Min : c.Domain.Max;
            p = c.PointAt(t);
            tangent = c.TangentAt(t);
            normal = up - (up * tangent) * tangent;
            return normal.Unitize();
        }

        /// <summary>Builds the closed outline and fill mesh of one diagram, drawn from a baseline offset from the beam.</summary>
        private static Polyline Diagram(List<Point3d> basePts, List<Vector3d> normals, List<double> values, double offset,
                                        double scale, out Mesh fill, out Line baseline)
        {
            var bases = basePts.Select((p, i) => p + normals[i] * offset).ToList();
            var tops = bases.Select((p, i) => p + normals[i] * values[i] * scale).ToList();
            baseline = new Line(bases[0], bases[bases.Count - 1]);

            var outline = new Polyline(tops);
            for (int i = bases.Count - 1; i >= 0; i--) outline.Add(bases[i]);
            outline.Add(tops[0]);

            fill = new Mesh();
            for (int i = 0; i < bases.Count - 1; i++)
            {
                double a = values[i], b = values[i + 1];
                if (a * b < 0)
                {
                    // split at the zero crossing so the fill does not cross itself
                    double f = a / (a - b);
                    Point3d z = bases[i] + (bases[i + 1] - bases[i]) * f;
                    AddTriangle(fill, bases[i], tops[i], z);
                    AddTriangle(fill, z, tops[i + 1], bases[i + 1]);
                }
                else if (Math.Abs(a) + Math.Abs(b) > 0)
                {
                    int v0 = fill.Vertices.Add(bases[i]), v1 = fill.Vertices.Add(bases[i + 1]);
                    int v2 = fill.Vertices.Add(tops[i + 1]), v3 = fill.Vertices.Add(tops[i]);
                    fill.Faces.AddFace(v0, v1, v2, v3);
                }
            }
            if (fill.Faces.Count == 0) fill = null;
            else { fill.Compact(); fill.Normals.ComputeNormals(); }
            return outline;
        }

        private static void AddTriangle(Mesh m, Point3d a, Point3d b, Point3d c)
        {
            int i = m.Vertices.Add(a), j = m.Vertices.Add(b), k = m.Vertices.Add(c);
            m.Faces.AddFace(i, j, k);
        }

        private static int IndexOfMax(List<double> values, Func<double, double> key)
        {
            int best = 0;
            for (int i = 1; i < values.Count; i++) if (key(values[i]) > key(values[best])) best = i;
            return best;
        }

        private static void AddExtremeLabel(Preview pv, List<double> values, int i, List<Point3d> basePts, List<Vector3d> normals,
                                            double offset, double scale, string prefix, string unit, Color color, double maxAbs)
        {
            if (maxAbs <= 0 || Math.Abs(values[i]) < 1e-9) return;
            double push = (values[i] >= 0 ? 1 : -1) * 0.035 * basePts[0].DistanceTo(basePts[basePts.Count - 1]);
            Point3d p = basePts[i] + normals[i] * (offset + values[i] * scale + push);
            pv.Labels.Add(new Label { Text = prefix + Num(values[i]) + unit, Point = p, Color = color });
        }

        private static void DrawLoads(Preview pv, BeamSolution sol, Curve span, Vector3d up, double length)
        {
            double arrow = 0.14 * length;
            double maxW = sol.Loads.Where(l => l.Type == LoadType.Distributed)
                                   .Select(l => Math.Max(Math.Abs(l.Wa), Math.Abs(l.Wb))).DefaultIfEmpty(0).Max();
            foreach (SolverLoad l in sol.Loads)
            {
                if (l.Type == LoadType.Point)
                {
                    if (!Frame(span, l.A, up, out Point3d p, out _, out Vector3d n)) continue;
                    Point3d tail = p + n * arrow;
                    pv.LoadArrows.Add(l.Wa >= 0 ? new Line(tail, p) : new Line(p, tail));
                    pv.Labels.Add(new Label { Text = "P = " + Num(l.Wa) + " kN", Point = tail + n * 0.02 * length, Color = LoadColor });
                    continue;
                }
                double h = 0.06 * length;
                var top = new Polyline();
                int steps = Math.Max(2, (int)Math.Round((l.B - l.A) / length * 40));
                for (int k = 0; k <= steps; k++)
                {
                    double x = l.A + (l.B - l.A) * k / steps;
                    double w = l.Wa + (l.Wb - l.Wa) * k / steps;
                    if (!Frame(span, x, up, out Point3d p, out _, out Vector3d n)) continue;
                    top.Add(p + n * (maxW > 0 ? h * Math.Abs(w) / maxW : 0) + n * 0.01 * length);
                }
                pv.LoadProfiles.Add(top);
                int arrows = Math.Max(2, (int)Math.Round((l.B - l.A) / length * 12) + 1);
                for (int k = 0; k < arrows; k++)
                {
                    double f = (double)k / (arrows - 1);
                    double x = l.A + (l.B - l.A) * f, w = l.Wa + (l.Wb - l.Wa) * f;
                    if (Math.Abs(w) < 1e-12 || !Frame(span, x, up, out Point3d p, out _, out Vector3d n)) continue;
                    Point3d tail = p + n * (h * Math.Abs(w) / maxW + 0.01 * length);
                    pv.LoadArrows.Add(w >= 0 ? new Line(tail, p) : new Line(p, tail));
                }
                if (Frame(span, 0.5 * (l.A + l.B), up, out Point3d mid, out _, out Vector3d nm))
                {
                    string text = Math.Abs(l.Wa - l.Wb) < 1e-12 ? "w = " + Num(l.Wa) + " kN/m" : "w = " + Num(l.Wa) + " to " + Num(l.Wb) + " kN/m";
                    pv.Labels.Add(new Label { Text = text, Point = mid + nm * (h + 0.03 * length), Color = LoadColor });
                }
            }
        }

        private static void DrawSupports(Preview pv, BeamSolution sol, Curve span, Vector3d up, double length)
        {
            double g = 0.035 * length;
            for (int i = 0; i < sol.Supports.Count; i++)
            {
                SolverSupport s = sol.Supports[i];
                if (!Frame(span, s.X, up, out Point3d p, out Vector3d t, out Vector3d n)) continue;
                string reaction = "R = " + Num(Clean(sol.Reactions[i])) + " kN";
                if (s.Type == SupportType.Fixed)
                {
                    // wall across the beam, hatched on the outside
                    Vector3d outside = s.X < 0.5 * length ? -t : t;
                    pv.SupportLines.Add(new Line(p - n * 1.3 * g, p + n * 1.3 * g));
                    for (int k = 0; k < 5; k++)
                    {
                        Point3d a = p + n * g * (-1.2 + 0.6 * k);
                        pv.SupportLines.Add(new Line(a, a + outside * 0.5 * g - n * 0.5 * g));
                    }
                    reaction += ", M = " + Num(Clean(sol.ReactionMoments[i])) + " kN-m";
                }
                else
                {
                    var tri = new Polyline { p, p - n * g + t * 0.65 * g, p - n * g - t * 0.65 * g, p };
                    pv.SupportGlyphs.Add(tri);
                    if (s.Type == SupportType.Pinned)
                    {
                        Point3d b = p - n * g;
                        pv.SupportLines.Add(new Line(b - t * g, b + t * g));
                    }
                    else
                    {
                        double r = 0.18 * g;
                        Vector3d axis = Vector3d.CrossProduct(t, n);
                        foreach (double side in new[] { -0.35, 0.35 })
                        {
                            Point3d c = p - n * (g + r) + t * side * g;
                            pv.SupportCircles.Add(new Circle(new Plane(c, axis), r));
                        }
                        Point3d b = p - n * (g + 2 * r);
                        pv.SupportLines.Add(new Line(b - t * g, b + t * g));
                    }
                }
                pv.Labels.Add(new Label { Text = reaction, Point = p - n * 2.4 * g, Color = SupportColor });
            }
        }

        // ================================================================== preview

        public override BoundingBox ClippingBox
        {
            get
            {
                BoundingBox box = base.ClippingBox;
                foreach (Preview p in _previews) box.Union(p.Box());
                return box;
            }
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportMeshes(args);
            if (Hidden || Locked) return;
            var shear = new DisplayMaterial(ShearColor, 0.65);
            var moment = new DisplayMaterial(MomentColor, 0.65);
            foreach (Preview p in _previews)
            {
                if (p.ShearFill != null) args.Display.DrawMeshShaded(p.ShearFill, shear);
                if (p.MomentFill != null) args.Display.DrawMeshShaded(p.MomentFill, moment);
            }
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            if (Hidden || Locked) return;
            int thick = Attributes.Selected ? 3 : 2;
            foreach (Preview p in _previews)
            {
                if (p.ShearOutline != null) args.Display.DrawPolyline(p.ShearOutline, ShearColor, thick);
                if (p.MomentOutline != null) args.Display.DrawPolyline(p.MomentOutline, MomentColor, thick);
                foreach (Line l in p.Baselines) args.Display.DrawLine(l, Color.Gray, 1);
                foreach (Line l in p.LoadArrows) args.Display.DrawArrow(l, LoadColor);
                foreach (Polyline pl in p.LoadProfiles) args.Display.DrawPolyline(pl, LoadColor, 1);
                foreach (Polyline pl in p.SupportGlyphs) args.Display.DrawPolyline(pl, SupportColor, thick);
                foreach (Line l in p.SupportLines) args.Display.DrawLine(l, SupportColor, thick);
                foreach (Circle c in p.SupportCircles) args.Display.DrawCircle(c, SupportColor, thick);
                foreach (Label lb in p.Labels) args.Display.Draw2dText(lb.Text, lb.Color, lb.Point, true, 12);
            }
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.smdiagram;

        public override Guid ComponentGuid => new Guid("1ed1e4cc-f5aa-4fbe-8cf2-164e277d83ef");
    }
}
