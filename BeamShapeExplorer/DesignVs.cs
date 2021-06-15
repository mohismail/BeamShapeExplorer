using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using BeamShapeExplorer.DataTypes;


namespace BeamShapeExplorer
{
    public class DesignVs : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public DesignVs()
          : base("Vs Design", "VsD",
              "Design for shear reinforcement, Vs",
              "Beam Shape Explorer", "Design")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Material Properties", "MP", "Properties for steel and concrete materials", GH_ParamAccess.item);
            pManager.AddCurveParameter("Concrete Section", "Ag", "Concrete sections to analyze for flexural capacity", GH_ParamAccess.list);

            pManager.AddNumberParameter("Shear Reinforcement Diameter (mm)", "dv", "Diameter (mm) of transverse reinforcement", GH_ParamAccess.item, 10);
            pManager.AddNumberParameter("Shear Reinforcement Spacing (mm)", "s", "Spaceing (mm) of transverse reinforcement", GH_ParamAccess.item, 100);
            pManager.AddNumberParameter("Shear Reinforcement Inclination Angle (rads)", "θ", "Inclination angle (radians) of transverse reinforcement", GH_ParamAccess.item, (Math.PI/2));
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Design Shear Reinforcement (kN)", "Vs", "Design resistance (kN) of transverse reinforcement", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            MaterialProperties mp = null;
            List<Curve> crvAg = new List<Curve>();
            double dv = 0;
            double s = 0;
            double theta = 0;

            if (!DA.GetData(0, ref mp)) return;
            if (!DA.GetDataList(1, crvAg)) return;
            if (!DA.GetData(2, ref dv)) return;
            if (!DA.GetData(3, ref s)) return;
            if (!DA.GetData(4, ref theta)) return;

            //Copy to each analysis plugin - extracts material properties from MP input
            double fc = mp.fC; double Ec = mp.EC; double ec = mp.eC; double rhoc = mp.rhoC; double EEc = mp.EEC;
            double fy = mp.fY; double Es = mp.ES; double es = mp.eS; double rhos = mp.rhoS; double EEs = mp.EES;

            //Creates planar Breps from input curves
            Brep[] brepsAg = Brep.CreatePlanarBreps(crvAg, DocumentTolerance());

            List<double> Vs = new List<double>();

            for (int i = 0; i < crvAg.Count; i++)
            {
                Brep brepAg = brepsAg[i];

                Surface srfAg = brepAg.Surfaces[0];
                Double uSrfC = srfAg.Domain(0)[1] - srfAg.Domain(0)[0];
                Double vSrfC = srfAg.Domain(1)[1] - srfAg.Domain(1)[0];

                Curve U = srfAg.IsoCurve(0, 0.5 * vSrfC + srfAg.Domain(1)[0]);
                Curve V = srfAg.IsoCurve(1, 0.5 * uSrfC + srfAg.Domain(0)[0]);

                Point3d endPtV = V.PointAtEnd; Point3d startPtV = V.PointAtStart;
                if (endPtV.Z > startPtV.Z) { V.Reverse(); }

                double d = U.GetLength() * 1000;

                double Av = Math.PI * dv * dv / 4;
                double sectVs = 0.87 * fy * Av * d * (Math.Sin(theta) + Math.Cos(theta)) / (1000 * s);

                Vs.Add(sectVs);
            }

            DA.SetDataList(0, Vs);


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
                return BeamShapeExplorer.Properties.Resources.vs;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("158677d8-12bc-4d58-9c26-5a4515a6173a"); }
        }
    }
}