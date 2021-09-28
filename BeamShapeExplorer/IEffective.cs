using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BeamShapeExplorer
{
    public class IEffective : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MyComponent1 class.
        /// </summary>
        public IEffective()
          : base("Effective moment of inertia", "Ieff",
              "Calculates effective moment of inertia for loaded concrete section",
              "Beam Shape Explorer", "TEST")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Moment Envelope (kN-m)", "Mu (kN-m)", "Applied moments (kN-m) along length of element", GH_ParamAccess.list, 15);
            pManager.AddNumberParameter("Cracking Moment (kN-m)", "Mcr (kN-m)", "Cracking moment of beam section", GH_ParamAccess.list, 10);
            pManager.AddNumberParameter("Gross Moment of Inertia (m\x2074)", "Iu (m\x2074)", "Gross moment of area for each section", GH_ParamAccess.list);
            pManager.AddNumberParameter("Cracked Moment of Inertia (m\x2074)", "Icr (m\x2074)", "Moment of area for fully cracked sections", GH_ParamAccess.list);

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Effective Moment of Inertia", "Ieff (m\x2074)", "Effective moment of inertia for loaded concrete sections", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<double> Mu = new List<double>();
            List<double> Mcr = new List<double>();
            List<double> Iu = new List<double>();
            List<double> Icr = new List<double>();

            if (!DA.GetDataList(0, Mu)) return;
            if (!DA.GetDataList(1, Mcr)) return;
            if (!DA.GetDataList(2, Iu)) return;
            if (!DA.GetDataList(3, Icr)) return;

            List<double> Ieff = new List<double>();
            for (int i = 0; i < Mu.Count; i++)
            {
                double sectMu = Math.Abs(Mu[i]);
                double sectMcr = Math.Abs(Mcr[i]);

                double sectIeff = (Iu[i] * Math.Pow((sectMcr / sectMu), 3) + Icr[i] * (1 - Math.Pow((sectMcr / sectMu), 3))); 
                Ieff.Add(sectIeff);
            }

            DA.SetDataList(0, Ieff);

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
            get { return new Guid("70FACD62-B48A-471D-9691-2C0D851E5A08"); }
        }
    }
}