using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace TestingThingsdsfdsf
{

	public class Program
    {
      static void Main(string[] args)
        {
            string code = System.Configuration.ConfigurationManager.AppSettings["code"];
            Console.WriteLine(code);
        }

    }
}
