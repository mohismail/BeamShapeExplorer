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

            pManager.AddIntegerParameter("Section subdivisions", "m", "Number of cuts to identify web width, bw, defaults to 10", GH_ParamAccess.item, 15);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Reinforcement Ratio", "ρ", "Steel reinforcement ratio at each section", GH_ParamAccess.list);
            pManager.AddNumberParameter("Reinforcement ratio overdesign (%)", "ρmax %error", "Percent error of reinforcement ratio overdesign, negative if exceeds the maximum permissable ratio", GH_ParamAccess.list);
            pManager.AddNumberParameter("Reinforcement ratio underdesign (%)", "ρmin %error", "Percent error of reinforcement ratio underdesign, negative if below the minimum permissable ratio", GH_ParamAccess.list);
            pManager.AddCurveParameter("bwCrv", "bwCrv", "bwCrv", GH_ParamAccess.list);
            pManager.AddNumberParameter("Max Reinforcement Ratio", "ρmax", "Maximum Steel reinforcement ratio at each section", GH_ParamAccess.list);
            pManager.AddNumberParameter("Min Reinforcement Ratio", "ρmin", "Minimum Steel reinforcement ratio at each section", GH_ParamAccess.list);

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
            int M = 0;

            if (!DA.GetData(0, ref mp)) return;
            if (!DA.GetDataList(1, crvAg)) return;
            if (!DA.GetDataList(2, crvAs)) return;
            if (!DA.GetData(3, ref M)) return;

            if (M <= 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input m must be an integer greater than 2");
                return;
            }

            //Copy to each analysis plugin - extracts material properties from MP input
            double fc = mp.fC; double Ec = mp.EC; double ec = mp.eC; double rhoc = mp.rhoC; double EEc = mp.EEC;
            double fy = mp.fY; double Es = mp.ES; double es = mp.eS; double rhos = mp.rhoS; double EEs = mp.EES;

            //Code limits for design
            double cdMax = ec / (ec + es);
            double rhoMax = (0.319 * fc / (fy)); //Changed coefficients. Assuming tension-controlled limit controls
            double rhoMin = Math.max((0.25 * Math.Sqrt(fc) / fy),(1.4 / fy)); //ACI 9.6.1.2 SI Units

            //Creates planar Breps from input curves
            Brep[] brepsAg = Brep.CreatePlanarBreps(crvAg, DocumentTolerance());
            Brep[] brepsAs = Brep.CreatePlanarBreps(crvAs, DocumentTolerance()); 

            List<Curve> bwCrvs = new List<Curve>();
            List<double> sectRho = new List<double>();
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
                Double uSrfC = srfAg.Domain(0)[1] - srfAg.Domain(0)[0];
                Double vSrfC = srfAg.Domain(1)[1] - srfAg.Domain(1)[0];

                Curve U = srfAg.IsoCurve(0, 0.5 * vSrfC + srfAg.Domain(1)[0]);
                Curve V = srfAg.IsoCurve(1, 0.5 * uSrfC + srfAg.Domain(0)[0]);

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
                bwCrvs.Add(bwCrv);

                double areaAs = AreaMassProperties.Compute(crvAs[i]).Area;
                double bw = 0;
                double d = 0;
                double bwd = 0;
                if (bwCrv != null) { bw = bwCrv.GetLength(); d = U.GetLength(); bwd = bw * d; }
                double rho = areaAs / bwd;

                if (double.IsPositiveInfinity(rho) || double.IsNegativeInfinity(rho))
                {
                    //AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Reinforcement ratio is invalid.");
                    //return;

                    rho = 0;
                }

                sectRho.Add(rho);

                double maxError = 100*((rhoMax-rho)/rhoMax);
                maxErrors.Add(maxError);
                repos;
                double minError = 100*(rho-rhoMin)/rhoMin;
                minErrors.Add(minError);

                rhoMaxs.Add(rhoMax);
                rhoMins.Add(rhoMin);
            }

            DA.SetDataList(0, sectRho);
            DA.SetDataList(1, maxErrors);
            DA.SetDataList(2, minErrors);
            DA.SetDataList(3, bwCrvs);
            DA.SetDataList(4, rhoMaxs);
            DA.SetDataList(5, rhoMins);

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