using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using BeamShapeExplorer.DataTypes;


namespace BeamShapeExplorer
{
    public class IEffective2 : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public IEffective2()
          : base("Effective moment of inertia", "Ieff",
              "Calculates effective moment of inertia for loaded concrete section",
              "Beam Shape Explorer", "Analysis")
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
            pManager.AddBrepParameter("Concrete  Compression Area", "Acomp", "Concrete compression block section, Acomp, can be found using Flexural Analysis component", GH_ParamAccess.list);
            pManager.AddNumberParameter("Moment Envelope (kN-m)", "Mu", "Applied moments (kN-m) along length of element", GH_ParamAccess.list, 15);
            pManager.AddIntegerParameter("Effective moment of inertia method", "Method", "0 for Branson method, 1 for Bischoff and Gross", GH_ParamAccess.item, 1);

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Gross Moment of Inertia (m\x2074)", "Iu", "Gross moment of inertia for each section", GH_ParamAccess.list);
            pManager.AddNumberParameter("Cracked Moment of Inertia (m\x2074)", "Icr", "Moment of inertia for fully cracked sections", GH_ParamAccess.list); 
            pManager.AddNumberParameter("Cracking Moment (kN-m)", "Mcr", "Cracking moment of beam section", GH_ParamAccess.list);

            pManager.AddNumberParameter("Effective Moment of Inertia (m\x2074)", "Ieff", "Effective moment of inertia for loaded concrete sections", GH_ParamAccess.list);
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
            List<Brep> srfAc = new List<Brep>();
            List<double> Mu = new List<double>();
            int Method = 0;


            if (!DA.GetData(0, ref mp)) return;
            if (!DA.GetDataList(1, crvAg)) return;
            if (!DA.GetDataList(2, crvAs)) return;
            if (!DA.GetDataList(3, srfAc)) return;
            if (!DA.GetDataList(4, Mu)) return;
            if (!DA.GetData(5, ref Method)) return;


            //Copy to each analysis plugin - extracts material properties from MP input
            double fc = mp.fC; double Ec = mp.EC; double ec = mp.eC; double rhoc = mp.rhoC; double EEc = mp.EEC;
            double fy = mp.fY; double Es = mp.ES; double es = mp.eS; double rhos = mp.rhoS; double EEs = mp.EES;


            int building_code = 0; string bc = null;
            GH_SettingsServer BCsettings = new GH_SettingsServer("BSEBuildingCode", true);
            building_code = BCsettings.GetValue("CodeNumber", building_code);
            bc = BCsettings.GetValue("CodeName", bc); ;

            double n = Es / Ec;
            Brep[] brepsAg = Brep.CreatePlanarBreps(crvAg, DocumentTolerance()); //Creates planar Breps from input curves

            List<double> xu = new List<double>();
            List<double> d = new List<double>();
            List<double> As = new List<double>();
            List<double> b = new List<double>();
            List<double> xc = new List<double>();
            List<double> h = new List<double>();
            List<Curve> crvB = new List<Curve>();

            List<double> Iu = new List<double>();
            List<double> Ig = new List<double>();
            List<double> Icr = new List<double>();
            List<double> Mcr = new List<double>();
            List<double> Ieff = new List<double>();


            for (int i = 0; i < crvAg.Count; i++)
            {
                double sectIg = AreaMassProperties.Compute(crvAg[i]).CentroidCoordinatesMomentsOfInertia.Y; Ig.Add(sectIg);
                double sectXu = AreaMassProperties.Compute(crvAg[i]).Centroid.Z; xu.Add(sectXu);
                double sectD = AreaMassProperties.Compute(crvAs[i]).Centroid.Z; d.Add(sectD);
                double sectAs = AreaMassProperties.Compute(crvAs[i]).Area; As.Add(sectAs);

                //GROSS MOMENT OF INERTIA
                double sectIu = (sectIg + (n - 1) * sectAs * Math.Pow(sectD - sectXu, 2)); 
                Iu.Add(sectIu);
                
                Brep srfAci = srfAc[i];

                Curve U = srfAci.Faces[0].TrimAwareIsoCurve(0, srfAci.Edges[0].PointAtStart.Z - DocumentTolerance())[0]; 
                Curve V = srfAci.Faces[0].TrimAwareIsoCurve(1, 0)[0]; crvB.Add(V);

                double sectB = U.GetLength(); b.Add(sectB);
                double sectXc = V.GetLength(); xc.Add(sectXc);
                double Iconc = (sectB * Math.Pow(sectXc, 3)) / 3;

                //CRACKED MOMENT OF INERTIA
                double sectIcr = Iconc + (n - 1) * sectAs * Math.Pow((sectD + sectXc), 2);
                Icr.Add(sectIcr);

                //double sectH = AreaMassProperties.Compute(crvAs[i]).Centroid.Z; h.Add(sectH);
                Brep brepAg = brepsAg[i];

                Curve Ug = brepAg.Faces[0].TrimAwareIsoCurve(0, brepAg.Edges[0].PointAtStart.Z - DocumentTolerance())[0];
                Curve Vg = brepAg.Faces[0].TrimAwareIsoCurve(1, 0)[0];

                double sectH = Vg.GetLength(); h.Add(sectH);

                ////CRACKING MOMENT
                double sectMcr = (1000 * 0.7 * Math.Sqrt(fc) * sectIg / (sectH + sectXu));
                Mcr.Add(sectMcr);

                double sectMu = Math.Abs(Mu[i]);
                //double sectMcrAbs = Math.Abs(sectMcr);

                if(Method == 0)
                {
                    double sectIeff = (Iu[i] * Math.Pow((sectMcr / sectMu), 3) + Icr[i] * (1 - Math.Pow((sectMcr / sectMu), 3)));
                    if (sectMcr < sectMu) { Ieff.Add(sectIeff); }
                    else { Ieff.Add(sectIu); }
                }
                else if(Method == 1)
                {
                    double MRatio = sectMcr / sectMu;
                    double epsilon = 1 - Math.Sqrt(1 - MRatio);
                    double nau = 1 - (sectIcr / sectIu);
                    double gamma = (((1.6 * Math.Pow(epsilon, 3)) - (0.6 * Math.Pow(epsilon, 4))) / (MRatio * MRatio) + 2.4 * Math.Log(2 - epsilon));

                    double sectIeff = sectIcr / (1 - (gamma * nau * MRatio*MRatio));
                    if (sectMcr < sectMu) { Ieff.Add(sectIeff); }
                    else { Ieff.Add(sectIu); }
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input Method can only have a value of 0 or 1");
                    return;
                }

            }

            DA.SetDataList(0, Iu);
            DA.SetDataList(1, Icr);
            DA.SetDataList(2, Mcr);
            DA.SetDataList(3, Ieff);


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
            get { return new Guid("29521551-3BA1-4F90-A332-F6918AC9C942"); }
        }
    }
}