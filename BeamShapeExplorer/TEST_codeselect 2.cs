using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BeamShapeExplorer
{
    public class TEST_codeselect : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public TEST_codeselect()
          : base("Select Code 2", "Nickname",
              "Description",
              "Beam Shape Explorer", "TEST")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Code", "Code", "Code", GH_ParamAccess.item);

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("Code", "Code", "Code", GH_ParamAccess.item);
            pManager.AddTextParameter("Code", "Code", "Code", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int building_code = 0;
            
            if (!DA.GetData(0, ref building_code)) return;


            string bc = null;
            if (building_code == 0) { bc = "IS"; }
            else if (building_code == 1) { bc = "ACI"; }
            else { bc = "NULL"; }
            
            DA.SetData(0, building_code);
            DA.SetData(1, bc);

            GH_SettingsServer BCsettings = new GH_SettingsServer("BSEBuildingCode", false);
            BCsettings.Clear();
            BCsettings.SetValue("CodeNumber", building_code);
            BCsettings.SetValue("CodeName", bc);
            BCsettings.WritePersistentSettings();
            //Grasshopper.Instances.Settings.SetValue(bc);

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
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("aa7a2c4b-cfaa-4eed-bbaf-ef6a0b82c433"); }
        }
    }
}