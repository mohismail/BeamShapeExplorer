using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using BeamShapeExplorer.DataTypes;


namespace BeamShapeExplorer
{
    public class CostCalculation : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public CostCalculation()
          : base("Total Cost", "Cost",
              "Calculated the total cost of the element according to the metric defined by user.",
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
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Total cost", "Cost", "Total cost of a concrete element", GH_ParamAccess.item);
            pManager.AddNumberParameter("Total Mass (kg)", "Mass", "Total mass (kg) of the concrete element", GH_ParamAccess.item);
            pManager.AddBrepParameter("Steel and concrete Breps", "breps", "Breps of the final steel and concrete components", GH_ParamAccess.list);

            ((IGH_PreviewObject)pManager[2]).Hidden = true;
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

            if (!DA.GetData(0, ref mp)) return;
            if (!DA.GetDataList(1, crvAg)) return;
            if (!DA.GetDataList(2, crvAs)) return;

            //Copy to each analysis plugin - extracts material properties from MP input
            double fc = mp.fC; double Ec = mp.EC; double ec = mp.eC; double rhoc = mp.rhoC; double EEc = mp.EEC;
            double fy = mp.fY; double Es = mp.ES; double es = mp.eS; double rhos = mp.rhoS; double EEs = mp.EES;

            List<Brep> brepBeam = new List<Brep>();

            Curve ref_crvAs = crvAs[0];
            for(int i = 0; i < crvAs.Count; i++)
            {
                Curve crv_copy = crvAs[i].DuplicateCurve();
                if(crv_copy != null && Curve.DoDirectionsMatch(crv_copy, ref_crvAs) != true)
                {
                    crv_copy.Reverse();
                    crv_copy.Simplify(CurveSimplifyOptions.All, DocumentTolerance(), DocumentAngleTolerance());
                    crvAs[i] = crv_copy;
                }
            }

            Curve ref_crvAg = crvAg[0];
            for (int i = 0; i < crvAg.Count; i++)
            {
                Curve crv_copy = crvAg[i].DuplicateCurve();
                if (crv_copy != null && Curve.DoDirectionsMatch(crv_copy, ref_crvAg) != true)
                {
                    crv_copy.Reverse();
                    crv_copy.Simplify(CurveSimplifyOptions.All, DocumentTolerance(), DocumentAngleTolerance());
                    crvAg[i] = crv_copy;
                }
            }


            Brep[] brepC = Brep.CreateFromLoft(crvAg, Point3d.Unset, Point3d.Unset, LoftType.Tight, false);
            Brep clsBrepC = brepC[0].CapPlanarHoles(DocumentTolerance()); brepBeam.Add(clsBrepC);
            double massC = Math.Abs(clsBrepC.GetVolume()) * rhoc;
            double totEEc = Math.Abs(massC * EEc);

            Brep[] brepS = Brep.CreateFromLoft(crvAs, Point3d.Unset, Point3d.Unset, LoftType.Normal, false);
            Brep clsBrepS = brepS[0].CapPlanarHoles(DocumentTolerance()); brepBeam.Add(clsBrepS);
            double massS = Math.Abs(clsBrepS.GetVolume()) * rhos;
            double totEEs = Math.Abs(massS * EEs);

            double totVol = clsBrepS.GetVolume() + clsBrepC.GetVolume();
            double totMass = massC + massS;
            double totEE = totEEc + totEEs;

            DA.SetData(0, totEE);
            DA.SetData(1, totMass);
            DA.SetDataList(2, brepBeam);
            //DA.SetDataList(2, TESTbrepC);

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
                return BeamShapeExplorer.Properties.Resources.ee2;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("675c4a18-52d8-450f-8e88-48785815f54f"); }
        }
    }
}