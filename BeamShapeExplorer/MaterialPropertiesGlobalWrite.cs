using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

using BeamShapeExplorer.DataTypes;

namespace BeamShapeExplorer
{
    public class MaterialPropertiesSetGlobal : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public MaterialPropertiesSetGlobal()
          : base("Material Properties Global", " Set MP",
              "Compiles global material properties for structural analysis",
              "Beam Shape Explorer", "Global Settings")
        {//Grasshopper.Instances.Settings.SetValue(bc);
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
            pManager.AddNumberParameter("Concrete Cost (unit/kg)", "CostC", "Cost per unit mass of concrete (unit/kg)", GH_ParamAccess.item, 0.87);

            pManager.AddNumberParameter("Steel Characteristic Strength (MPa, N/mm2)", "fy", "Characteristic strength of longitudinal reinforcement (MPa, N/mm2)", GH_ParamAccess.item, 415);
            pManager.AddNumberParameter("Steel E-modulus (MPa, N/mm2)", "Es", "Characteristic Young's modulus of steel (MPa, N/mm2)", GH_ParamAccess.item, 205000);
            pManager.AddNumberParameter("Steel Maximum Strain (mm/mm)", "εs", "Maximum strain allowed in steel (mm/mm)", GH_ParamAccess.item, 0.004);
            pManager.AddNumberParameter("Steel Density (kg/m3)", "ρs", "Density of steel (kg/m3)", GH_ParamAccess.item, 8050);
            pManager.AddNumberParameter("Steel Cost (unit/kg)", "CostS", "Cost per unit mass of steel (unit/kg)", GH_ParamAccess.item, 30);

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {

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

            GH_SettingsServer MPsettings = new GH_SettingsServer("MPInitial", false);
            MPsettings.Clear();
            MPsettings.SetValue("fc", fc);
            MPsettings.SetValue("Ec", Ec);
            MPsettings.SetValue("ec", ec);
            MPsettings.SetValue("rhoc", rhoc);
            MPsettings.SetValue("EEc", EEc);
            MPsettings.SetValue("fy", fy);
            MPsettings.SetValue("Es", Es);
            MPsettings.SetValue("es", es);
            MPsettings.SetValue("rhos", rhos);
            MPsettings.SetValue("EEs", EEs);
            MPsettings.WritePersistentSettings();
            //Grasshopper.Instances.Settings.SetValue(mp);

        }

        //DA.SetDataList(0, MP);
        //DA.SetDataList(1, info);



        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return BeamShapeExplorer.Properties.Resources.gmpcomp;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("dad22d45-4a16-4ed0-bf87-2473483f6bb1"); }
        }
    }
}