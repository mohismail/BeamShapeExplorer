using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using BeamShapeExplorer.DataTypes;


namespace BeamShapeExplorer
{
    public class MaterialPropertiesDeconstruct : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public MaterialPropertiesDeconstruct()
          : base("Deconstruct Material Properties", "DeconMP",
              "Extracts material properties from Material Properties object",
              "Beam Shape Explorer", "Utilities")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Material Properties", "MP", "Properties for steel and concrete materials", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Concrete Characteristic Strength (MPa, N/mm2)", "f'c", "Characteristic strength of concrete (MPa, N/mm2)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Concrete E-modulus (MPa, N/mm2)", "Ec", "Characteristic Young's modulus of concrete (MPa, N/mm2)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Concrete Maximum Strain (mm/mm)", "εc", "Maximum strain allowed in concrete (mm/mm)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Concrete Density (kg/m3)", "ρc", "Density of concrete (kg/m3)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Concrete Cost (unit/kg)", "CostC", "Cost per unit mass of concrete (unit/kg)", GH_ParamAccess.item);

            pManager.AddNumberParameter("Steel Characteristic Strength (MPa, N/mm2)", "fy", "Characteristic strength of longitudinal reinforcement (MPa, N/mm2)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Steel E-modulus (MPa, N/mm2)", "Es", "Characteristic Young's modulus of steel (MPa, N/mm2)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Steel Maximum Strain (mm/mm)", "εs", "Maximum strain allowed in steel (mm/mm)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Steel Density (kg/m3)", "ρs", "Density of steel (kg/m3)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Steel Cost(unit / kg)", "CostS", "Cost per unit mass of steel(unit / kg)", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            MaterialProperties mp = null;
            if (!DA.GetData(0, ref mp)) return;

            //Copy to each analysis plugin - extracts material properties from MP input
            double fc = mp.fC; double Ec = mp.EC; double ec = mp.eC; double rhoc = mp.rhoC; double EEc = mp.EEC;
            double fy = mp.fY; double Es = mp.ES; double es = mp.eS; double rhos = mp.rhoS; double EEs = mp.EES;

            DA.SetData(0, fc);
            DA.SetData(1, Ec);
            DA.SetData(2, ec);
            DA.SetData(3, rhoc);
            DA.SetData(4, EEc);

            DA.SetData(5, fy);
            DA.SetData(6, Es);
            DA.SetData(7, es);
            DA.SetData(8, rhos);
            DA.SetData(9, EEs);

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
                return BeamShapeExplorer.Properties.Resources.mpdec;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("8e738ed3-c784-4dbf-8103-fde6e058ef7b"); }
        }
    }
}