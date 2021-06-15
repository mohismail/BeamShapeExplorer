using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using BeamShapeExplorer.DataTypes;


namespace BeamShapeExplorer
{
    public class SelfWeightEnvelope : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public SelfWeightEnvelope()
          : base("Load Envelope from Selfweight", "LEw",
              "Provides moment and shear envelope for a beam from selfweight",
              "Beam Shape Explorer", "Loads")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Material Properties", "MP", "Properties for steel and concrete materials", GH_ParamAccess.item);
            pManager.AddBrepParameter("Beam BRep", "BBrep", "Closed BRep representing the volume of a beam", GH_ParamAccess.item);
            pManager.AddCurveParameter("Span Curve", "SpnCrv", "Curve representing the full span on the beam", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Number of Analysis Sections", "n", "Number of analysis sections along length of element", GH_ParamAccess.item, 10);
            pManager.AddNumberParameter("Graph Scale", "scale", "Scaling factor for graph visualization", GH_ParamAccess.item, 0.1);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Distributed Load Envelope (kN/m)", "qw", "Load envelope (kN/m) for selfweight", GH_ParamAccess.list);
            pManager.AddCurveParameter("Split Curve", "qwCrv", "Split curve allowing for line load application in Karamba", GH_ParamAccess.list);

            //pManager.AddVectorParameter("Point Load Envelope (kN)", "Pw", "Equivalent point loads (kN) for selfweight", GH_ParamAccess.list);
            //pManager.AddCurveParameter("Split Curve", "PwCrv", "Split curve allowing for point load application in Karamba", GH_ParamAccess.list);
            //pManager.AddPointParameter("Point Loading", "PwPts", "Points locations for equivalent point loads along curve", GH_ParamAccess.list);

            //pManager.AddNumberParameter("Shear Envelope (kN)", "Vw", "Shear envelope for selfweight (kN)", GH_ParamAccess.list);
            //pManager.AddNumberParameter("Moment Envelope (kN-m)", "Mw", "Moment envelope for selfweight (kN-m)", GH_ParamAccess.list);

            pManager.AddCurveParameter("Distributed Load Graph (Actual)", "Actual Load", "Self weight distribution curve", GH_ParamAccess.list);
            pManager.AddCurveParameter("Distributed Load Graph (Average)", "Average Load", "Self weight distribution curve", GH_ParamAccess.list);
            pManager.AddBrepParameter("Element Segments", "Segment", "Segments of element extracted for self weight calculation", GH_ParamAccess.list);

            ((IGH_PreviewObject)pManager[2]).Hidden = true;
            ((IGH_PreviewObject)pManager[3]).Hidden = true;
            ((IGH_PreviewObject)pManager[4]).Hidden = true;
            
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            MaterialProperties mp = null;
            Brep BBrep = new Brep();
            Curve spCrv = null;
            int N = 0;
            double scale = 0;

            if (!DA.GetData(0, ref mp)) return;
            if (!DA.GetData(1, ref BBrep)) return;
            if (!DA.GetData(2, ref spCrv)) return;
            if (!DA.GetData(3, ref N)) return;
            if (!DA.GetData(4, ref scale)) return;


            if (N < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input n must be an integer greater than 1");
                return;
            }

            //Copy to each analysis plugin - extracts material properties from MP input
            double fc = mp.fC; double Ec = mp.EC; double ec = mp.eC; double rhoc = mp.rhoC; double EEc = mp.EEC;
            double fy = mp.fY; double Es = mp.ES; double es = mp.eS; double rhos = mp.rhoS; double EEs = mp.EES;

            if (spCrv.PointAtStart.X > spCrv.PointAtEnd.X) { spCrv.Reverse(); }
            //double tStart = 0;
            //spCrv.ClosestPoint(spCrv.PointAtEnd, out tStart);

            //Extract perpendicular planes to segment Brep
            Double[] spCrvDiv = spCrv.DivideByCount(N, false);
            Plane[] splitPls = spCrv.GetPerpendicularFrames(spCrvDiv);

            double L = spCrv.GetLength();
            double dl = L / (N);

            BoundingBox boundBBrep = BBrep.GetBoundingBox(Plane.WorldXY);
            List<Brep> splitPlns = new List<Brep>();
            List<Brep> splitBBreps = new List<Brep>();
            Brep smallBBrep = BBrep.DuplicateBrep();
            Brep add = new Brep();

            List<double> qw = new List<double>();
            List<Point3d> qwPts = new List<Point3d>();
            List<Point3d> spCrvPts = new List<Point3d>();


            //qw.Add(0);
            //qwPts.Add(spCrv.PointAtStart);

            for (int i = 0; i < splitPls.Length; i++)
            {
                PlaneSurface plSrf = PlaneSurface.CreateThroughBox(splitPls[i], boundBBrep);
                splitPlns.Add(plSrf.ToBrep());
            }

            Brep[] splitBBrep = smallBBrep.Split(splitPlns, DocumentTolerance());
            double[] sortSplit = new double[splitBBrep.Length];

            if (splitBBrep[0] == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "BREP Split error");
                return;
            }


            for (int i = 0; i < splitBBrep.Length; i++)
            {
                splitBBrep[i] = splitBBrep[i].CapPlanarHoles(DocumentTolerance());
                double xLoc = VolumeMassProperties.Compute(splitBBrep[i]).Centroid.X;
                sortSplit[i] = xLoc;
            }

            Array.Sort(sortSplit, splitBBrep);

