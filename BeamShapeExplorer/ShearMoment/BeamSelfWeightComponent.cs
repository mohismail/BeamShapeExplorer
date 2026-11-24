using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;

using BeamShapeExplorer.DataTypes;

namespace BeamShapeExplorer.ShearMoment
{
    /// <summary>
    /// Turns the self-weight of a beam brep (e.g. from Beam Builder) into distributed loads for Shear and Moment
    /// Diagram. The cross-section area is measured at evenly spaced sections along the span curve, and the self-weight
    /// varies linearly between them, so a shaped (variable-depth) beam gets a matching variable load.
    /// </summary>
    public class BeamSelfWeightComponent : GH_Component
    {
        private const double KgToKN = 0.00980665; // 1 kg of mass weighs 9.80665 N

        public BeamSelfWeightComponent()
          : base("Beam Self-Weight", "BSelfW",
              "Self-weight of a beam brep (e.g. from Beam Builder) as distributed loads for Shear and Moment Diagram. " +
              "The cross-section area is measured at evenly spaced sections along the span curve, and the load varies " +
              "linearly between them. Connect the Loads output together with Beam Loads.",
              "Beam Shape Explorer", "Loads")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Material Properties", "MP",
                "Properties for steel and concrete materials; the concrete density is used.", GH_ParamAccess.item);
            pManager.AddBrepParameter("Beam Brep", "BBrep", "Closed brep of the beam, e.g. the Beam Builder's BBrep output.", GH_ParamAccess.item);
            pManager.AddCurveParameter("Span Curve", "SpnCrv",
                "Curve representing the span of the beam, e.g. the Beam Builder's Span Curve. Use the same curve as on " +
                "Shear and Moment Diagram, because load locations are measured along it.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Number of Sections", "n",
                "Number of evenly spaced sections where the cross-section area is measured (at least 2). More sections " +
                "follow a shaped beam more closely.", GH_ParamAccess.item, 21);
            pManager.AddNumberParameter("Load Factor", "LF",
                "Factor applied to the self-weight, e.g. 1.2 or 1.4 for a factored dead load. Default 1.0 (unfactored).",
                GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Density (kg/m3)", "rho",
                "(Optional) Density to use instead of the concrete density from Material Properties, e.g. to include " +
                "reinforcement (reinforced concrete is about 2400-2500 kg/m3).", GH_ParamAccess.item);

            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Loads", "Loads",
                "Self-weight as distributed loads (one linearly varying load between each pair of sections). Connect them " +
                "to Shear and Moment Diagram.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Self-Weight (kN/m)", "qw", "Self-weight per unit length at each section (factored).", GH_ParamAccess.list);
            pManager.AddNumberParameter("Section Locations (m)", "x", "Distance of each section along the span curve.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Section Areas (m2)", "A", "Cross-section area of the brep at each section.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Total Self-Weight (kN)", "W", "Total self-weight carried by the loads (factored).", GH_ParamAccess.item);
            pManager.AddCurveParameter("Section Curves", "Crv", "Cross-section outlines used to measure the areas.", GH_ParamAccess.list);
            pManager.AddTextParameter("Description", "D", "Summary of the self-weight and a check against the brep volume.", GH_ParamAccess.list);

            ((IGH_PreviewObject)pManager[5]).Hidden = true;
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            MaterialProperties mp = null;
            Brep brep = null;
            Curve span = null;
            int n = 21;
            double factor = 1.0, density = double.NaN;

            if (!DA.GetData(0, ref mp) || mp == null) return;
            if (!DA.GetData(1, ref brep) || brep == null) return;
            if (!DA.GetData(2, ref span) || span == null) return;
            DA.GetData(3, ref n);
            DA.GetData(4, ref factor);
            bool densityGiven = DA.GetData(5, ref density);

            if (n < 2) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Number of Sections must be at least 2."); return; }
            double rho = densityGiven ? density : mp.rhoC;
            if (!(rho > 0)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The density must be greater than zero."); return; }
            double length = span.GetLength();
            if (!(length > Rhino.RhinoMath.ZeroTolerance)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The span curve has no length."); return; }

            double tol = DocumentTolerance();
            double inset = Math.Max(10 * tol, 1e-3 * length); // keep the end sections off the brep's end faces

            var xs = new List<double>();
            var areas = new List<double>();
            var sectionCurves = new List<Curve>();
            var missing = new List<string>();
            for (int i = 0; i < n; i++)
            {
                double x = length * i / (n - 1);
                double xc = Math.Max(inset, Math.Min(length - inset, x));
                if (!span.LengthParameter(xc, out double t)) t = span.Domain.ParameterAt(xc / length);
                var plane = new Plane(span.PointAt(t), span.TangentAt(t));
                double area = SectionArea(brep, plane, tol, sectionCurves, out bool open);
                if (open) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The section at " + Num(x) + " m is not closed; check that the brep is a closed solid.");
                if (area <= 0) missing.Add(Num(x));
                xs.Add(x);
                areas.Add(Math.Max(0, area));
            }
            if (missing.Count == n)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The brep does not cross the span curve's section planes; check that the brep lies along the span curve.");
                return;
            }
            if (missing.Count > 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No cross-section found at " + string.Join(", ", missing) +
                                  " m; the self-weight is 0 there.");

            double unitWeight = rho * KgToKN * factor; // kN/m3
            var qw = areas.Select(a => a * unitWeight).ToList();
            var loads = new List<BeamLoad>();
            double total = 0;
            for (int i = 0; i < n - 1; i++)
            {
                loads.Add(new BeamLoad
                {
                    Type = LoadType.Distributed,
                    Magnitude = qw[i],
                    EndMagnitude = qw[i + 1],
                    Location = xs[i],
                    EndLocation = xs[i + 1],
                    Group = "Self-weight",
                });
                total += 0.5 * (qw[i] + qw[i + 1]) * (xs[i + 1] - xs[i]);
            }

            var description = new List<string>
            {
                "Self-weight: " + Num(qw.Min()) + " to " + Num(qw.Max()) + " kN/m over " + (n - 1) + " segment(s)",
                "Total: " + Num(total) + " kN (density " + Num(rho) + " kg/m3" +
                    (densityGiven ? "" : " from Material Properties") + ", load factor " + Num(factor) + ")",
            };

            // check against the brep's volume
            if (brep.IsSolid)
            {
                VolumeMassProperties vmp = VolumeMassProperties.Compute(brep);
                if (vmp != null && vmp.Volume > 0)
                {
                    double fromVolume = Math.Abs(vmp.Volume) * unitWeight;
                    double diff = fromVolume > 0 ? Math.Abs(total - fromVolume) / fromVolume : 0;
                    description.Add("Check: brep volume " + Num(Math.Abs(vmp.Volume)) + " m3 gives " + Num(fromVolume) +
                                    " kN (difference " + (100 * diff).ToString("0.0", CultureInfo.InvariantCulture) + "%)");
                    if (diff > 0.03)
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The self-weight from the sections (" + Num(total) +
                            " kN) differs from the brep volume (" + Num(fromVolume) + " kN) by " +
                            (100 * diff).ToString("0.0", CultureInfo.InvariantCulture) +
                            "%. Increase Number of Sections, or check that the brep spans the whole span curve.");
                }
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "The brep is not a closed solid, so the total could not be checked against its volume.");
            }
            description.Add("Only the brep's volume is included (no separate reinforcement weight); use Density to account for it.");

            Message = Num(total) + " kN";
            DA.SetDataList(0, loads);
            DA.SetDataList(1, qw);
            DA.SetDataList(2, xs);
            DA.SetDataList(3, areas);
            DA.SetData(4, total);
            DA.SetDataList(5, sectionCurves);
            DA.SetDataList(6, description);
        }

        /// <summary>
        /// Net area of the brep's cross-section in a plane. Holes (curves inside an odd number of other curves) are
        /// subtracted. Section curves are added to <paramref name="outlines"/>.
        /// </summary>
        private static double SectionArea(Brep brep, Plane plane, double tol, List<Curve> outlines, out bool open)
        {
            open = false;
            if (!Intersection.BrepPlane(brep, plane, tol, out Curve[] curves, out Point3d[] _) || curves == null || curves.Length == 0)
                return 0;
            Curve[] joined = Curve.JoinCurves(curves, 2 * tol);
            var closed = new List<Curve>();
            foreach (Curve c in joined)
            {
                if (c.IsClosed || c.PointAtStart.DistanceTo(c.PointAtEnd) <= 2 * tol) closed.Add(c);
                else open = true;
            }
            double area = 0;
            for (int i = 0; i < closed.Count; i++)
            {
                AreaMassProperties amp = AreaMassProperties.Compute(closed[i]);
                if (amp == null) continue;
                int depth = 0;
                for (int j = 0; j < closed.Count; j++)
                    if (i != j && Curve.PlanarClosedCurveRelationship(closed[i], closed[j], plane, tol) == RegionContainment.AInsideB)
                        depth++;
                area += (depth % 2 == 0 ? 1 : -1) * Math.Abs(amp.Area);
                outlines.Add(closed[i]);
            }
            return area;
        }

        private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        protected override System.Drawing.Bitmap Icon => Properties.Resources.loadself2;

        public override Guid ComponentGuid => new Guid("784b4498-a7ff-4835-ba37-93678530e78a");
    }
}
