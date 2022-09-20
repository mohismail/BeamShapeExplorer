using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using BeamShapeExplorer.DataTypes;


namespace BeamShapeExplorer
{
    public class FlexAnalysis2 : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public FlexAnalysis2()
          : base("Flexural Analysis, V2", "FA2",
              "Analyzes beam sections for flexural capacity, Version 2",
              "Beam Shape Explorer", "Analysis")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Material Properties", "MP", "Properties for steel and concrete materials", GH_ParamAccess.item);
            pManager.AddNumberParameter("Moment Envelope (kN-m)", "Mu", "Applied moments (kN-m) along length of element", GH_ParamAccess.list, 15);

            pManager.AddBrepParameter("Concrete Section", "Ag", "Concrete sections to analyze for flexural capacity", GH_ParamAccess.list);
            pManager.AddCurveParameter("Steel Section", "As", "Steel sections to analyze to flexural capacity", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Section subdivisions", "m", "Number of cuts to integrate along section depth, defaults to 10", GH_ParamAccess.item, 15);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Moment Capacity (kN-m)", "Mn", "Moment Capacity (kN-m)", GH_ParamAccess.list);
            pManager.AddBrepParameter("Compression Area", "Acomp", "Area of concrete in compression block, Acomp", GH_ParamAccess.list);
            pManager.AddBrepParameter("Compression Area", "Acomp", "Area of concrete in compression block, Acomp", GH_ParamAccess.tree);
            pManager.AddPointParameter("Centroid", "c_bar", "Centroid of compression block, c_bar", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            //Map out inputs
            MaterialProperties mp = null;
            List<double> Mu = new List<double>();
            List<Brep> srfAg = new List<Brep>();
            List<Curve> crvAs = new List<Curve>();
            int M = 0;
            int building_code = 0; string bc = null;

            //Change settings file for building code
            GH_SettingsServer BCsettings = new GH_SettingsServer("BSEBuildingCode", true);
            building_code = BCsettings.GetValue("CodeNumber", building_code);
            bc = BCsettings.GetValue("CodeName", bc); ;

            if (!DA.GetData(0, ref mp)) return;
            if (!DA.GetDataList(1, Mu)) return;
            if (!DA.GetDataList(2, srfAg)) return;
            if (!DA.GetDataList(3, crvAs)) return;
            if (!DA.GetData(4, ref M)) return;

            //Copy to each analysis plugin - extracts material properties from MP input
            double fc = mp.fC; double Ec = mp.EC; double ec = mp.eC; double rhoc = mp.rhoC; double EEc = mp.EEC;
            double fy = mp.fY; double Es = mp.ES; double es = mp.eS; double rhos = mp.rhoS; double EEs = mp.EES;

            double tol = DocumentTolerance();

            if (M <= 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input m must be an integer greater than 2");
                return;
            }

            //Code limits for design
            double cdMax, B1, rhoMax = 0, rhoMin = 0;
            double rhoDes, sConst = 0;

            //Code limits for design
            cdMax = ec / (ec + es);
            if (building_code == 0)
            {
                rhoMax = (0.36 * fc / (0.87 * fy)) * cdMax; //Indian NBC
                rhoMin = 0.25 * Math.Sqrt(fc) / fy; //Indian NBC
            }
            else if (building_code == 1)
            {
                B1 = 0.85 - (0.05 * ((fc - 28) / 7)); //Calculate Beta_1 due to change of concrete strength
                rhoMax = (0.85 * fc / (fy)) * B1 * cdMax; //ACI-318 Code
                rhoMin = Math.Max(0.25 * Math.Sqrt(fc) / fy, 1.4 / fy);
            }

            //Steel design constants
            rhoDes = 0.66 * rhoMax;
            sConst = 0.87 * fy * (1 - 1.005 * rhoDes * (fy / fc));

            //for testing with single moment at all points
            double[] newMu = new double[srfAg.Count];
            for (int i = 0; i < srfAg.Count; i++) { newMu[i] = Mu[0]; }
            if (Mu.Count == srfAg.Count) { newMu = Mu.ToArray(); }

            //Create planar Breps from input curves
            Brep[] brepsAs = Brep.CreatePlanarBreps(crvAs, tol);

            //Initialize lists
            List<double> Mn = new List<double>();
            List<double> errors = new List<double>();
            List<Brep> Ac_srf_all = new List<Brep>();
            var Ac_srf_tree = new GH_Structure<IGH_Goo>();
            List<double> Ac_des_all = new List<double>();
            List<Point3d> c_all = new List<Point3d>();


            //Actual design and calculations
            for (int i = 0; i < srfAg.Count; i++)
            {
                Brep brepAg = srfAg[i];
                Brep brepAs = brepsAs[i];

                //Extract guiding isoline
                Plane plane = Plane.WorldYZ;
                BoundingBox bbox = brepAg.GetBoundingBox(true);
                Line V = bbox.GetEdges()[8];

                //Correcting isoline direction
                Vector3d V_dir = V.Direction;
                if (V_dir.Z > 0) { V.Flip(); }
                if (Mu[i] > 0) { V.Flip(); } //positive moment correction

                //Extract cutting planes
                double[] t = new double[M];
                double n = 0;
                for (int j = 0; j < t.Length; j++)
                {
                    t[j] = (n / M) * V.Length;
                    n++;
                }
                Plane[] planes = V.ToNurbsCurve().GetPerpendicularFrames(t);

                //Starting variables, adjust to include formula for material properties
                double desiredArea = brepAs.GetArea() * 0.87 * fy / (0.36 * fc); 
                int stop = 0;
                double Ac_des = 0;
                Point3d c = new Point3d();
                Brep[] Ac_srf = null;

                //Find Ac using input planes, desired Ac, and section surface
                for (int j = 1; j < planes.Length; j++)
                {
                    if (Ac_des < desiredArea)
                    {
                        Brep[] Ag_srfs = brepAg.Trim(planes[j], tol);
                        Ac_des = 0;
                        c = Point3d.Origin;

                        for (int k = 0; k < Ag_srfs.Length; k++)
                        {
                            Ac_des += Ag_srfs[k].GetArea(tol, tol);
                            Point3d c_j = AreaMassProperties.Compute(Ag_srfs[k]).Centroid;
                            c += c_j;
                        }

                        c /= Ag_srfs.Length;

                        Ac_srf = brepAg.Trim(planes[j], tol);
                        stop = i;

                    }
                    

                }


                Point3d centBrepAs = new Point3d();
                if (brepAs.IsValid){centBrepAs = AreaMassProperties.Compute(brepAs).Centroid;}

                //Calculate Mn
                double Z = Math.Abs(c.Z) - Math.Abs(centBrepAs.Z);
                double T = 0;
                double As = brepAs.GetArea();
                if (building_code == 0)
                {
                    T = 0.87 * As * fy * 1000; //ADD NEGATIVE ACCOMODATION...
                }
                else if (building_code == 1)
                {
                    T = As * fy * 1000; //ADD NEGATIVE ACCOMODATION... 
                }

                double sectMn = T * Z;
                Mn.Add(sectMn);

                double error = ((sectMn - newMu[i]) / newMu[i]) * 100;
                errors.Add(error);

                Ac_des_all.Add(Ac_des);
                foreach (Brep ac in Ac_srf) { Ac_srf_all.Add(ac); }
                c_all.Add(c);

                for(int j =0; j < Ac_srf.Length; j++) 
                { 
                    GH_Path ghpath = new GH_Path(i,j);
                    GH_Brep Ac_srf_goo = new GH_Brep(Ac_srf[j]);
                    Ac_srf_tree.Append(Ac_srf_goo, ghpath);
                }

            }

            DA.SetDataList(0, Mn);
            DA.SetDataList(1, Ac_srf_all);
            DA.SetDataTree(2, Ac_srf_tree);
            DA.SetDataList(3, c_all);
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
                return BeamShapeExplorer.Properties.Resources.flex;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("6070707F-1F80-4C67-9DE5-C580F3045BBC"); }
        }
    }
}