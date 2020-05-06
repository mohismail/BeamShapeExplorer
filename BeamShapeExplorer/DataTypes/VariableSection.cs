using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeamShapeExplorer.DataTypes
{
    public class VariableSection
    {
        public Plane sctPln;
        public List<Point3d> sctPts;

        public VariableSection(Plane SPln, List<Point3d> SPts)
        {
            this.sctPln = SPln; this.sctPts = SPts;
        }

        public override string ToString()
        {
            string query = sctPts.Count + " points on plane with normal vector " + sctPln.Normal + " and origin point " + sctPln.Origin;
            return query;
        }
    }
}