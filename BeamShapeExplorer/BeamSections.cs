using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

using BeamShapeExplorer.DataTypes;

namespace BeamShapeExplorer
{
    public class BeamSections : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public BeamSections()
          : base("Beam Sections", "BS",
              "Extracts steel and concrete sections from given input",
              "Beam Shape Explorer", "Analysis")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //pManager.AddNumberParameter("Material Properties", "MP", "Properties for steel and concrete materials", GH_ParamAccess.list);
            pManager.AddGenericParameter("Material Properties", "MP", "Properties for steel and concrete materials", GH_ParamAccess.item);

            pManager.AddNumberParameter("Moment Envelope (kN-m)", "Mu", "Applied moments (kN-m) along length of element", GH_ParamAccess.list, 15);

            pManager.AddBrepParameter("Beam BRep", "BBrep", "Closed BRep representing the volume of a beam", GH_ParamAccess.item);
            pManager.AddCurveParameter("Span Curve", "SpnCrv", "Curve representing the full span on the beam", GH_ParamAccess.item);

            //pManager.AddNumberParameter("Distance Between Sections (m)", "n", "Distance between sections for analysis", GH_ParamAccess.item, 0.25);
            //pManager.AddIntegerParameter("Number of Segments", "n", "Number of segments along length of element extracted for analysis", GH_ParamAccess.item, 10);
            pManager.AddNumberParameter("Clear Cover (mm)", "cc", "An initial estimate for the steel clear cover (mm)", GH_ParamAccess.item, 30);
            pManager.AddNumberParameter("Area of Steel (mm2)", "As", "Area of tensile reinforcing steel; if blank, component will estimate based on moment envelope", GH_ParamAccess.item, 0);

