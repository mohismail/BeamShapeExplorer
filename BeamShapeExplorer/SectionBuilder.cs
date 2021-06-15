using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using BeamShapeExplorer.DataTypes;


namespace BeamShapeExplorer
{
    public class SectionBuilder : GH_Component
    {
        internal List<Point3d> sctPts;
        internal Plane sctPln;

        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public SectionBuilder()
          : base("Section Builder", "SBuild",
              "Creates variable section for shaped element",
              "Beam Shape Explorer", "Design")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Width (m) of section, b ", "b", "Width (m) of section", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("Depth (m) of section, h", "h", "Depth (m) of section", GH_ParamAccess.item, 0.15);
            pManager.AddNumberParameter("Variable x-coefficients", "VPx", "Variable coefficients informing shape of section (0-1)", GH_ParamAccess.list, 1);
            pManager.AddNumberParameter("Variable y-coefficients", "VPy", "Variable coefficients informing shape of section (0-1)", GH_ParamAccess.list, 0);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("pts", "pts", "pts", GH_ParamAccess.list);
            pManager.AddCurveParameter("crv", "crv", "crv", GH_ParamAccess.item);

            ((IGH_PreviewObject)pManager[0]).Hidden = true;
            ((IGH_PreviewObject)pManager[1]).Hidden = true;

            pManager.AddGenericParameter("Variable Section", "VSect", "Variable section object", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double b = 0;
            double h = 0;
            List<double> VPx = new List<double>();
            List<double> VPy = new List<double>();

            if (!DA.GetData(0, ref b)) return;
            if (!DA.GetData(1, ref h)) return;
            if (!DA.GetDataList(2, VPx)) return;
            if (!DA.GetDataList(3, VPy)) return;

            if(VPx.Count == 1) { VPx.Add(1); }
            if (VPy.Count == 1) { VPy.Add(1); }

            Transform mirrorYZ = Transform.Mirror(Plane.WorldYZ);
            Transform mirrorXZ = Transform.Mirror(Plane.WorldZX);

            List<Point3d> sctPts = new List<Point3d>();
            List<Point3d> sctPts1 = new List<Point3d>();
            List<Point3d> sctPts2 = new List<Point3d>();

            Plane pln = Plane.WorldYZ;

            //Point3d ptA = pln.PointAt(-b * 0.5, 0); sctPts.Add(ptA);
            //Point3d ptB = pln.PointAt(-b * 0.5, -h); sctPts.Add(ptB);
            //Point3d ptC = ptB; ptC.Transform(mirrorXZ); sctPts.Add(ptC);
            //Point3d ptD = ptA; ptD.Transform(mirrorXZ); sctPts.Add(ptD);
            //sctPts.Add(ptA);

            if(VPx.Count < VPy.Count)
            {
                for(int i = VPx.Count; i < VPy.Count; i++) { VPx.Add(VPx[VPx.Count - 1]); }
            }
            if (VPx.Count > VPy.Count)
            {
                for (int i = VPy.Count; i < VPx.Count; i++) { VPy.Add(VPy[VPy.Count - 1]); }
            }

            b *= 0.5;

            double a1 = VPy[0]; double ptX = -VPx[0] * b; double ptY = -a1 * h;

            //Point3d ptT = pln.PointAt(-b, 0); sctPts1.Add(ptT); //fix to allow for variable width at top
                      
            Point3d ptM = pln.PointAt(ptX, ptY);
            sctPts1.Add(ptM);

            for (int i = 1; i < VPx.Count; i++)
            {
                double a2 = VPy[i];
                double a = (1 - a1) * a2 + a1;
                ptX = -b * VPx[i];
                ptY = -h * a;
                ptM = pln.PointAt(ptX, ptY);
                sctPts1.Add(ptM);
                a1 = a;
            }

            sctPts2.AddRange(sctPts1);
            for(int i = 0; i < sctPts2.Count; i++)
            {
                Point3d pt = sctPts2[i];
                pt.Transform(mirrorXZ);
                sctPts2[i] = pt;
            }
            sctPts2.Reverse();

            //foreach (Point3d pt in sctPts2) { pt.Transform(mirrorXZ); }
            //Point3d ptB = pln.PointAt(ptX, -h); sctPts1.Add(ptB);

            sctPts.AddRange(sctPts1);
            sctPts.AddRange(sctPts2);
            sctPts.Add(sctPts1[0]);
            Curve sctCrv = Curve.CreateInterpolatedCurve(sctPts, 1);

            VariableSection vSct = new VariableSection(pln, sctPts1);

            DA.SetDataList(0, sctPts1);
            DA.SetData(1, sctCrv);
            DA.SetData(2, vSct);
            
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
                return BeamShapeExplorer.Properties.Resources.sctbld;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("0acf76f9-fc80-44b4-9ea8-00642660dd38"); }
        }
    }
}