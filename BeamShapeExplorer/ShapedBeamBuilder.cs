using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using BeamShapeExplorer.DataTypes;


namespace BeamShapeExplorer
{
    public class ShapedBeamBuilder : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public ShapedBeamBuilder()
          : base("Beam Builder", "BBuild",
              "Creates Brep of beam shaped by variable inputs",
              "Beam Shape Explorer", "Utilities")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Span (m)", "Spn", "Span (m) of beam", GH_ParamAccess.item, 5);
            pManager.AddGenericParameter("Variable Section Objects", "VSect", "Variable sections along length of beam", GH_ParamAccess.list);
            //pManager.AddNumberParameter("Width (m) of section, b ", "b", "Width (m) of section", GH_ParamAccess.item, 1);
            //pManager.AddNumberParameter("Depth (m) of section, h", "h", "Depth (m) of section", GH_ParamAccess.item, 0.15);
            //pManager.AddNumberParameter("Variable coefficients", "VP", "Variable coefficients informing shape of section (0-1)", GH_ParamAccess.list, 0.5);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddPlaneParameter("Construction planes", "Pl", "Planes along element length used for brep construction", GH_ParamAccess.list);
            pManager.AddPointParameter("Variable Points", "Pts", "Variable points defining shaped element", GH_ParamAccess.list);
            pManager.AddCurveParameter("Variable Curves", "Crv", "Lofted curves resulting in shaped element", GH_ParamAccess.list);

            ((IGH_PreviewObject)pManager[0]).Hidden = true;
            ((IGH_PreviewObject)pManager[1]).Hidden = true;
            ((IGH_PreviewObject)pManager[2]).Hidden = true;

            pManager.AddBrepParameter("Beam Brep", "BBrep", "Brep representing geometry of a shaped element", GH_ParamAccess.item);
            pManager.AddCurveParameter("Span Curve", "SpCrv", "Curve representing span of shaped element", GH_ParamAccess.item);

        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double span = 0;
            List<VariableSection> vSect = new List<VariableSection>();

            if (!DA.GetData(0, ref span)) return;
            if (!DA.GetDataList(1, vSect)) return;

            int N = vSect.Count;

            if (N < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "VSect needs at least 2 inputs");
                return;
            }

            Transform mirrorYZ = Transform.Mirror(Plane.WorldYZ);
            Transform mirrorXZ = Transform.Mirror(Plane.WorldZX);

            Point3d spPtA = new Point3d(-span * 0.5, 0, 0);
            Curve spCrv = new Line(spPtA, Plane.WorldXY.XAxis, span).ToNurbsCurve();
            Curve spCrvHlf = new Line(spPtA, Plane.WorldXY.XAxis, span * 0.5).ToNurbsCurve();

            //if (N < 2) { N = 3; }
            double[] spCrvDiv = spCrv.DivideByCount(N - 1, true);
            Plane[] spCrvPln = spCrv.GetPerpendicularFrames(spCrvDiv);
            for (int i = 0; i < spCrvPln.Length; i++) //reorient planes to align with z-axis
            {
                Plane pl = spCrvPln[i];
                pl.Rotate(-Math.PI * 0.5, Plane.WorldXY.XAxis);
                spCrvPln[i] = pl;
            }

            List<Point3d> allPts = new List<Point3d>();
            List<Curve> crvs = new List<Curve>();

            int ptCnt = vSect[0].sctPts.Count;
            Point3d[,] crvPts = new Point3d[ptCnt, vSect.Count];
            //for(int i = 0; i < vSect.Count; i++) { Point3d[] crvPt = new Point3d[vSect.Count]; }


            for (int i = 0; i < vSect.Count; i++) //moves and reorients points from input i indicates number section, j number curve/point in section
            {
                List<Point3d> sctPts = vSect[i].sctPts;
                Plane sctPln = vSect[i].sctPln;

                if (sctPts.Count != ptCnt) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Each section needs the same number of points"); return; }

                Transform reorient = Transform.PlaneToPlane(sctPln, spCrvPln[i]);
                //foreach (Point3d pt in sctPts) { pt.Transform(reorient); allPts.Add(pt); } //remove when workaround is figured out

                for (int j = 0; j < sctPts.Count; j++) //separate points to each "row" in a list of arrays
                {
                    Point3d pt = sctPts[j];
                    pt.Transform(reorient);
                    crvPts[j, i] = pt;
                }
            }

            int degree = 3;

            for (int i = 0; i < crvPts.GetLength(0); i++) //creates curves through points
            {
                Point3d[] aCrvPts = new Point3d[vSect.Count];
                for (int j = 0; j < crvPts.GetLength(1); j++) { aCrvPts[j] = crvPts[i, j]; }
                Curve crv = Curve.CreateInterpolatedCurve(aCrvPts, degree);
                crvs.Add(crv);
            }

            List<Brep> faces = new List<Brep>();

            Brep[] loftFace = Brep.CreateFromLoft(crvs, Point3d.Unset, Point3d.Unset, LoftType.Straight, false);
            //Brep face1 = new Brep();
            //foreach(Brep face in loftFace) { face1 = face.DuplicateBrep(); }
            Brep face1 = loftFace[0].DuplicateBrep();
            Brep face2 = face1.DuplicateBrep(); face2.Transform(mirrorXZ);
            faces.Add(face1); faces.Add(face2);

            Curve[] naked1 = face1.DuplicateNakedEdgeCurves(true, false); Curve[] naked2 = face2.DuplicateNakedEdgeCurves(true, false);
            Curve[] loftCrv1 = new Curve[] { naked1[1], naked2[1] }; Curve[] loftCrv2 = new Curve[] { naked1[3], naked2[3] };
            Brep[] face3 = Brep.CreateFromLoft(loftCrv1, Point3d.Unset, Point3d.Unset, LoftType.Straight, false);
            Brep[] face4 = Brep.CreateFromLoft(loftCrv2, Point3d.Unset, Point3d.Unset, LoftType.Straight, false);
            faces.Add(face3[0]); faces.Add(face4[0]);

            Brep[] beamBreps = Brep.JoinBreps(faces, DocumentTolerance());
            Brep beam = beamBreps[0].CapPlanarHoles(DocumentTolerance());

            int intersects = 0;
            int a = crvs.Count;
            for (int i = 0; i < (a - 1); i++)
            {
                for (int j = (i + 1); j < a; j++)
                {
                    Rhino.Geometry.Intersect.CurveIntersections crvCheck = Rhino.Geometry.Intersect.Intersection.CurveCurve(
                        crvs[i], crvs[j], DocumentTolerance(), 0.0);

                    intersects += crvCheck.Count;
                }
            }

            if (intersects > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Beam volume is invalid.");
                return;
            }

            DA.SetDataList(0, spCrvPln);
            DA.SetDataList(1, crvPts);
            DA.SetDataList(2, crvs);
            DA.SetData(3, beam);
            DA.SetData(4, spCrv);

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
                return BeamShapeExplorer.Properties.Resources.beambld;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("c57680eb-8fde-49c3-9b57-6c9570336946"); }
        }
    }
}