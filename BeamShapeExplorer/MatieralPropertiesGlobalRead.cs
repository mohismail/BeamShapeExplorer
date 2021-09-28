using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

using BeamShapeExplorer.DataTypes;

namespace BeamShapeExplorer
{
    public class MaterialPropertiesReadGlobal : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public MaterialPropertiesReadGlobal()
         : base("Read MP", "Read MP",
              "Outputs global material properties for structural analysis",
              "Beam Shape Explorer", "Global Settings")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
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

            GH_SettingsServer MPsettings = new GH_SettingsServer("MPInitial", true);
            fc = MPsettings.GetValue("fc", fc);
            Ec = MPsettings.GetValue("Ec", Ec);
            ec = MPsettings.GetValue("ec", ec);
            rhoc = MPsettings.GetValue("rhoc", rhoc);
            EEc = MPsettings.GetValue("EEc", EEc);
            fy = MPsettings.GetValue("fy", fy);
            Es = MPsettings.GetValue("Es", Es);
            es = MPsettings.GetValue("es", es);
            rhos = MPsettings.GetValue("rhos", rhos);
            EEs = MPsettings.GetValue("EEs", EEs);



            List<double> MP = new List<double>()
            { fc, Ec, ec, rhoc, EEc,
              fy, Es, es, rhos, EEs };

            List<string> info = new List<string>()
            {
                "fc = " + MP[0] + " MPa",
                "Ec = " + MP[1] + " MPa",
                "εc = " + MP[2] + " mm/mm",
                "ρc = " + MP[3] + " kg/m3",
                "EEc = " + MP[4] + " unit/kg",
                "fy = " + MP[5] + " MPa",
                "Es = " + MP[6] + " MPa",
                "εs = " + MP[7] + " mm/mm",
                "ρs = " + MP[8] + " kg/m3",
                "EEs " + MP[9] + " unit/kg"
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
                return BeamShapeExplorer.Properties.Resources.gmpread;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("5cfa0b96-88ac-4d51-bf8b-e3927bc4fb16"); }
        }
    }
}