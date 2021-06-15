using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BeamShapeExplorer
{
    public class LoadEnvelope : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public LoadEnvelope()
          : base("Uniform Load Envelope", "LE",
              "Provides moment and shear envelope for a uniformly loaded, simply-supported beam",
              "Beam Shape Explorer", "Loads")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Span Curve", "SpnCrv", "Curve representing the full span on the beam", GH_ParamAccess.item);
            //pManager.AddNumberParameter("Distance Between Sections (m)", "n", "Distance between sections for analysis", GH_ParamAccess.item, 0.25);
            pManager.AddIntegerParameter("Number of Analysis Sections", "n", "Number of analysis sections along length of element", GH_ParamAccess.item, 10);

            pManager.AddNumberParameter("Distributed Load (kN/m)", "q", "Distributed load along the length of beam (kN/m)", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("Graph Scale", "scale", "Scaling factor for graph visualization", GH_ParamAccess.item, 0.1);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Shear Envelope (kN)", "Vu", "Shear envelope for applied loads (kN)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Moment Envelope (kN-m)", "Mu", "Moment envelope for applied loads (kN-m)", GH_ParamAccess.list);

            pManager.AddCurveParameter("Shear and moment graphs", "Graphs", "Shear and moment graph curves", GH_ParamAccess.list);
            ((IGH_PreviewObject)pManager[2]).Hidden = true;
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve spCrv = null;
            int N = 0;
            double Q = 0;
            double scale = 0;

            if (!DA.GetData(0, ref spCrv)) return;
            if (!DA.GetData(1, ref N)) return;
            if (!DA.GetData(2, ref Q)) return;
            if (!DA.GetData(3, ref scale)) return;

            if (N < 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input n must be an integer greater than 2");
                return;
            }

            if (Q <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input q must have a positive value greater than 0");
                return;
            }

            //if (N > spCrv.GetLength() * 0.5)
            //{
            //    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input n must be less than half of the beam's length");
            //    return;
            //}

            double L = spCrv.GetLength();
            //Double[] spCrvDiv = spCrv.DivideByLength(N, true);
            Double[] spCrvDiv = spCrv.DivideByCount(N-1, true);


            List<Point3d> spCrvPts = new List<Point3d>();
            List<Point3d> vuPts = new List<Point3d>();
            List<Point3d> muPts = new List<Point3d>();

            List<double> Vu = new List<double>();
            List<double> Mu = new List<double>();

            for (int i = 0; i < spCrvDiv.Length; i++)
            {
                Point3d spCrvPt = spCrv.PointAt(spCrvDiv[i]);
                spCrvPts.Add(spCrvPt);

                double ptX = spCrvPt.X;
                double ptY = spCrvPt.Y;

                double x = L / (N - 1) * i;

                double vu = Q * (L / 2 - x);
                Vu.Add(vu);
                Point3d vuPt = new Point3d(ptX, ptY, vu * scale);
                vuPts.Add(vuPt);

                double mu = -0.5 * Q * x * (L - x);
                Mu.Add(mu);
                Point3d muPt = new Point3d(ptX, ptY, mu * scale);
                muPts.Add(muPt);
            }

            List<Curve> graphs = new List<Curve>();

            Curve vuCrv = Curve.CreateInterpolatedCurve(vuPts, 1);
            Brep vuSrf = Brep.CreateEdgeSurface(new List<Curve> { vuCrv, spCrv });
            graphs.Add(vuCrv);

            Curve muCrv = Curve.CreateInterpolatedCurve(muPts, 1);
            Brep muSrf = Brep.CreateEdgeSurface(new List<Curve> { muCrv, spCrv });
            graphs.Add(muCrv);

            DA.SetDataList(0, Vu);
            DA.SetDataList(1, Mu);
            DA.SetDataList(2, graphs);

        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return BeamShapeExplorer.Properties.Resources.loaduni2;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("38c7bdc9-7413-483a-a87c-6d391c16650b"); }
        }
    }
}