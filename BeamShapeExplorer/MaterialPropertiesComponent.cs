using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

using BeamShapeExplorer.DataTypes;

namespace BeamShapeExplorer
{

    public class MaterialPropertiesComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public MaterialPropertiesComponent()
          : base("Material Properties", "MP",
              "Compiles material properties for structural analysis",
              "Beam Shape Explorer", "Utilities")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Concrete Characteristic Strength (MPa, N/mm2)", "f'c", "Characteristic strength of concrete (MPa, N/mm2)", GH_ParamAccess.item, 40);
            pManager.AddNumberParameter("Concrete E-modulus (MPa, N/mm2)", "Ec", "Characteristic Young's modulus of concrete (MPa, N/mm2)", GH_ParamAccess.item, 27500);
            pManager.AddNumberParameter("Concrete Maximum Strain (mm/mm)", "εc", "Maximum strain allowed in concrete (mm/mm)", GH_ParamAccess.item, 0.0035);
            pManager.AddNumberParameter("Concrete Density (kg/m3)", "ρc", "Density of concrete (kg/m3)", GH_ParamAccess.item, 2400);
            pManager.AddNumberParameter("Concrete Embodied Energy Coefficient (MJ/kg)", "EEc", "Embodied energy coefficient of concrete (MJ/kg)", GH_ParamAccess.item, 0.87);

            pManager.AddNumberParameter("Steel Characteristic Strength (MPa, N/mm2)", "fy", "Characteristic strength of longitudinal reinforcement (MPa, N/mm2)", GH_ParamAccess.item, 415);
            pManager.AddNumberParameter("Steel E-modulus (MPa, N/mm2)", "Es", "Characteristic Young's modulus of steel (MPa, N/mm2)", GH_ParamAccess.item, 205000);
            pManager.AddNumberParameter("Steel Maximum Strain (mm/mm)", "εs", "Maximum strain allowed in steel (mm/mm)", GH_ParamAccess.item, 0.004);
            pManager.AddNumberParameter("Steel Density (kg/m3)", "ρs", "Density of steel (kg/m3)", GH_ParamAccess.item, 8050);
            pManager.AddNumberParameter("Steel Embodied Energy Coefficient (MJ/kg)", "EEs", "Embodied energy coefficient of steel (MJ/kg)", GH_ParamAccess.item, 30);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Material Properties", "MP", "Compiled list of material properties", GH_ParamAccess.item);
            //pManager.AddTextParameter("Material Property Information", "MP_Info", "Text summary of material properties", GH_ParamAccess.list);
            //pManager.AddGenericParameter("MPObject", "MPObj", "asdadssa", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double fc = 0; double Ec = 0; double ec = 0; double rhoc = 0; double EEc = 0;
            double fy = 0; double Es = 0; double es = 0; double rhos = 0; double EEs = 0;

            if (!DA.GetData(0, ref fc)) return;
            if (!DA.GetData(1, ref Ec)) return;
            if (!DA.GetData(2, ref ec)) return;
            if (!DA.GetData(3, ref rhoc)) return;
            if (!DA.GetData(4, ref EEc)) return;

            if (!DA.GetData(5, ref fy)) return;
            if (!DA.GetData(6, ref Es)) return;
            if (!DA.GetData(7, ref es)) return;
            if (!DA.GetData(8, ref rhos)) return;
            if (!DA.GetData(9, ref EEs)) return;

            List<double> MP = new List<double>()
            { fc, Ec, ec, rhoc, EEc,
              fy, Es, es, rhos, EEs };

            List<string> info = new List<string>()
            {
                "fc = " + MP[0] + " MPa",
                "Ec = " + MP[1] + " MPa",
                "εc = " + MP[2] + " mm/mm",
                "ρc = " + MP[3] + " kg/m3",
                "EEc = " + MP[4] + " MJ/kg",
                "fy = " + MP[5] + " MPa",
                "Es = " + MP[6] + " MPa",
                "εs = " + MP[7] + " mm/mm",
                "ρs = " + MP[8] + " kg/m3",
                "EEs " + MP[9] + " MJ/kg"
            };

            //DA.SetDataList(0, MP);
            //DA.SetDataList(1, info);

            MaterialProperties MPObj = new MaterialProperties(fc, Ec, ec, rhoc, EEc, fy, Es, es, rhos, EEs);
            DA.SetData(0, MPObj);


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
                return BeamShapeExplorer.Properties.Resources.mpcomp;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("030fb3d2-0ced-433a-b5e0-0ede01f7c301"); }
        }
    }
}