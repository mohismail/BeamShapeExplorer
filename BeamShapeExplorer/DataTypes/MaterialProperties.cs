using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BeamShapeExplorer.DataTypes
{
    public class MaterialProperties
    {
        public double fC, EC, eC, rhoC, EEC;
        public double fY, ES, eS, rhoS, EES;

        public MaterialProperties(
            double fc, double Ec, double ec, double rhoc, double EEc,
            double fy, double Es, double es, double rhos, double EEs)
        {
            this.fC = fc; this.EC = Ec; this.eC = ec; this.rhoC = rhoc; this.EEC = EEc;
            this.fY = fy; this.ES = Es; this.eS = es; this.rhoS = rhos; this.EES = EEs;
        }

        public override string ToString()
        {
            string query = @"fc = " + this.fC + " MPa \n" +
            "Ec = " + this.EC + " MPa \n" + 
            "εc = " + this.eC + " mm/mm \n" + 
            "ρc = " + this.rhoC + " kg/m3 \n" + 
            "EEc = " + this.EEC + " MJ/kg \n" + 
            "fy = " + this.fY + " MPa \n" + 
            "Es = " + this.ES + " MPa \n" + 
            "εs = " + this.eS + " mm/mm \n" + 
            "ρs = " + this.rhoS + " kg/m3 \n" + 
            "EEs = " + this.EES + " MJ/kg";

            return query;
        }
    }
}
