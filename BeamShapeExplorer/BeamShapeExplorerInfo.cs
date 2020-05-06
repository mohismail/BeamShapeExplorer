using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace BeamShapeExplorer
{
    public class BeamShapeExplorerInfo : GH_AssemblyInfo
    {
        public override string Name
        {
            get
            {
                return "BeamShapeExplorer";
            }
        }
        public override Bitmap Icon
        {
            get
            {
                //Return a 24x24 pixel bitmap to represent this GHA library.
                return null;
            }
        }
        public override string Description
        {
            get
            {
                //Return a short string describing the purpose of this GHA library.
                return "";
            }
        }
        public override Guid Id
        {
            get
            {
                return new Guid("0fa77360-1220-4afb-b8be-d1989c86741e");
            }
        }

        public override string AuthorName
        {
            get
            {
                //Return a string identifying you or your company.
                return "HP Inc.";
            }
        }
        public override string AuthorContact
        {
            get
            {
                //Return a string representing your preferred contact details.
                return "";
            }
        }
    }
}
