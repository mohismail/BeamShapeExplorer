using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel;

namespace BeamShapeExplorer.ShearMoment
{
    /// <summary>Defines point and distributed loads and where they act along the span.</summary>
    public class BeamLoadsComponent : GH_Component
    {
        public BeamLoadsComponent()
          : base("Beam Loads", "BLoads",
              "Defines point and distributed loads on a beam and where they act along the span. Each list item is one " +
              "load; shorter lists repeat their last item. Connect the Loads output to Shear and Moment Diagram.",
              "Beam Shape Explorer", "Loads")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Load Type", "T",
                "Type of each load: point or distributed.", GH_ParamAccess.list, "point");
            pManager.AddNumberParameter("Magnitude", "P",
                "Point load (kN), or distributed load intensity at its start (kN/m). Positive acts downward " +
                "(against the diagram's Up direction); negative acts upward.", GH_ParamAccess.list, 10.0);
            pManager.AddNumberParameter("Location", "x",
                "Position of a point load, or start of a distributed load, measured along the span curve from its " +
                "start (m, or a fraction of the span if Relative is true).", GH_ParamAccess.list, 0.0);
            pManager.AddNumberParameter("End Location", "xEnd",
                "(Optional) End of a distributed load (m, or a fraction of the span). Default: the end of the span. " +
                "Ignored for point loads.", GH_ParamAccess.list);
            pManager.AddNumberParameter("End Magnitude", "PEnd",
                "(Optional) Intensity at the end of a distributed load (kN/m), for a linearly varying (triangular or " +
                "trapezoidal) load. Default: the same as Magnitude (uniform). Ignored for point loads.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Relative", "Rel",
                "If true, Location and End Location are fractions of the span length (0 = start, 1 = end) instead of distances.",
                GH_ParamAccess.item, false);

            pManager[3].Optional = true;
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Loads", "Loads", "Beam loads. Connect them to Shear and Moment Diagram.", GH_ParamAccess.list);
            pManager.AddTextParameter("Description", "D", "A description of each load.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var types = new List<string>();
            var magnitudes = new List<double>();
            var locations = new List<double>();
            var endLocations = new List<double>();
            var endMagnitudes = new List<double>();
            bool relative = false;
            DA.GetDataList(0, types);
            DA.GetDataList(1, magnitudes);
            DA.GetDataList(2, locations);
            DA.GetDataList(3, endLocations);
            DA.GetDataList(4, endMagnitudes);
            DA.GetData(5, ref relative);

            if (types.Count == 0 || magnitudes.Count == 0 || locations.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Load Type, Magnitude and Location need at least one value each.");
                return;
            }

            int count = new[] { types.Count, magnitudes.Count, locations.Count, endLocations.Count, endMagnitudes.Count }.Max();
            var loads = new List<BeamLoad>();
            bool ignoredEnds = false;
            for (int i = 0; i < count; i++)
            {
                string typeText = Pick(types, i);
                LoadType? type = BeamLoad.ParseType(typeText);
                if (type == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Unknown load type \"" + typeText + "\". Use point or distributed.");
                    return;
                }
                var load = new BeamLoad
                {
                    Type = type.Value,
                    Magnitude = Pick(magnitudes, i),
                    Location = Pick(locations, i),
                    Relative = relative,
                };
                load.EndMagnitude = endMagnitudes.Count > 0 ? Pick(endMagnitudes, i) : load.Magnitude;
                load.EndLocation = endLocations.Count > 0 ? Pick(endLocations, i) : double.NaN;
                if (load.Type == LoadType.Point)
                {
                    if (i < endLocations.Count || i < endMagnitudes.Count) ignoredEnds = true;
                    load.EndMagnitude = load.Magnitude;
                    load.EndLocation = double.NaN;
                }
                else if (!double.IsNaN(load.EndLocation) && load.EndLocation <= load.Location)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Load " + (i + 1) + ": End Location must be greater than Location.");
                    return;
                }
                if (relative && (load.Location < 0 || load.Location > 1 || load.EndLocation < 0 || load.EndLocation > 1))
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        "Load " + (i + 1) + ": relative locations should be between 0 and 1.");
                loads.Add(load);
            }
            if (ignoredEnds)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "End Location and End Magnitude are ignored for point loads.");

            Message = loads.Count + " load(s)";
            DA.SetDataList(0, loads);
            DA.SetDataList(1, loads.Select(l => l.ToString()));
        }

        private static T Pick<T>(List<T> list, int i) => list[Math.Min(i, list.Count - 1)];

        protected override System.Drawing.Bitmap Icon => Properties.Resources.beamloads;

        public override Guid ComponentGuid => new Guid("72775269-81d3-4d95-bc08-8260376b0782");
    }

    /// <summary>Defines fixed, pinned and roller supports from Grasshopper points.</summary>
    public class BeamSupportsComponent : GH_Component
    {
        public BeamSupportsComponent()
          : base("Beam Supports", "BSupports",
              "Defines beam supports from points and support types (fixed, pinned or roller). The points are " +
              "projected onto the span curve by Shear and Moment Diagram. Shorter Type lists repeat their last item.",
              "Beam Shape Explorer", "Loads")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Points", "Pt",
                "Support locations. Each point is projected onto the span curve, e.g. the ends of the Beam Builder's Span Curve.",
                GH_ParamAccess.list);
            pManager.AddTextParameter("Support Type", "T",
                "Type of each support: fixed (restrains movement and rotation), pinned or roller (restrain movement " +
                "only; they behave the same for the vertical loads analyzed here).", GH_ParamAccess.list, "pinned");
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Supports", "Sup", "Beam supports. Connect them to Shear and Moment Diagram.", GH_ParamAccess.list);
            pManager.AddTextParameter("Description", "D", "A description of each support.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var points = new List<Rhino.Geometry.Point3d>();
            var types = new List<string>();
            if (!DA.GetDataList(0, points)) return;
            DA.GetDataList(1, types);
            if (types.Count == 0) types.Add("pinned");

            var supports = new List<BeamSupport>();
            for (int i = 0; i < points.Count; i++)
            {
                string text = types[Math.Min(i, types.Count - 1)];
                SupportType? type = BeamSupport.ParseType(text);
                if (type == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Unknown support type \"" + text + "\". Use fixed, pinned or roller.");
                    return;
                }
                supports.Add(new BeamSupport { Point = points[i], Type = type.Value });
            }
            if (types.Count > points.Count)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "There are more support types than points; the extra types are ignored.");

            Message = supports.Count + " support(s)";
            DA.SetDataList(0, supports);
            DA.SetDataList(1, supports.Select(s => s.ToString()));
        }

        protected override System.Drawing.Bitmap Icon => Properties.Resources.beamsupports;

        public override Guid ComponentGuid => new Guid("6b601dcc-163a-49f1-8090-cb5cb7eacfd6");
    }
}
