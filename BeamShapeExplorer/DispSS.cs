using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using BeamShapeExplorer.DataTypes;

namespace BeamShapeExplorer
{
    public class DispSS : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public DispSS()
          : base("Displacement of Simply Supported Beam", "DefSS",
              "Calculates deflection and rotation for simply supported beam",
              "Beam Shape Explorer", "Analysis")
        {
            
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Material Properties", "MP", "Properties for steel and concrete materials", GH_ParamAccess.item);
            pManager.AddNumberParameter("Moment Envelope (kN-m)", "Mu", "Applied moments (kN-m) along length of element", GH_ParamAccess.list);
            pManager.AddNumberParameter("Effective Moment of Inertia (m\x2074)", "Ieff", "Effective moment of inertia for loaded concrete sections", GH_ParamAccess.list);
            pManager.AddCurveParameter("Span Curve", "SpnCrv", "Curve representing the full span on the beam", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Rotation", "\u03F4", "Rotation along length of simply supported beam", GH_ParamAccess.list);
            pManager.AddNumberParameter("Deflection (m)", "\u0394", "Deflection (m) along length of simply supported beam", GH_ParamAccess.list);

        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            MaterialProperties mp = null;
            List<double> Mu = new List<double>();
            List<double> Ie = new List<double>();
            Curve spCrv = null;


            if (!DA.GetData(0, ref mp)) return;
            if (!DA.GetDataList(1, Mu)) return;
            if (!DA.GetDataList(2, Ie)) return;
            if (!DA.GetData(3, ref spCrv)) return;


            //Copy to each analysis plugin - extracts material properties from MP input
            double fc = mp.fC; double Ec = mp.EC; double ec = mp.eC; double rhoc = mp.rhoC; double EEc = mp.EEC;
            double fy = mp.fY; double Es = mp.ES; double es = mp.eS; double rhos = mp.rhoS; double EEs = mp.EES;


            int building_code = 0; string bc = null;
            GH_SettingsServer BCsettings = new GH_SettingsServer("BSEBuildingCode", true);
            building_code = BCsettings.GetValue("CodeNumber", building_code);
            bc = BCsettings.GetValue("CodeName", bc); ;

            List<double> R = new List<double>();
            List<double> D = new List<double>();
            
            int countR = (int)Math.Ceiling((double)Mu.Count / 2);
            double L = spCrv.GetLength();
            double dx = L / (Mu.Count - 1);

            for (int i = 0; i < countR; i++)
            {
                double sectMu = Math.Abs(Mu[i]);
                double sectIe = Math.Abs(Ie[i]);

                double sectR = (sectMu/(Ec*sectIe*1000))*dx;
                R.Add(sectR);
                
                if (i > 0) 
                {
                    R[i] = (R[i] + R[i - 1]); //integration along length
                }
            }

            for (int i = 0; i < R.Count; i++)
            {
                R[i] = (R[i] - R[countR-1]); //subtract integration constant 1
            }

            List<double> RtoD = new List<double>(R);

            for (int i = 0; i < R.Count; i++)
            {
                RtoD[i] = RtoD[i] * dx;
                if(i > 0) {RtoD[i] = RtoD[i] + RtoD[i - 1]; } //integration along length
            }

            for (int i = 0; i < R.Count; i++)
            {
                double sectD = (RtoD[i] - RtoD[0]); //subtract integration constant 1
                D.Add(sectD);
            }

            DA.SetDataList(0, R);
            DA.SetDataList(1, D);

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
            get { return new Guid("25E94FC8-B9D1-4D61-9C9A-56486877BD5A"); }
        }
    }
}