            //for (int i = 1; i <= splitPls.Length - 1; i++)
            //{
            //    PlaneSurface plSrf = PlaneSurface.CreateThroughBox(splitPls[i], boundBBrep);
            //    splitPlns.Add(plSrf.ToBrep());

            //    Brep[] split = smallBBrep.Split(plSrf.ToBrep(), DocumentTolerance());

            //    if (split.Length > 0)
            //    {
            //        if (split[0].GetVolume() < split[1].GetVolume()) 
            //        {
            //            add = split[0].CapPlanarHoles(DocumentTolerance());
            //            smallBBrep = split[1].CapPlanarHoles(DocumentTolerance());
            //        }
            //        else if (split[0].GetVolume() > split[1].GetVolume())
            //        {
            //            add = split[1].CapPlanarHoles(DocumentTolerance());
            //            smallBBrep = split[0].CapPlanarHoles(DocumentTolerance());
            //        }
            //        splitBBreps.Add(add); 
            //    }
            //    //foreach(Brep b in split) { splitBBreps.Add(b.CapPlanarHoles(DocumentTolerance())); }
            //}
            //qwPts.Add(spCrv.PointAtEnd);

            //Point3d qwPt0 = spCrv.PointAt(edge0div[0] * 0.5); qwPts.Add(qwPt0);

            for (int i = 0; i < splitBBrep.Length; i++)
            {
                Point3d pt = new Point3d();
                if (i < splitBBrep.Length - 1) {pt = spCrv.PointAt(spCrvDiv[i] + spCrvDiv[0] * 0.5); }
                else if (i == splitBBrep.Length - 1) { pt = spCrv.PointAt(spCrvDiv[0] * 0.5); }
                spCrvPts.Add(pt);
                double dqw = Math.Abs(splitBBrep[i].GetVolume()) * rhoc * 0.00980665 / dl;

                qw.Add(dqw);
                Point3d qwPt = new Point3d(pt.X, pt.Y, dqw * scale);
                qwPts.Add(qwPt);
            }

            List<Curve> graphActual = new List<Curve>();
            //Curve qwCrv = null;
            Curve qwCrv = Curve.CreateInterpolatedCurve(qwPts, 1);
            graphActual.Add(qwCrv);

            for (int i = 0; i < qwPts.Count; i++)
            {
                Line crv = new Line(qwPts[i], spCrvPts[i]);
                graphActual.Add(crv.ToNurbsCurve());
            }


            double qwSum = 0;
            foreach (double qwi in qw) { qwSum += qwi; }
            double qwAve = qwSum / qw.Count;

            List<Point3d> qwAvePts = new List<Point3d>();

            for (int i = 0; i < qw.Count; i++)
            {
                //Point3d pt = spCrv.PointAt((spCrvDiv[i] + spCrvDiv[1] * 0.5)); // spCrv.GetLength());
                Point3d pt = spCrvPts[i];
                Point3d qwAvePt = new Point3d(pt.X, pt.Y, qwAve * scale);
                qwAvePts.Add(qwAvePt);
            }

            List<Curve> graphAverage = new List<Curve>();
            Curve qwAveCrv = Curve.CreateInterpolatedCurve(qwAvePts, 1);
            graphAverage.Add(qwAveCrv);

            for (int i = 0; i < qwPts.Count; i++)
            {
                Line crv = new Line(qwAvePts[i], spCrvPts[i]);
                graphAverage.Add(crv.ToNurbsCurve());
            }

            List<Vector3d> Pw = new List<Vector3d>();
            foreach(double q in qw) { Pw.Add(new Vector3d(0, 0, -q * dl)); }

            //Brep[] splitBBrep = BBrep.Split(splitPlns, DocumentTolerance());

            Double[] PwSpCrvDiv = spCrv.DivideByCount(2 * N, true);
            Curve[] PwSpCrvSplit = spCrv.Split(PwSpCrvDiv);

            Double[] qwSpCrvDiv = spCrv.DivideByCount(N, true);
            Curve[] qwSpCrvSplit = spCrv.Split(qwSpCrvDiv);

            List<Curve> qwGraphActual = new List<Curve>();
            List<Curve> qwGraphAverage = new List<Curve>();
            Curve crvAct = null;
            Curve crvAve = null;
            for (int i = 0; i < qwSpCrvSplit.Length; i++)
            {
                Transform moveAct = Transform.Translation(0, 0, qw[i] * scale);
                Transform moveAve = Transform.Translation(0, 0, qwAve * scale);

                crvAct = qwSpCrvSplit[i].DuplicateCurve(); 
                crvAct.Transform(moveAct); 
                qwGraphActual.Add(crvAct);

                crvAve = qwSpCrvSplit[i].DuplicateCurve();
                crvAve.Transform(moveAve); 
                qwGraphAverage.Add(crvAve);
            }

            //Curve[] spCrvTEST = new Curve[1] { spCrv};

            for (int i = 0; i < qw.Count; i++) { qw[i] *= -1; }

            DA.SetDataList(0, qw);
            DA.SetDataList(1, qwSpCrvSplit);

            //DA.SetDataList(2, Pw);
            //DA.SetDataList(3, PwSpCrvSplit);
            //DA.SetDataList(4, spCrvPts);

            DA.SetDataList(2, qwGraphActual);
            DA.SetDataList(3, qwGraphAverage);
            DA.SetDataList(4, splitBBrep);

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
                return BeamShapeExplorer.Properties.Resources.loadself2;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("82752d64-9b2b-42ae-84d2-c2dce7f17025"); }
        }
    }
}