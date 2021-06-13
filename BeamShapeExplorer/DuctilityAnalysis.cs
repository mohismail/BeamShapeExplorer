using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using BeamShapeExplorer.DataTypes;


namespace BeamShapeExplorer
{
    public class DuctilityAnalysis : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public DuctilityAnalysis()
          : base("Ductility Analysis", "DA",
              "Analyzes beam sections for ductility",
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
            pManager.AddCurveParameter("Steel Section", "As", "Steel sections to analyze to flesural capacity", GH_ParamAccess.list);
            pManager.AddNumberParameter("Clear Cover (mm)", "cc", "An initial estimate for the steel clear cover (mm)", GH_ParamAccess.item, 30); //Jonathan

            pManager.AddIntegerParameter("Section subdivisions", "m", "Number of cuts to identify web width, bw, defaults to 10", GH_ParamAccess.item, 15);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Reinforcement Ratio", "ρ", "Steel reinforcement ratio at each section", GH_ParamAccess.list);
            //pManager.AddNumberParameter("Reinforcement ratio overdesign (%)", "ρmax %error", "Percent error of reinforcement ratio overdesign, negative if exceeds the maximum permissable ratio", GH_ParamAccess.list);
            //pManager.AddNumberParameter("Reinforcement ratio underdesign (%)", "ρmin %error", "Percent error of reinforcement ratio underdesign, negative if below the minimum permissable ratio", GH_ParamAccess.list);
            pManager.AddCurveParameter("bwCrv", "bwCrv", "bwCrv", GH_ParamAccess.list);
            pManager.AddNumberParameter("Distance to Tensile Reinforcement (m)", "d", "Distance to Tensile Reinforcement", GH_ParamAccess.list); //Jonathan
            //pManager.AddCurveParameter("Us", "Us", "Us", GH_ParamAccess.list);
            //pManager.AddCurveParameter("Vs", "Vs", "Vs", GH_ParamAccess.list);
            //pManager.AddNumberParameter("uSrfC", "uSrfC", "uSrfC", GH_ParamAccess.list);
            //pManager.AddNumberParameter("vSrfC", "vSrfC", "vSrfC", GH_ParamAccess.list);
            pManager.AddNumberParameter("Max Reinforcement Ratio", "ρmax", "Maximum Steel reinforcement ratio at each section", GH_ParamAccess.list);
            pManager.AddNumberParameter("Min Reinforcement Ratio", "ρmin", "Minimum Steel reinforcement ratio at each section", GH_ParamAccess.list);

            ((IGH_PreviewObject)pManager[1]).Hidden = true;

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
            double CC = 0;
            int M = 0;

            int building_code = 0; string bc = null;

            GH_SettingsServer BCsettings = new GH_SettingsServer("BSEBuildingCode", true);
            building_code = BCsettings.GetValue("CodeNumber", building_code);
            bc = BCsettings.GetValue("CodeName", bc); ;


            if (!DA.GetData(0, ref mp)) return;
            if (!DA.GetDataList(1, crvAg)) return;
            if (!DA.GetDataList(2, crvAs)) return;
            if (!DA.GetData(3, ref CC)) return;
            if (!DA.GetData(4, ref M)) return;

            if (M <= 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input m must be an integer greater than 2");
                return;
            }

            //Copy to each analysis plugin - extracts material properties from MP input
            double fc = mp.fC; double Ec = mp.EC; double ec = mp.eC; double rhoc = mp.rhoC; double EEc = mp.EEC;
            double fy = mp.fY; double Es = mp.ES; double es = mp.eS; double rhos = mp.rhoS; double EEs = mp.EES;

            //Code limits for design
            double cdMax=0, B1=0, rhoMax=0, rhoMin = 0;
            double rhoDes=0, sConst = 0;

            if (building_code == 0)
            {
                cdMax = ec / (ec + es);
                rhoMax = (0.36 * fc / (0.87 * fy)) * cdMax;
                rhoMin = 0.25 * Math.Sqrt(fc) / fy;
            }
            else if (building_code == 1)
            {
                cdMax = ec / (ec + es);
                B1 = 0.85 - (0.05 * ((fc - 28) / 7)); //Calculate Beta_1 due to change of concrete strength
                rhoMax = (0.85 * fc / (fy)) * B1 * cdMax; //ACI-318 Code
                rhoMin = Math.Max(0.25 * Math.Sqrt(fc) / fy, 1.4 / fy);
            }

            //Creates planar Breps from input curves
            Brep[] brepsAg = Brep.CreatePlanarBreps(crvAg, DocumentTolerance());
            Brep[] brepsAs = Brep.CreatePlanarBreps(crvAs, DocumentTolerance());

            List<Curve> bwCrvs = new List<Curve>();
            List<Curve> Us = new List<Curve>(); //Jonathan
            List<Curve> Vs = new List<Curve>();
            List<double> sectRho = new List<double>();
            List<double> sectd = new List<double>(); //Jonathan
            List<double> sectusrfC = new List<double>(); //Jonathan
            List<double> sectvsrfC = new List<double>(); //Jonathan
            List<double> maxErrors = new List<double>();
            List<double> minErrors = new List<double>();

            List<double> rhoMaxs = new List<double>();
            List<double> rhoMins = new List<double>();

            ////REMOVE WHEN DONE TESTING COMPONENT
            //maxErrors.Add(rhoMax);
            //minErrors.Add(rhoMin);

            for (int i = 0; i < crvAg.Count; i++)
            {
                Brep brepAg = brepsAg[i];
                Brep brepAs = brepsAs[i];

                Surface srfAg = brepAg.Surfaces[0];
                Surface srfAs = brepAs.Surfaces[0]; //Jonathan

                Double uMidDom = (srfAg.Domain(0)[1] + srfAg.Domain(0)[0]) / 2;
                Double vMidDom = (srfAg.Domain(1)[1] + srfAg.Domain(1)[0]) / 2;
                Double uSrfC = srfAg.Domain(0)[1] - srfAg.Domain(0)[0];
                Double vSrfC = srfAg.Domain(1)[1] - srfAg.Domain(1)[0]; //Works fine for bw //Jonathan
                //Double uSrfC = srfAg.Domain(0)[1] - srfAg.Domain(0)[0] - srfAs.Domain(1)[0];
                //Double vSrfC = srfAg.Domain(1)[1] - srfAg.Domain(1)[0] - srfAs.Domain(0)[1];

                //Correction of U and V curve extraction
                Curve U0 = srfAg.IsoCurve(0, vMidDom);
                Curve[] UIntCrv; Point3d[] UIntPt;
                Rhino.Geometry.Intersect.Intersection.CurveBrep(U0, brepAg, DocumentTolerance(), out UIntCrv, out UIntPt);
                Curve U = UIntCrv[0];

                Curve V0 = srfAg.IsoCurve(1, uMidDom);
                Curve[] VIntCrv; Point3d[] VIntPt;
                Rhino.Geometry.Intersect.Intersection.CurveBrep(V0, brepAg, DocumentTolerance(), out VIntCrv, out VIntPt);
                Curve V = VIntCrv[0];

                Point3d endPtV = V.PointAtEnd; Point3d startPtV = V.PointAtStart;
                if (endPtV.Z > startPtV.Z) { V.Reverse(); }

                Double[] vDivision = V.DivideByCount(M, false);
                Plane[] vPlane = V.GetPerpendicularFrames(vDivision);

                Curve[] vContours = new Curve[vPlane.Length];
                double[] vConLen = new double[vPlane.Length];
                for (int j = 0; j < vPlane.Length; j++)
                {
                    Curve[] vContour = Brep.CreateContourCurves(brepAg, vPlane[j]);
                    if (vContour.Length > 0)
                    {
                        vContours[j] = vContour[0];
                        vConLen[j] = vContour[0].GetLength();
                    }
                }
                Curve bCrv = vContours[0];
                Array.Sort(vConLen, vContours);
                Curve bwCrv = null;
                //if (vContours[0] == null) { bwCrv = U; }
                //else { bwCrv = vContours[0]; }
                bwCrv = vContours[0];
                if (bwCrv == null) { bwCrv = U; }

                bwCrvs.Add(bwCrv);
                Us.Add(U);
                Vs.Add(V);

                double areaAs = AreaMassProperties.Compute(crvAs[i]).Area;
                double bw=0, d=0, bwd = 0;

                if (building_code == 0)
                {
                    bw = bwCrv.GetLength(); d = U.GetLength(); bwd = bw * d;
                }
                else if (building_code == 1)
                {
                    double Agheight = 0;
                    Point3d centAs = new Point3d();
                    Point3d centAg = new Point3d();

                    //Agheight = srfAg.Domain(0)[0] - srfAg.Domain(0)[1];
                    centAs = AreaMassProperties.Compute(brepAs).Centroid;
                    centAg = AreaMassProperties.Compute(brepAg).Centroid;

                    bw = bwCrv.GetLength(); d = (0.96 * V.GetLength()) - (CC / 1000); bwd = bw * d;
                }

                double rho = areaAs / bwd;

                if (double.IsPositiveInfinity(rho) || double.IsNegativeInfinity(rho))
                {
                    //AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Reinforcement ratio is invalid.");
                    //return;

                    rho = 0;
                }

                sectRho.Add(rho);

                sectd.Add(d);

                sectusrfC.Add(uSrfC);

                sectvsrfC.Add(vSrfC);

                double maxError = 100*((rhoMax-rho)/rhoMax);
                maxErrors.Add(maxError);

                double minError = 100*(rho-rhoMin)/rhoMin;
                minErrors.Add(minError);

                rhoMaxs.Add(rhoMax);
                rhoMins.Add(rhoMin);
            }

            DA.SetDataList(0, sectRho);
            DA.SetDataList(1, bwCrvs);
            DA.SetDataList(2, sectd);
            DA.SetDataList(3, rhoMaxs);
            DA.SetDataList(4, rhoMins);

            //DA.SetDataList(1, maxErrors);
            //DA.SetDataList(2, minErrors);
            //DA.SetDataList(5, Us);
            //DA.SetDataList(6, Vs);
            //DA.SetDataList(7, sectusrfC);
            //DA.SetDataList(8, sectvsrfC);
            //DA.SetDataList(5, rhoMaxs);
            //DA.SetDataList(6, rhoMins);

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
                return BeamShapeExplorer.Properties.Resources.duct;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("7f00cc08-61de-4c03-bdca-293defc18749"); }
        }
    }
}