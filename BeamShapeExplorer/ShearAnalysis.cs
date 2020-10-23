using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using BeamShapeExplorer.DataTypes;


namespace BeamShapeExplorer
{
    public class ShearAnalysis : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public ShearAnalysis()
          : base("Shear Analysis", "SA",
              "Analyzes beam sections for shear capacity",
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
            pManager.AddNumberParameter("Shear Envelope (kN)", "Vu", "Applied shear (kN) along length of element", GH_ParamAccess.list, 15);
            pManager.AddNumberParameter("Design Shear Reinforcement (kN)", "Vs", "Design shear resistance (kN) of reinforcement", GH_ParamAccess.list, 0);

            pManager.AddCurveParameter("Concrete Section", "Ag", "Concrete sections to analyze for flexural capacity", GH_ParamAccess.list);
            pManager.AddCurveParameter("Steel Section", "As", "Steel sections to analyze to flesural capacity", GH_ParamAccess.list);

            pManager.AddNumberParameter("Clear Cover (mm)", "cc", "An initial estimate for the steel clear cover (mm)", GH_ParamAccess.item, 30);

            pManager.AddIntegerParameter("Section subdivisions", "m", "Number of cuts to identify web width, bw, defaults to 15", GH_ParamAccess.item, 15);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Shear Capacity (kN)", "Vn", "Shear Capacity (kN)", GH_ParamAccess.list);
            //pManager.AddNumberParameter("Vc1Sect", "Vc1Sect", "Vc1Sect", GH_ParamAccess.list);
            //pManager.AddNumberParameter("Vc2Sect", "Vc2Sect", "Vc2Sect", GH_ParamAccess.list);
            //pManager.AddNumberParameter("Sectbw", "Sectbw", "Sectbw", GH_ParamAccess.list);
            //pManager.AddNumberParameter("Sectd", "Sectd", "Sectd", GH_ParamAccess.list);

            //pManager.AddNumberParameter("Shear Capacity Overdesign (%)", "%error", "Percent error of moment capacity, negative if capacity has not met demand", GH_ParamAccess.list);
            pManager.AddCurveParameter("bwCrv", "bwCrv", "bwCrv", GH_ParamAccess.list);

            ((IGH_PreviewObject)pManager[1]).Hidden = true;

            //pManager.AddCurveParameter("bw", "bw", "bw", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            

            MaterialProperties mp = null;
            List<double> Mu = new List<double>();
            List<double> Vu = new List<double>();
            List<double> Vs = new List<double>();
            List<Curve> crvAg = new List<Curve>();
            List<Curve> crvAs = new List<Curve>();
            double CC = 0;
            int M = 0;
            int building_code = 0;

            if (!DA.GetData(0, ref mp)) return;
            if (!DA.GetDataList(1, Mu)) return;
            if (!DA.GetDataList(2, Vu)) return;
            if (!DA.GetDataList(3, Vs)) return;
            if (!DA.GetDataList(4, crvAg)) return;
            if (!DA.GetDataList(5, crvAs)) return;
            if (!DA.GetData(6, ref CC)) return;
            if (!DA.GetData(7, ref M)) return;


            if (M <= 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input m must be an integer greater than 2");
                return;
            }

            //Copy to each analysis plugin - extracts material properties from MP input
            double fc = mp.fC; double Ec = mp.EC; double ec = mp.eC; double rhoc = mp.rhoC; double EEc = mp.EEC;
            double fy = mp.fY; double Es = mp.ES; double es = mp.eS; double rhos = mp.rhoS; double EEs = mp.EES;

            //Creates planar Breps from input curves
            Brep[] brepsAg = Brep.CreatePlanarBreps(crvAg, DocumentTolerance()); 
            Brep[] brepsAs = Brep.CreatePlanarBreps(crvAs, DocumentTolerance());

            //Mu correction, for testing with single moment at all points
            double[] newMu = new double[crvAg.Count];
            for (int i = 0; i < crvAg.Count; i++) { newMu[i] = Mu[0]; }
            if (Mu.Count == crvAg.Count) { newMu = Mu.ToArray(); }

            //Vu correction, for testing with single moment at all points
            double[] newVu = new double[crvAg.Count];
            for (int i = 0; i < crvAg.Count; i++) { newVu[i] = Vu[0]; }
            if (Vu.Count == crvAg.Count) { newVu = Vu.ToArray(); }

            //Vs correction, for testing with single moment at all points
            double[] newVs = new double[crvAg.Count];
            for (int i = 0; i < crvAg.Count; i++) { newVs[i] = Vs[0]; }
            if (Vs.Count == crvAg.Count) { newVs = Vs.ToArray(); }

            ////Mn correction, for testing with single moment at all points
            //double[] newMn = new double[crvAg.Count];
            //for (int i = 0; i < crvAg.Count; i++) { newMn[i] = Mn[0]; }
            //if (Mn.Count == crvAg.Count) { newMn = Mn.ToArray(); }

            List<Curve> bwCrvs = new List<Curve>();
            List<double> sectRho = new List<double>();
            List<double> Vn = new List<double>();
            List<double> Vc1Sect = new List<double>();
            List<double> Vc2Sect = new List<double>();
            List<double> Sectbw = new List<double>();
            List<double> Sectd = new List<double>();
            List<double> errors = new List<double>();


            for (int i = 0; i < crvAg.Count; i++)
            {
                Brep brepAg = brepsAg[i];
                Brep brepAs = brepsAs[i];

                Surface srfAg = brepAg.Surfaces[0];

                Double uMidDom = (srfAg.Domain(0)[1] + srfAg.Domain(0)[0]) / 2;
                Double vMidDom = (srfAg.Domain(1)[1] + srfAg.Domain(1)[0]) / 2;

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
                for(int j = 0; j < vPlane.Length; j++)
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

                double areaAs = AreaMassProperties.Compute(crvAs[i]).Area;
                double bw=0, d=0, bwd = 0;
                if (building_code == 0)
                {
                    bw = bwCrv.GetLength(); d = U.GetLength(); bwd = bw * d;
                }
                else if (building_code == 1)
                {
                    bw = bwCrv.GetLength(); d = (0.96 * V.GetLength()) - (CC / 1000); bwd = bw * d; //Jonathan
                }
                double rho = areaAs / bwd;

                sectRho.Add(rho);

                //Unit conversions for shear calculations
                bw *= 1000; d *= 1000;
                double sectMu = Math.Abs(newMu[i]) * 1000000 ; double sectVu = Math.Abs(newVu[i] * 1000) ;

                //IS456 shear calculation
                double beta = Math.Max(0.8 * fc / (6.89 * rho * 100), 1);
                double tauC = 0.85 * Math.Sqrt(0.8 * fc) * (Math.Sqrt(1 + 5 * beta) - 1) / (6 * beta);
                double sectVcIS = tauC * bw * d / 1000;

                //ACI shear calculation
                double Vc1 = (0.16 * Math.Sqrt(fc) + 17 * rho * sectVu * d / sectVu) * bw * d / 1000;
                double Vc2 = 0.29 * Math.Sqrt(fc) * bw * d / 1000;
                double sectVcACI = Math.Min(Vc1, Vc2);

                ////CFP shear calculation, WIP
                //double avx = Math.Abs(1000 * newMu[i] / newVu[i]);
                //double Mcx = 0.875 * avx * d * (0.342 * bw + 0.3 * (newMn[i] / (d * d)) * Math.Sqrt(z / avx)) * Math.Pow((16.66 / (rho * fy)), 0.25);
                //sectVcCFP = Mcx / (avx * 1000);

                double sectVn = 0;
                if (building_code == 0)
                {
                    if (Double.IsNaN(sectVn)) { sectVn = 0; }
                    else if (newVs[i] < DocumentTolerance()) { sectVn = Math.Min(sectVcIS, sectVcACI) * 0.5; }
                    else { sectVn = Math.Min(sectVcIS, sectVcACI) + newVs[i]; }
                }
                else if (building_code == 1)
                {
                    if (Double.IsNaN(sectVn)) { sectVn = 0; }
                    else if (newVs[i] < DocumentTolerance()) { sectVn = sectVcACI * 0.5; } //Jonathan
                    else { sectVn = sectVcACI + newVs[i]; } //Jonathan
                }

                Vn.Add(sectVn);
                //Jonathan
                if (building_code == 1)
                {
                    Vc1Sect.Add(Vc1);
                    Vc2Sect.Add(Vc2);
                    Sectbw.Add(bw);
                    Sectd.Add(d);
                }
                

                double error = ((sectVn - Math.Abs(newVu[i])) / Math.Abs(newVu[i])) * 100;
                errors.Add(error);


            }

            DA.SetDataList(0, Vn);
            //DA.SetDataList(1, errors);
            //DA.SetDataList(1, Vc1Sect);
            //DA.SetDataList(2, Vc2Sect);
            //DA.SetDataList(3, Sectbw);
            //DA.SetDataList(4, Sectd);
            DA.SetDataList(1, bwCrvs);

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
                return BeamShapeExplorer.Properties.Resources.shear;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("bb38b17e-4763-41c5-a72e-873ae585cebc"); }
        }
    }
}