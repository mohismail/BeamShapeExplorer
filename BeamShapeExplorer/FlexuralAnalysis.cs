using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using BeamShapeExplorer.DataTypes;


// In order to load the result of this wizard, you will also need to
// add the output bin/ folder of this project to the list of loaded
// folder in Grasshopper.
// You can use the _GrasshopperDeveloperSettings Rhino command for that.

namespace BeamShapeExplorer
{
    public class FlexuralAnalysis : GH_Component
    {
        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public FlexuralAnalysis()
          : base("Flexural Analysis", "FA",
              "Analyzes beam sections for flexural capacity",
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

            pManager.AddCurveParameter("Concrete Section", "Ag", "Concrete sections to analyze for flexural capacity", GH_ParamAccess.list);
            pManager.AddCurveParameter("Steel Section", "As", "Steel sections to analyze to flesural capacity", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Section subdivisions", "m", "Number of cuts to integrate along section depth, defaults to 10", GH_ParamAccess.item, 15);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Moment Capacity (kN-m)", "Mn", "Moment Capacity (kN-m)", GH_ParamAccess.list);
            //pManager.AddNumberParameter("Moment Capacity Overdesign (%)", "%error", "Percent error of moment capacity, negative if capacity has not met demand", GH_ParamAccess.list);
            pManager.AddBrepParameter("Compression Area", "Ac", "Area of concrete in compression block, Ac", GH_ParamAccess.list);

        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            MaterialProperties mp = null;
            List<double> Mu = new List<double>();
            List<Curve> crvAg = new List<Curve>();
            List<Curve> crvAs = new List<Curve>();
            int M = 0;

            if (!DA.GetData(0, ref mp)) return;
            if (!DA.GetDataList(1, Mu)) return;
            if (!DA.GetDataList(2, crvAg)) return;
            if (!DA.GetDataList(3, crvAs)) return;
            if (!DA.GetData(4, ref M)) return;

            //Copy to each analysis plugin - extracts material properties from MP input
            double fc = mp.fC; double Ec = mp.EC; double ec = mp.eC; double rhoc = mp.rhoC; double EEc = mp.EEC;
            double fy = mp.fY; double Es = mp.ES; double es = mp.eS; double rhos = mp.rhoS; double EEs = mp.EES;

            if (M <= 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input m must be an integer greater than 2");
                return;
            }

            Brep[] brepsAg = Brep.CreatePlanarBreps(crvAg, DocumentTolerance()); //Creates planar Breps from input curves
            Brep[] brepsAs = Brep.CreatePlanarBreps(crvAs, DocumentTolerance());

            List<Brep> allSubSrf = new List<Brep>();
            List<Brep> finalSubSrf = new List<Brep>();
            List<Double> finalSubSrfArea = new List<Double>();
            List<double> Mn = new List<double>();
            List<double> errors = new List<double>();


            //Code limits for design
            double cdMax = ec / (ec + es);
            double rhoMax = (0.36 * fc / (0.87 * fy)) * cdMax;
            double rhoMin = 0.25 * Math.Sqrt(fc) / fy;

            //Steel design constants
            double rhoDes = 0.66 * rhoMax;
            double sConst = 0.87 * fy * (1 - 1.005 * rhoDes * (fy / fc));

            //for testing with single moment at all points
            double[] newMu = new double[crvAg.Count];
            for (int i = 0; i < crvAg.Count; i++) { newMu[i] = Mu[0]; }
            if (Mu.Count == crvAg.Count) { newMu = Mu.ToArray(); }



            for (int i = 0; i < crvAg.Count; i++)
            {
                Brep brepAg = brepsAg[i];
                Brep brepAs = new Brep();
                brepAs = brepsAs[i];

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
                if(endPtV.Z > startPtV.Z) { V.Reverse(); }
                if (newMu[i] > DocumentTolerance()) { V.Reverse(); }

                Double[] vDivision = V.DivideByCount(M, false);
                Plane[] vPlane = V.GetPerpendicularFrames(vDivision);

                double desiredArea = brepAs.GetArea() * 0.87 * fy / (0.36 * fc); //Adjust to include formula for material properties

                List<Brep> subSrf = new List<Brep>();

                for (int j = 0; j < vPlane.Length; j++) //Needs a fix for when m is 1 or less
                {
                    Curve[] contour = Brep.CreateContourCurves(brepAg, vPlane[j]);
                    Brep[] splitSrfs = brepAg.Split(contour, DocumentTolerance());
                    if (splitSrfs.Length > 0 && splitSrfs.Length > 1) { subSrf.Add(splitSrfs[1]);
                        allSubSrf.Add(splitSrfs[1]); }
                }

                double subSrfArea = 0;
                List<Brep> potentialSubSrf = new List<Brep>();

                Brep compAg = new Brep();
                int check = 0;

                for (int j = 0; j < subSrf.Count; j++)
                {
                    Surface subSrf2 = subSrf[j].Surfaces[0];
                    subSrfArea = AreaMassProperties.Compute(subSrf[j]).Area;

                    if ((subSrfArea > desiredArea) && (check == 0))
                    {
                        compAg = subSrf[j]; 
                        check++;
                    }
                }

                finalSubSrf.Add(compAg);
                finalSubSrfArea.Add(compAg.GetArea());

                Point3d centCompAg = new Point3d();
                Point3d centBrepAs = new Point3d();

                if (compAg.IsValid)
                {
                    centCompAg = AreaMassProperties.Compute(compAg).Centroid;
                    centBrepAs = AreaMassProperties.Compute(brepAs).Centroid;
                }

                double As = brepAs.GetArea();
                //double Z = centBrepAs.DistanceTo(centCompAg);
                double Z = centBrepAs.Z - centCompAg.Z;
                double T = 0.87 * As * fy * 1000; //ADD NEGATIVE ACCOMODATION...
                double sectMn = T * Z;
                Mn.Add(sectMn);

                double error = ((sectMn - newMu[i]) / newMu[i]) * 100;
                errors.Add(error);

            }

            DA.SetDataList(0, Mn);
            //DA.SetDataList(1, errors);
            DA.SetDataList(1, finalSubSrf);

        }

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                // You can add image files to your project resources and access them like this:
                //return Resources.IconForThisComponent;
                return BeamShapeExplorer.Properties.Resources.flex;
            }
        }

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("58cba816-4fb0-4d59-a79f-d9c4eaf7304f"); }
        }
    }
}
/// hello im nebyu