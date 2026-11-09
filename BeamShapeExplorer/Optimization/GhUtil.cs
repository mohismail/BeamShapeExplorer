using System;
using System.Collections.Generic;
using System.IO;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace BeamShapeExplorer.Optimization
{
    /// <summary>A reference to a Number Slider in the document, with its bounds at the time it was read.</summary>
    public class SliderRef
    {
        public Guid Id;
        public string Name;
        public double Min, Max, Value;
        public int Decimals;
        public bool Integer;

        public static SliderRef From(GH_NumberSlider s, string name)
        {
            double lo = (double)s.Slider.Minimum, hi = (double)s.Slider.Maximum;
            if (hi < lo) { double t = lo; lo = hi; hi = t; }
            return new SliderRef
            {
                Id = s.InstanceGuid,
                Name = name,
                Min = lo,
                Max = hi,
                Value = (double)s.Slider.Value,
                Decimals = s.Slider.DecimalPlaces,
                Integer = s.Slider.Type != Grasshopper.GUI.Base.GH_SliderAccuracy.Float,
            };
        }

        /// <summary>Same keys as the Python driver's info/meta format.</summary>
        public Dictionary<string, object> ToInfo()
        {
            return new Dictionary<string, object>
            {
                { "name", Name }, { "min", Min }, { "max", Max }, { "value", Value },
                { "decimals", Decimals }, { "integer", Integer },
            };
        }

        public Dictionary<string, object> ToMeta()
        {
            return new Dictionary<string, object>
            {
                { "name", Name }, { "lo", Min }, { "hi", Max }, { "value", Value },
                { "decimals", Decimals }, { "integer", Integer },
            };
        }
    }

    internal static class GhUtil
    {
        /// <summary>Number Sliders wired into an input, in wire order. Other sources are reported in warnings.</summary>
        public static List<GH_NumberSlider> GetSliders(IGH_Param input, List<string> warnings)
        {
            var sliders = new List<GH_NumberSlider>();
            foreach (IGH_Param src in input.Sources)
            {
                if (src is GH_NumberSlider s) sliders.Add(s);
                else if (warnings != null)
                    warnings.Add("Input \"" + input.Name + "\" source \"" + src.NickName +
                                 "\" is not a Number Slider and will be ignored.");
            }
            return sliders;
        }

        /// <summary>Slider references named after the slider NickNames (made unique).</summary>
        public static List<SliderRef> SliderRefs(List<GH_NumberSlider> sliders, string prefix)
        {
            var names = new List<string>();
            foreach (GH_NumberSlider s in sliders) names.Add(s.NickName);
            names = UniqueNames(names, prefix);
            var refs = new List<SliderRef>();
            for (int i = 0; i < sliders.Count; i++) refs.Add(SliderRef.From(sliders[i], names[i]));
            return refs;
        }

        public static List<string> UniqueNames(List<string> names, string prefix)
        {
            var result = new List<string>();
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++)
            {
                string n = string.IsNullOrWhiteSpace(names[i]) ? prefix + i : names[i].Trim();
                n = n.Replace(",", "_").Replace("\"", "_").Replace("\n", "_");
                if (seen.TryGetValue(n, out int c))
                {
                    seen[n] = c + 1;
                    n = n + "_" + (c + 1);
                }
                else seen[n] = 0;
                result.Add(n);
            }
            return result;
        }

        /// <summary>
        /// Resolves a user path: environment variables are expanded and relative paths are taken relative to the
        /// saved .gh file's folder. If the document is unsaved, a relative path is returned unchanged and
        /// <paramref name="remark"/> explains it. Returns "" for an empty path, null for an invalid one.
        /// </summary>
        public static string ResolvePath(string path, GH_Document doc, out string remark)
        {
            remark = null;
            if (string.IsNullOrWhiteSpace(path)) return "";
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
            if (!Path.IsPathRooted(path))
            {
                string ghFile = doc?.FilePath;
                if (!string.IsNullOrEmpty(ghFile))
                    path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ghFile), path));
                else
                    remark = "The path \"" + path + "\" is relative and this definition is unsaved; " +
                             "save the .gh file or use a full path.";
            }
            return path;
        }
    }
}