            pManager.AddBooleanParameter("Constant Area of Steel", "Constant As", "True if the beam will have a constant area of steel", GH_ParamAccess.item, false);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Concrete Sections", "Ag", "Concrete beam sections for analysis", GH_ParamAccess.list);
            pManager.AddCurveParameter("Steel Reinforcement Sections", "As", "Steel Reinforcement sections for analysis", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            //List<double> MP = new List<double>();
            MaterialProperties mp = null;

            List<double> Mu = new List<double>();
            Brep BBrep = new Brep();
            Curve spCrv = null;
            //int N = 0;
            double CC = 0;
            double selAs = 0;
            bool fix = false;

     
            int building_code = 0; string bc = null;

            GH_SettingsServer BCsettings = new GH_SettingsServer("BSEBuildingCode", true);
            building_code = BCsettings.GetValue("CodeNumber", building_code);
            bc = BCsettings.GetValue("CodeName", bc); ;


            if (!DA.GetData(0, ref mp)) return;
            if (!DA.GetDataList(1, Mu)) return;
            if (!DA.GetData(2, ref BBrep)) return;
            if (!DA.GetData(3, ref spCrv)) return;
            //if (!DA.GetData(4, ref N)) return;
            if (!DA.GetData(4, ref CC)) return;
            if (!DA.GetData(5, ref selAs)) return;
            if (!DA.GetData(6, ref fix)) return;

            //MaterialProperties mp = null;
            //try
            //{
            //    mp = (MaterialProperties)mpobj;
            //}
            //catch (Exception e)
            //{
            //    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Must provide a valid Material Properties object");
            //    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.ToString());
            //    return;
            //}

            int N = Mu.Count;

            if (N < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input Mu needs to have at least 2 values");
                return;
            }


            //if (N > spCrv.GetLength() * 0.5)
            //{
            //    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input n must be less than half of the beam's length");
            //    return;
            //}

            //Copy to each analysis plugin - extracts material properties from MP input
            //double fc = MP[0]; double Ec = MP[1]; double ec = MP[2]; double rhoc = MP[3]; double EEc = MP[4];
            //double fy = MP[5]; double Es = MP[6]; double es = MP[7]; double rhos = MP[8]; double EEs = MP[9];

            double fc = mp.fC; double Ec = mp.EC; double ec = mp.eC; double rhoc = mp.rhoC; double EEc = mp.EEC;
            double fy = mp.fY; double Es = mp.ES; double es = mp.eS; double rhos = mp.rhoS; double EEs = mp.EES;

            //double fc = mp.FC;

            
            //Code limits for design
            double cdMax, B1, rhoMax=0, rhoMin = 0;
            double rhoDes, sConst = 0;

       
            cdMax = ec / (ec + es);
            if (building_code == 0)
            {
                rhoMax = (0.36 * fc / (0.87 * fy)) * cdMax; //Indian NBC 
            }
            else if (building_code == 1)
            {
                B1 = 0.85 - (0.05 * ((fc - 28) / 7)); //Calculate Beta_1 due to change of concrete strength
                rhoMax = (0.85 * fc / (fy)) * B1 * cdMax; //ACI-318 Code
            }
            else


            rhoMin = Math.Max(0.25 * Math.Sqrt(fc) / fy, 1.4/fy);

            //Steel design constants //
            rhoDes = 0.66 * rhoMax;
            sConst = 0.87 * fy * (1 - 1.005 * rhoDes * (fy / fc));
      
            
            
            Console.Write(sConst);

            //Extract beam sections
            BoundingBox boundBBrep = BBrep.GetBoundingBox(false);

            Curve edge0 = spCrv;
            Double[] edge0div = edge0.DivideByCount(N - 1, true);
            //Double[] edge0div = edge0.DivideByLength(N, true);
            Plane[] splitPls = edge0.GetPerpendicularFrames(edge0div);

            List<Curve> AgCrv = new List<Curve>();
            List<Curve> AsCrv = new List<Curve>();
            double[] AsCrvArea = new double[splitPls.Length];

            List<Point3d> origins = new List<Point3d>();
            List<Plane> planesAg = new List<Plane>();

            //for testing with single moment at all points
            double[] newMu = new double[splitPls.Length];
            for (int i = 0; i < splitPls.Length; i++) { newMu[i] = Mu[0]; } 
            if (Mu.Count == splitPls.Length) { newMu = Mu.ToArray(); }

            CurveSimplifyOptions crvSimp = new CurveSimplifyOptions();

            for (int i = 0; i < splitPls.Length; i++)
            {
                Curve[] ag = Brep.CreateContourCurves(BBrep, splitPls[i]);
                Brep[] brepAg = Brep.CreatePlanarBreps(ag, DocumentTolerance());
                Surface srfAg = brepAg[0].Faces[0];

                Curve ag0 = ag[0].Simplify(CurveSimplifyOptions.All, DocumentTolerance(), DocumentAngleTolerance());

                AgCrv.Add(ag0);

                Double uSrfC = srfAg.Domain(0)[1] - srfAg.Domain(0)[0];
                Double vSrfC = srfAg.Domain(1)[1] - srfAg.Domain(1)[0];

                Curve U = srfAg.IsoCurve(0, 0.5 * vSrfC + srfAg.Domain(1)[0]);
                Curve V = srfAg.IsoCurve(1, 0.5 * uSrfC + srfAg.Domain(0)[0]);

                Point3d endPtV = V.PointAtEnd; Point3d startPtV = V.PointAtStart;
                if (endPtV.Z < startPtV.Z) { V.Reverse(); }
                if (newMu[i] > DocumentTolerance()) { V.Reverse(); }

                srfAg.TryGetPlane(out Plane plAg);

                double d = V.GetLength() - (CC / 1000);
                double areaAs = 0;

                if (selAs == 0)
                {
                    areaAs = (1000000 * Math.Abs(newMu[i])) / (sConst * d * 1000); //As in mm2
                }
                else { areaAs = selAs; }
                
                double radAs = Math.Sqrt(areaAs / Math.PI) / 1000; //radius in m

                if (radAs <= DocumentTolerance()) { radAs = DocumentTolerance(); } //insert correction for when radius is equal to 0

                Point3d origin = V.PointAtLength(CC / 1000);
                origins.Add(origin);
                planesAg.Add(plAg);

                Circle crcAs = new Circle(plAg, origin, radAs);
                Curve crvAs = NurbsCurve.CreateFromCircle(crcAs);

                AsCrv.Add(crvAs);
                AsCrvArea[i] = areaAs;
            }


            double startAg0 = 0;
            AgCrv[0].ClosestPoint(new Point3d(0, 0, 0), out startAg0);
            AgCrv[0].ChangeClosedCurveSeam(startAg0);
            double startAs0 = 0;
            AgCrv[0].ClosestPoint(new Point3d(0, 0, 0), out startAs0);
            AgCrv[0].ChangeClosedCurveSeam(startAs0);

            double newStartAg = 0;
            Point3d startAg = new Point3d();
            double newStartAs = 0;
            Point3d startAs = new Point3d();

            for (int i = 1; i < AgCrv.Count; i++)
            {
                if (AgCrv[i] != null && AgCrv[i - 1] != null)
                {
                    //if (!Curve.DoDirectionsMatch(AgCrv[i], AgCrv[0])) { AgCrv[i].Reverse(); }
                    startAg = AgCrv[i - 1].PointAtStart;
                    AgCrv[i].ClosestPoint(startAg, out newStartAg);
                    AgCrv[i].ChangeClosedCurveSeam(newStartAg);
                }

                if (AsCrv[i] != null && AsCrv[i - 1] != null)
                {
                    //if (!Curve.DoDirectionsMatch(AsCrv[i], AsCrv[0])) { AsCrv[i].Reverse(); }
                    startAs = AsCrv[i - 1].PointAtStart;
                    AsCrv[i].ClosestPoint(startAs, out newStartAs);
                    AsCrv[i].ChangeClosedCurveSeam(newStartAs);
                }
            }


            List<Curve> finalAsCrv = new List<Curve>();

            Curve[] AsCrvArray = AsCrv.ToArray();
            Array.Sort(AsCrvArea, AsCrvArray);
            double AsCrvAreaMax = AsCrvArea[AsCrvArea.Length - 1];
            double radAsMax = Math.Sqrt(AsCrvAreaMax / Math.PI) / 1000; //radius in m
            Curve AsCrvMax = AsCrvArray[AsCrvArray.Length - 1];

            if(fix != false)
            {
                for(int i = 0; i < AsCrv.Count; i++)
                {
                    Circle newCrcAs = new Circle(planesAg[i], origins[i], radAsMax);
                    Curve newCrvAs = NurbsCurve.CreateFromCircle(newCrcAs);
                    finalAsCrv.Add(newCrvAs);
                }
            }
            else { finalAsCrv = AsCrv; }

            if (finalAsCrv.Count == 0 || AgCrv.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "One or both outputs are empty, check input loads and geometry!");
                return;
            }

            if (finalAsCrv.Contains(null))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "As is Null!");
                return;
            }

            //Curve ref_crvAs = finalAsCrv[0];
            //for (int i = 0; i < finalAsCrv.Count; i++)
            //{
            //    Curve crv_copy = finalAsCrv[i].DuplicateCurve();
            //    if (crv_copy != null && Curve.DoDirectionsMatch(crv_copy, ref_crvAs) != true)
            //    {
            //        crv_copy.Reverse();
            //        crv_copy.Simplify(CurveSimplifyOptions.All, DocumentTolerance(), DocumentAngleTolerance());
            //        finalAsCrv[i] = crv_copy;
            //    }
            //}

            DA.SetDataList(0, AgCrv);
            DA.SetDataList(1, finalAsCrv);

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
                return BeamShapeExplorer.Properties.Resources.beamsct;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("892caa27-e9cd-4bf1-82a5-eebb2d862092"); }
        }
    }
}