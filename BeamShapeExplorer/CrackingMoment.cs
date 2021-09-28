using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using BeamShapeExplorer.DataTypes;


namespace BeamShapeExplorer
{
    public class CrackingMoment : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public CrackingMoment()
          : base("Cracking moment", "Mcr",
              "Calculates cracking moment for each concrete section",
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
            pManager.AddNumberParameter("Cracking Moment (kN-m)", "Mcr (kN-m)", "Cracking moment of beam section", GH_ParamAccess.list);
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

            Brep[] brepsAg = Brep.CreatePlanarBreps(crvAg, DocumentTolerance()); //Creates planar Breps from input curves

            List<double> Ig = new List<double>();
            List<double> xu = new List<double>();
            List<double> h = new List<double>();
            List<double> Mcr = new List<double>();
            for (int i = 0; i < crvAg.Count; i++)
            {
                double sectIg = AreaMassProperties.Compute(crvAg[i]).CentroidCoordinatesMomentsOfInertia.Y; Ig.Add(sectIg);
                double sectXu = AreaMassProperties.Compute(crvAg[i]).Centroid.Z; xu.Add(sectXu);


                //double sectH = AreaMassProperties.Compute(crvAs[i]).Centroid.Z; h.Add(sectH);
                Brep brepAg = brepsAg[i];
                Double uMidDom = (brepAg.Faces[0].Domain(0)[1] + brepAg.Faces[0].Domain(0)[0]) / 2;
                Double vMidDom = (brepAg.Faces[0].Domain(1)[1] + brepAg.Faces[0].Domain(1)[0]) / 2;

                Curve U = brepAg.Faces[0].TrimAwareIsoCurve(0, brepAg.Edges[0].PointAtStart.Z - DocumentTolerance())[0];
                Curve V = brepAg.Faces[0].TrimAwareIsoCurve(1, 0)[0];

                double sectH = V.GetLength(); h.Add(sectH);

                double sectMcr = (1000*0.7*Math.Sqrt(fc)*sectIg/(sectH+sectXu)); Mcr.Add(sectMcr);
            }

            DA.SetDataList(0, Mcr);
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
            get { return new Guid("FC3AEAA3-1D79-473D-9CC3-58E57E697593"); }
        }
    }
}