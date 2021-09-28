using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using BeamShapeExplorer.DataTypes;


namespace BeamShapeExplorer
{
    public class IGross : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public IGross()
          : base("Gross moment of Inertia", "Iu",
              "Calculates moment of inertia for uncracked concrete section",
              "Beam Shape Explorer", "TEST")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Material Properties", "MP", "Properties for steel and concrete materials", GH_ParamAccess.item);
            pManager.AddCurveParameter("Concrete Section", "Ag", "Concrete sections to analyze for flexural capacity", GH_ParamAccess.list);
            pManager.AddCurveParameter("Steel Section", "As", "Steel sections to analyze to flexural capacity", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Gross Moment of Inertia (m\x2074)", "Iu (m\x2074)", "Gross moment of inertia for each section", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            MaterialProperties mp = null;
            List<Curve> crvAg = new List<Curve>();
            List<Curve> crvAs = new List<Curve>();
            if (!DA.GetData(0, ref mp)) return;
            if (!DA.GetDataList(1, crvAg)) return;
            if (!DA.GetDataList(2, crvAs)) return;


            //Copy to each analysis plugin - extracts material properties from MP input
            double fc = mp.fC; double Ec = mp.EC; double ec = mp.eC; double rhoc = mp.rhoC; double EEc = mp.EEC;
            double fy = mp.fY; double Es = mp.ES; double es = mp.eS; double rhos = mp.rhoS; double EEs = mp.EES;


            int building_code = 0; string bc = null;
            GH_SettingsServer BCsettings = new GH_SettingsServer("BSEBuildingCode", true);
            building_code = BCsettings.GetValue("CodeNumber", building_code);
            bc = BCsettings.GetValue("CodeName", bc); ;

            double n = Es / Ec;

            List<double> Ig = new List<double>();
            List<double> xu = new List<double>();
            List<double> d = new List<double>();
            List<double> As = new List<double>();
            List<double> Iu = new List<double>();
            for(int i = 0; i < crvAg.Count; i++) 
            { 
                double sectIg = AreaMassProperties.Compute(crvAg[i]).CentroidCoordinatesMomentsOfInertia.Y; Ig.Add(sectIg);
                double sectXu = AreaMassProperties.Compute(crvAg[i]).Centroid.Z; xu.Add(sectXu);
                double sectD = AreaMassProperties.Compute(crvAs[i]).Centroid.Z; d.Add(sectD);
                double sectAs = AreaMassProperties.Compute(crvAs[i]).Area; As.Add(sectAs);
                
                double sectIu = (sectIg + (n - 1) * sectAs * Math.Pow(sectD - sectXu, 2)); Iu.Add(sectIu);
            }

            DA.SetDataList(0, Iu);
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
            get { return new Guid("D905B78F-3D61-4364-8F05-DED466BD009D"); }
        }
    }
}