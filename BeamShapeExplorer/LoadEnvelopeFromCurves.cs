using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BeamShapeExplorer
{
    public class LoadEnvelopeFromCurves : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public LoadEnvelopeFromCurves()
          : base("Load Envelope from Curves", "LECrv",
              "Provides moment and shear envelope for a beam from the shear and moment diagram",
              "Beam Shape Explorer", "Configuration")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Shear Curve", "VCrv", "Curve representing the shear envelope along the beam", GH_ParamAccess.item);
            pManager.AddCurveParameter("Moment Curve", "MCrv", "Curve representing the moment envelope along the beam", GH_ParamAccess.item);

            pManager.AddIntervalParameter("Shear Domain", "VDom", "Domain representing the bounds of the shear envelope (kN-m)", GH_ParamAccess.item);
            pManager.AddIntervalParameter("Moment Domain", "MDom", "Domain representing the bounds of the moment envelope (kN-m)", GH_ParamAccess.item);

            pManager.AddCurveParameter("Span Curve", "SpnCrv", "Curve representing the full span on the beam", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Number of Analysis Sections", "n", "Number of analysis sections along length of element", GH_ParamAccess.item, 10);
           
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Shear Envelope (kN)", "Vu", "Shear envelope for applied loads (kN)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Moment Envelope (kN-m)", "Mu", "Moment envelope for applied loads (kN-m)", GH_ParamAccess.list);

            pManager.AddCurveParameter("Shear and moment graphs", "Graphs", "Shear and moment graph curves", GH_ParamAccess.list);
            ((IGH_PreviewObject)pManager[2]).Hidden = true;

            //pManager.AddPointParameter("pts", "pts", "pts", GH_ParamAccess.list);

        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve vCrv = null;
            Curve mCrv = null;
            Interval vDom = new Interval();
            Interval mDom = new Interval();
            Curve spCrv = null;
            int N = 0;

            if (!DA.GetData(0, ref vCrv)) return;
            if (!DA.GetData(1, ref mCrv)) return;
            if (!DA.GetData(2, ref vDom)) return;
            if (!DA.GetData(3, ref mDom)) return;
            if (!DA.GetData(4, ref spCrv)) return;
            if (!DA.GetData(5, ref N)) return;


            if (N < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input n must be an integer greater than 1");
                return;
            }

            double L = spCrv.GetLength();
            Double[] spCrvDiv = spCrv.DivideByCount(N - 1, true);
            Plane[] spCrvPl = spCrv.GetPerpendicularFrames(spCrvDiv);

            List<double> Vu = new List<double>();
            List<double> Mu = new List<double>();

            List<Point3d> spCrvPts = new List<Point3d>();
            List<Point3d> vPts = new List<Point3d>();
            List<Point3d> mPts = new List<Point3d>();

            List<Point3d> allPts = new List<Point3d>();

            for (int i = 0; i < spCrvPl.Length; i++)
            {
                Point3d spCrvPt = spCrv.PointAt(spCrvDiv[i]);
                spCrvPts.Add(spCrvPt);

                Rhino.Geometry.Intersect.CurveIntersections vInt = Rhino.Geometry.Intersect.Intersection.CurvePlane(vCrv, spCrvPl[i], DocumentTolerance());
                Point3d vPt = new Point3d();
                if (vInt != null) { vPt = vInt[0].PointA; }
                else { vPt = vPts[i - 1]; }
                vPts.Add(vPt);

                Rhino.Geometry.Intersect.CurveIntersections mInt = Rhino.Geometry.Intersect.Intersection.CurvePlane(mCrv, spCrvPl[i], DocumentTolerance());
                Point3d mPt = mInt[0].PointA;
                mPts.Add(mPt);

                allPts.Add(spCrvPt); allPts.Add(mPt); allPts.Add(vPt);
            }

            //if (mDom.IsDecreasing) { mDom.Swap(); }
            //if (vDom.IsDecreasing) { vDom.Swap(); }

            BoundingBox vBb = vCrv.GetBoundingBox(false);
            Point3d vBbMin = vBb.Min;
            Point3d vBbMax = vBb.Max;

            BoundingBox mBb = mCrv.GetBoundingBox(false);
            Point3d mBbMin = mBb.Min;
            Point3d mBbMax = mBb.Max;
            
            Interval mCrvDom = new Interval(mBbMin.Z, mBbMax.Z);
            Interval vCrvDom = new Interval(vBbMin.Z, vBbMax.Z);

            List<Point3d> newMPts = new List<Point3d>();
            List<Point3d> newVPts = new List<Point3d>();

            double scale = 1;

            for(int i =0; i < spCrvPts.Count; i++)
            {
                double vPtZ = vPts[i].Z - spCrvPts[i].Z;
                double vu = remapNum.Remap(vPtZ, vCrvDom, vDom);
                if (vu == 0) { vu += DocumentTolerance(); }
                Vu.Add(vu);

                double mPtZ = mPts[i].Z - spCrvPts[i].Z;
                double mu = remapNum.Remap(mPtZ, mCrvDom, mDom);
                if (mu == 0) { mu += DocumentTolerance(); }
                Mu.Add(mu);

                Point3d newVPt = new Point3d(spCrvPts[i].X, spCrvPts[i].Y, remapNum.Remap(vu,vDom,vCrvDom));
                Point3d newMPt = new Point3d(spCrvPts[i].X, spCrvPts[i].Y, remapNum.Remap(mu,mDom,mCrvDom));
                newVPts.Add(newVPt); newMPts.Add(newMPt);
            }

            List<Curve> graphs = new List<Curve>();
            Curve newVCrv = Curve.CreateInterpolatedCurve(newVPts, 1); graphs.Add(newVCrv);
            Curve newMCrv = Curve.CreateInterpolatedCurve(newMPts, 1); graphs.Add(newMCrv);

            DA.SetDataList(0, Vu);
            DA.SetDataList(1, Mu);
            DA.SetDataList(2, graphs);
            //DA.SetDataList(3, allPts);


        }

        public static class remapNum
        {
            public static double Remap(double num, Interval fromDom, Interval toDom)
            {
                double fromMin = fromDom.Min;
                double fromMax = fromDom.Max;
                double toMin = toDom.Min;
                double toMax = toDom.Max;
                
                if (num < fromMin)
                {
                    num = fromMin;
                }
                else if (num > fromMax)
                {
                    num = fromMax;
                }

                double fromAbs = num - fromMin;
                double fromMaxAbs = fromMax - fromMin;
                double normal = fromAbs / fromMaxAbs;
                double toMaxAbs = toMax - toMin;
                double toAbs = toMaxAbs * normal;
                double to = toAbs + toMin;

                return to;
            }
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
                return BeamShapeExplorer.Properties.Resources.loadcrv;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("f62c6ba0-acb9-4a5d-8eab-7d3da6e9a30e"); }
        }
    }
}