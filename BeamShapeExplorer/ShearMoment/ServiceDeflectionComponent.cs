using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Grasshopper.Kernel;
using Rhino.Geometry;

using BeamShapeExplorer.DataTypes;

namespace BeamShapeExplorer.ShearMoment
{
    /// <summary>
    /// Effective moment of inertia, rotation and deflection of a shaped reinforced concrete beam under service loads.
    /// Combines the gross (Iu), cracking moment (Mcr), cracked (Icr) and effective (Ieff) moment of inertia
    /// calculations with a deflection analysis that works for any supports.
    /// </summary>
    public class ServiceDeflectionComponent : GH_Component
    {
        public ServiceDeflectionComponent()
          : base("Service Deflection", "SDefl",
              "Effective moment of inertia, rotation and deflection of a shaped reinforced concrete beam under service " +
              "loads. For each section it computes the uncracked (Iu) and cracked (Icr) moments of inertia from the actual " +
              "section shape, the cracking moment (Mcr) and the effective moment of inertia (Ieff), then solves the beam " +
              "with the resulting stiffness. The span, supports and loads come from Shear and Moment Diagram (Beam Model " +
              "output, with unfactored service loads). Short-term (immediate) deflection only.",
              "Beam Shape Explorer", "Analysis")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Material Properties", "MP", "Properties for steel and concrete materials", GH_ParamAccess.item);
            pManager.AddCurveParameter("Concrete Section", "Ag",
                "Concrete sections along the beam, e.g. from Beam Sections (planar closed curves; any shape).", GH_ParamAccess.list);
            pManager.AddCurveParameter("Steel Section", "As",
                "Steel reinforcement section for each concrete section (same order), e.g. from Beam Sections. A section " +
                "without steel can have a degenerate curve.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Beam Model", "Beam",
                "The Beam Model output of a Shear and Moment Diagram component whose loads are the unfactored service loads " +
                "(Beam Loads, and Load Envelope from Selfweight with Load Factor 1.0). It provides the span curve, the " +
                "supports and the loads; each section is located by projecting it onto the span curve.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Effective Moment of Inertia Method", "Method",
                "0 = Branson: Ie = (Mcr/Ma)^3 Iu + [1 - (Mcr/Ma)^3] Icr. 1 = Bischoff: Ie = Icr / [1 - (Mcr/Ma)^2 (1 - Icr/Iu)] " +
                "(default; more realistic for lightly reinforced sections).", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("Modulus of Rupture (MPa)", "fr",
                "(Optional) Flexural tensile strength of the concrete. Default from the building code setting: " +
                "0.7 sqrt(f'c) for IS 456, 0.62 sqrt(f'c) for ACI 318.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Deflection Limit (span/x)", "Limit",
                "Allowable deflection as a fraction of the span, e.g. 250 for span/250 (IS 456) or 360 for span/360 (ACI 318, " +
                "live load on floors).", GH_ParamAccess.item, 250);
            pManager.AddNumberParameter("Deflection Scale", "Scale",
                "(Optional) Exaggeration of the deflected shape. Default: automatic (the largest deflection drawn as 10% of the span).",
                GH_ParamAccess.item);

            pManager[5].Optional = true;
            pManager[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Gross Moment of Inertia (m\x2074)", "Iu",
                "Uncracked transformed moment of inertia of each section (steel counted as (n - 1) As).", GH_ParamAccess.list);
            pManager.AddNumberParameter("Cracked Moment of Inertia (m\x2074)", "Icr",
                "Cracked moment of inertia of each section, about its elastic cracked neutral axis, for the sign of its service moment.",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("Cracking Moment (kN-m)", "Mcr", "Cracking moment of each section, for the sign of its service moment.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Effective Moment of Inertia (m\x2074)", "Ieff", "Effective moment of inertia of each section under the service loads.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Service Moment (kN-m)", "Ma",
                "Service moment at each section (negative = sagging, positive = hogging, as Mu elsewhere in Beam Shape Explorer).",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("Rotation (rad)", "ϴ", "Rotation at each section (counterclockwise positive, span running left to right).", GH_ParamAccess.list);
            pManager.AddNumberParameter("Deflection (m)", "Δ", "Deflection at each section, positive downward.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Section Locations (m)", "x", "Distance of each section along the span curve.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Maximum Deflection (m)", "Δmax", "Largest deflection along the beam, positive downward.", GH_ParamAccess.item);
            pManager.AddCurveParameter("Deflected Shape", "Shape", "The beam's deflected shape, exaggerated by Deflection Scale.", GH_ParamAccess.item);
            pManager.AddTextParameter("Summary", "S", "Maximum deflection, deflection check, cracking and notes.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            MaterialProperties mp = null;
            var concrete = new List<Curve>();
            var steel = new List<Curve>();
            object modelObject = null;
            int methodIndex = 1;
            double fr = double.NaN, limit = 250, scale = double.NaN;

            if (!DA.GetData(0, ref mp) || mp == null) return;
            if (!DA.GetDataList(1, concrete)) return;
            if (!DA.GetDataList(2, steel)) return;
            if (!DA.GetData(3, ref modelObject) || modelObject == null) return;
            DA.GetData(4, ref methodIndex);
            bool frGiven = DA.GetData(5, ref fr);
            DA.GetData(6, ref limit);
            bool scaleGiven = DA.GetData(7, ref scale);

            BeamModel model = Unwrap<BeamModel>(modelObject);
            if (model == null || model.Span == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Beam Model must come from the Shear and Moment Diagram component's Beam Model output.");
                return;
            }
            Curve span = model.Span;
            Vector3d up = model.Up;
            var loads = model.Loads;
            var supports = model.Supports;
            if (loads.Count == 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "The Beam Model has no loads: the deflection is zero.");

            if (methodIndex != 0 && methodIndex != 1) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Method must be 0 (Branson) or 1 (Bischoff)."); return; }
            var method = (EffectiveInertiaMethod)methodIndex;
            if (concrete.Count != steel.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Concrete Section and Steel Section need the same number of curves (one steel section per concrete section).");
                return;
            }
            if (!up.Unitize()) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The Beam Model's Up direction is a zero vector."); return; }
            if (!(mp.EC > 0) || !(mp.ES > 0) || !(mp.fC > 0)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Material Properties need positive f'c, Ec and Es."); return; }
            if (!(limit > 0)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Deflection Limit must be greater than zero."); return; }
            double length = span.GetLength();
            if (!(length > Rhino.RhinoMath.ZeroTolerance)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The span curve has no length."); return; }

            // modulus of rupture from the building code setting unless given
            string code = "IS 456";
            if (!frGiven)
            {
                int buildingCode = 0;
                var settings = new GH_SettingsServer("BSEBuildingCode", true);
                buildingCode = settings.GetValue("CodeNumber", buildingCode);
                code = buildingCode == 1 ? "ACI 318" : "IS 456";
                fr = (buildingCode == 1 ? 0.62 : 0.7) * Math.Sqrt(mp.fC);
            }
            if (!(fr > 0)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Modulus of Rupture must be greater than zero."); return; }
            double modularRatio = mp.ES / mp.EC;

            // ---- section properties
            double tol = DocumentTolerance();
            var sections = new List<SectionMechanics>();
            var xs = new List<double>();
            for (int i = 0; i < concrete.Count; i++)
            {
                if (!ToSection(concrete[i], steel[i], up, tol, out List<double[]> outline, out List<SteelLump> bars, out string problem))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Section " + (i + 1) + ": " + problem);
                    return;
                }
                try { sections.Add(SectionMechanics.Compute(outline, bars, modularRatio, fr)); }
                catch (ArgumentException ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Section " + (i + 1) + ": " + ex.Message); return; }

                AreaMassProperties amp = AreaMassProperties.Compute(concrete[i]);
                if (amp == null || !span.ClosestPoint(amp.Centroid, out double t))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Section " + (i + 1) + " could not be located on the span curve.");
                    return;
                }
                xs.Add(span.GetLength(new Interval(span.Domain.Min, t)));
            }

            ServiceDeflectionResult result;
            try { result = ServiceDeflection.Analyze(length, supports, loads, xs, sections, mp.EC, method); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }
            if (!result.Converged)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The effective moment of inertia did not converge after " +
                                  result.Iterations + " iterations; the results may be inaccurate.");
            int missing = result.MissingTensionSteel.Count(b => b);
            if (missing > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, missing + " cracked section(s) have no reinforcement on their tension side " +
                    "(for example hogging at a section with only bottom steel); they use Ieff = Iu (Mcr/Ma)^3.");

            // ---- deflection along the beam
            var samples = Enumerable.Range(0, 401).Select(i => length * i / 400).Concat(result.SectionX).Concat(result.Solution.NodeX).OrderBy(x => x).ToList();
            double maxDefl = 0, maxAt = 0;
            foreach (double x in samples)
            {
                double dv = result.Deflection(x);
                if (Math.Abs(dv) > Math.Abs(maxDefl)) { maxDefl = dv; maxAt = x; }
            }
            if (!scaleGiven || scale == 0) scale = Math.Abs(maxDefl) > 0 ? 0.1 * length / Math.Abs(maxDefl) : 1;
            var shape = new Polyline();
            foreach (double x in samples.Distinct())
            {
                if (!span.LengthParameter(Math.Min(length, Math.Max(0, x)), out double t)) continue;
                Vector3d tangent = span.TangentAt(t);
                Vector3d normal = up - (up * tangent) * tangent;
                if (!normal.Unitize()) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Up Direction is parallel to the span curve."); return; }
                shape.Add(span.PointAt(t) - normal * result.Deflection(x) * scale);
            }

            // ---- summary
            double allowable = length / limit;
            double ratio = Math.Abs(maxDefl) > 0 ? length / Math.Abs(maxDefl) : double.PositiveInfinity;
            int cracked = result.Cracked.Count(c => c);
            var summary = new List<string>
            {
                "Maximum deflection: " + Mm(maxDefl) + " at " + Num(maxAt) + " m (span/" + (double.IsInfinity(ratio) ? "inf" : ratio.ToString("0", CultureInfo.InvariantCulture)) + ")",
                "Limit span/" + Num(limit) + " = " + Mm(allowable) + ": " + (Math.Abs(maxDefl) <= allowable ? "OK" : "EXCEEDED"),
                cracked + " of " + sections.Count + " section(s) cracked under the service loads",
                "Ieff: " + (method == EffectiveInertiaMethod.Branson ? "Branson" : "Bischoff") + "; fr = " + Num(fr) + " MPa" +
                    (frGiven ? "" : " (" + code + ")") + "; n = Es/Ec = " + Num(modularRatio),
                "Converged in " + result.Iterations + " iteration(s)" + (supports.Count > 2 || supports.Any(s => s.Type == SupportType.Fixed) && supports.Count > 1
                    ? " (statically indeterminate: moments redistributed with the cracked stiffness)" : ""),
                "Short-term deflection only: long-term creep and shrinkage are not included.",
            };
            if (Math.Abs(maxDefl) > allowable)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Maximum deflection " + Mm(maxDefl) + " exceeds span/" + Num(limit) + " (" + Mm(allowable) + ").");
            Message = Mm(maxDefl) + " (span/" + (double.IsInfinity(ratio) ? "inf" : ratio.ToString("0", CultureInfo.InvariantCulture)) + ")";

            DA.SetDataList(0, result.Iu);
            DA.SetDataList(1, result.Icr);
            DA.SetDataList(2, result.Mcr);
            DA.SetDataList(3, result.Ieff);
            DA.SetDataList(4, result.Moment);
            DA.SetDataList(5, result.SectionX.Select(result.Rotation));
            DA.SetDataList(6, result.SectionX.Select(result.Deflection));
            DA.SetDataList(7, result.SectionX);
            DA.SetData(8, maxDefl);
            if (shape.Count > 1) DA.SetData(9, shape.ToNurbsCurve());
            DA.SetDataList(10, summary);
        }

        /// <summary>
        /// Converts a concrete section curve and its steel curve to section coordinates: u across the section, v along
        /// the Up direction (projected into the section plane).
        /// </summary>
        private static bool ToSection(Curve concrete, Curve steel, Vector3d up, double tol, out List<double[]> outline,
                                      out List<SteelLump> bars, out string problem)
        {
            outline = null; bars = new List<SteelLump>(); problem = null;
            if (concrete == null || !concrete.IsClosed) { problem = "the concrete section must be a closed curve."; return false; }
            if (!concrete.TryGetPlane(out Plane plane, Math.Max(tol, 1e-6))) { problem = "the concrete section must be planar."; return false; }
            Vector3d normal = plane.ZAxis;
            Vector3d v = up - (up * normal) * normal;
            if (!v.Unitize()) { problem = "the section plane is perpendicular to the Up direction."; return false; }
            Vector3d u = Vector3d.CrossProduct(v, normal);
            Point3d origin = plane.Origin;

            Polyline poly;
            if (!concrete.TryGetPolyline(out poly))
            {
                PolylineCurve pc = concrete.ToPolyline(0.1 * tol, Rhino.RhinoMath.ToRadians(1.0), 0, 0);
                if (pc == null || !pc.TryGetPolyline(out poly)) { problem = "the concrete section could not be converted to a polygon."; return false; }
            }
            var pts = new List<double[]>();
            foreach (Point3d p in poly)
            {
                Vector3d d = p - origin;
                pts.Add(new[] { d * u, d * v });
            }
            if (pts.Count > 1 && Math.Abs(pts[0][0] - pts[pts.Count - 1][0]) < 1e-12 && Math.Abs(pts[0][1] - pts[pts.Count - 1][1]) < 1e-12)
                pts.RemoveAt(pts.Count - 1);
            if (pts.Count < 3) { problem = "the concrete section has fewer than 3 points."; return false; }
            outline = pts;

            if (steel != null)
            {
                AreaMassProperties amp = steel.IsClosed ? AreaMassProperties.Compute(steel) : null;
                if (amp != null && amp.Area > 0)
                    bars.Add(new SteelLump(amp.Area, (amp.Centroid - origin) * v));
            }
            return true;
        }

        private static T Unwrap<T>(object o) where T : class
        {
            if (o is T t) return t;
            if (o is Grasshopper.Kernel.Types.GH_ObjectWrapper w) return w.Value as T;
            if (o is Grasshopper.Kernel.Types.IGH_Goo goo) return goo.ScriptVariable() as T;
            return null;
        }

        private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Mm(double m) => (1000 * m).ToString("0.##", CultureInfo.InvariantCulture) + " mm";

        protected override System.Drawing.Bitmap Icon => Properties.Resources.deflection;

        public override Guid ComponentGuid => new Guid("a13c71d3-6c36-4be9-b1e5-b1fd84bff1b6");
    }
}
