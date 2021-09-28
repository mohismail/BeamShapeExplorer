using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using BeamShapeExplorer.DataTypes;


namespace BeamShapeExplorer
{
    public class ICracked : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public ICracked()
          : base("Cracked moment of Inertia", "Icr",
              "Calculates moment of inertia for fully cracked concrete section",
              "Beam Shape Explorer", "TEST")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Material Properties", "MP", "Properties for steel and concrete materials", GH_ParamAccess.item);
            pManager.AddBrepParameter("Concrete  Compression Area", "Acomp", "Concrete compression block section, Acomp, can be found using Flexural Analysis component", GH_ParamAccess.list);
            pManager.AddCurveParameter("Steel Section", "As", "Steel sections to analyze to flexural capacity", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Cracked Moment of Inertia (m\x2074)", "Icr (m\x2074)", "Moment of inertia for fully cracked sections", GH_ParamAccess.list);
            //pManager.AddCurveParameter("Cracked Moment of Inertia", "Icr (m\x2074)", "Moment of area for fully cracked sections", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            MaterialProperties mp = null;
            List<Brep> srfAc = new List<Brep>();
            List<Curve> crvAs = new List<Curve>();
            if (!DA.GetData(0, ref mp)) return;
            if (!DA.GetDataList(1, srfAc)) return;
            if (!DA.GetDataList(2, crvAs)) return;


            //Copy to each analysis plugin - extracts material properties from MP input
            double fc = mp.fC; double Ec = mp.EC; double ec = mp.eC; double rhoc = mp.rhoC; double EEc = mp.EEC;
            double fy = mp.fY; double Es = mp.ES; double es = mp.eS; double rhos = mp.rhoS; double EEs = mp.EES;


            int building_code = 0; string bc = null;
            GH_SettingsServer BCsettings = new GH_SettingsServer("BSEBuildingCode", true);
            building_code = BCsettings.GetValue("CodeNumber", building_code);
            bc = BCsettings.GetValue("CodeName", bc); ;

            double n = Es / Ec;

            List<double> d = new List<double>();
            List<double> As = new List<double>();
            List<double> b = new List<double>();
            List<double> xc = new List<double>();
            List<double> Icr = new List<double>();

            List<Curve> crvB = new List<Curve>();

            for (int i = 0; i < crvAs.Count; i++)
            {
                double sectD = Math.Abs(AreaMassProperties.Compute(crvAs[i]).Centroid.Z); d.Add(sectD);
                double sectAs = AreaMassProperties.Compute(crvAs[i]).Area; As.Add(sectAs);
                
                Brep srfAci = srfAc[i];

                Double uMidDom = (srfAci.Faces[0].Domain(0)[1] + srfAci.Faces[0].Domain(0)[0]) / 2;
                Double vMidDom = (srfAci.Faces[0].Domain(1)[1] + srfAci.Faces[0].Domain(1)[0]) / 2;

                Curve U = srfAci.Faces[0].TrimAwareIsoCurve(0, srfAci.Edges[0].PointAtStart.Z-DocumentTolerance())[0];
                Curve V = srfAci.Faces[0].TrimAwareIsoCurve(1, 0)[0];

                double sectB = U.GetLength();
                double sectXc = V.GetLength();

                double Iconc = (sectB * Math.Pow(sectXc, 3)) / 3;
                double sectIcr = Iconc + (n - 1) * sectAs * Math.Pow((sectD - sectXc), 2);

                crvB.Add(V);
                b.Add(sectB);
                xc.Add(sectXc);
                Icr.Add(sectIcr);
            }

                DA.SetDataList(0, Icr);
                //DA.SetDataList(1, crvB);

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
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("B68B1FE5-D59D-4C49-B086-3C3B7EDE5F12"); }
        }
    }
}