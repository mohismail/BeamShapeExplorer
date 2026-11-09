using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BeamShapeExplorer.Optimization
{
    /// <summary>
    /// A sampling plan: one row per optimization run with the value of each Sample Parameter slider (held
    /// fixed during the run) and, optionally, the start value of each Variable slider. Produced by the
    /// Sampling Plan component and consumed by the Optimization Sampler component. The CSV format is the
    /// same as python/sampling_plan.py: run, param:&lt;name&gt; ..., x0:&lt;name&gt; ...
    /// </summary>
    public class SamplingPlan
    {
        public List<SliderRef> Parameters = new List<SliderRef>();
        public List<SliderRef> Variables = new List<SliderRef>();   // empty: no start points in the plan
        public List<int> Runs = new List<int>();
        public List<double[]> P = new List<double[]>();
        public List<double[]> X0 = new List<double[]>();          // empty when Variables is empty
        public string Method = "file";
        public string Start = "file";
        public int? Seed;
        public int[] GridLevels;
        public string Source = "generated";
        public List<string> Notes = new List<string>();

        public int Count => Runs.Count;
        public bool HasStartPoints => Variables.Count > 0 && X0.Count == Runs.Count;

        public override string ToString()
        {
            string pars = Parameters.Count == 0 ? "no parameters" :
                Parameters.Count + " parameter(s) (" + string.Join(", ", Parameters.Select(p => p.Name)) + ")";
            string how = Source == "generated"
                ? Method + ", seed " + Seed + ", start " + Start
                : "from " + Path.GetFileName(Source);
            return "Sampling Plan: " + Count + " run(s), " + pars + ", " + how;
        }

        // ================================================================== generate

        /// <summary>
        /// Generates a plan inside the slider bounds. Parameters and "sampled" start points are drawn jointly so
        /// the combined design is space-filling. Without <paramref name="variables"/>, the plan has no start points
        /// and each run starts from the current variable values.
        /// </summary>
        public static SamplingPlan Generate(List<SliderRef> parameters, List<SliderRef> variables, int samples,
                                            string method, string start, int seed, IList<int> gridLevels)
        {
            var plan = new SamplingPlan
            {
                Parameters = parameters,
                Variables = variables ?? new List<SliderRef>(),
                Method = string.IsNullOrEmpty(method) ? "lhs" : method,
                Seed = seed,
            };
            int nPar = parameters.Count, nVar = plan.Variables.Count;
            if (string.IsNullOrEmpty(start) || start == "auto") start = nPar > 0 ? "current" : "sampled";
            plan.Start = start;
            if (start == "current")
            {
                // no start points: each run starts from the variable values when the study begins
                plan.Variables = new List<SliderRef>();
                nVar = 0;
            }
            else if (nVar == 0)
                plan.Notes.Add("Connect the Variable sliders to Variables to put \"" + start +
                               "\" start points in the plan; without them every run starts from the current variable values.");

            bool sampledStarts = start == "sampled" && nVar > 0;
            int d = nPar + (sampledStarts ? nVar : 0);
            if (samples < 1 && plan.Method != "grid") throw new ArgumentException("Samples must be at least 1.");
            int n = samples;
            int[] levels = null;
            if (plan.Method == "grid")
            {
                if (d == 0) throw new ArgumentException("Grid sampling needs Sample Parameters, or Variables with Start = sampled.");
                levels = PlanSampling.GridLevels(gridLevels, samples, d);
                plan.GridLevels = levels;
            }
            if (d == 0 && n > 1)
                plan.Notes.Add("There is nothing to sample (no Sample Parameters and a fixed start point), so every run is identical.");

            var rng = new Random(seed);
            double[][] u = d > 0 ? PlanSampling.Unit(plan.Method, n, d, rng, levels) : Enumerable.Range(0, n).Select(_ => new double[0]).ToArray();
            n = u.Length;
            if (plan.Method == "sobol" && (n & (n - 1)) != 0)
                plan.Notes.Add("Sobol sequences are most uniform when the sample count is a power of 2 (e.g. 64, 128, 256).");

            for (int i = 0; i < n; i++)
            {
                plan.Runs.Add(i);
                var p = new double[nPar];
                for (int j = 0; j < nPar; j++) p[j] = Scale(u[i][j], parameters[j]);
                plan.P.Add(p);
                if (nVar == 0) continue;
                var x = new double[nVar];
                for (int j = 0; j < nVar; j++)
                {
                    SliderRef s = plan.Variables[j];
                    switch (start)
                    {
                        case "sampled": x[j] = Scale(u[i][nPar + j], s); break;
                        case "random": x[j] = Scale(rng.NextDouble(), s); break;
                        case "center": x[j] = Scale(0.5, s); break;
                        default: x[j] = s.Value; break;
                    }
                }
                plan.X0.Add(x);
            }
            return plan;
        }

        private static double Scale(double u, SliderRef s) => s.Min + u * (s.Max - s.Min);

        // ================================================================== JSON for the Python driver

        public Dictionary<string, object> ToInfo(bool includeStartPoints)
        {
            return new Dictionary<string, object>
            {
                { "source", Source },
                { "method", Method },
                { "start", Start },
                { "seed", Seed.HasValue ? (object)Seed.Value : null },
                { "grid_levels", GridLevels != null ? GridLevels.Select(v => (object)v).ToList() : null },
                { "parameter_names", Parameters.Select(p => p.Name).ToList() },
                { "variable_names", includeStartPoints && HasStartPoints ? Variables.Select(v => v.Name).ToList() : new List<string>() },
                { "runs", Runs },
                { "P", P.Select(r => r.ToList()).ToList() },
                { "X0", includeStartPoints && HasStartPoints ? X0.Select(r => r.ToList()).ToList() : null },
            };
        }

        // ================================================================== CSV

        private static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        public static string MetaPath(string csvPath) =>
            Path.Combine(Path.GetDirectoryName(csvPath) ?? "", Path.GetFileNameWithoutExtension(csvPath) + ".meta.json");

        /// <summary>Writes the plan CSV and a .meta.json next to it (same format as python/sampling_plan.py).</summary>
        public void WriteCsv(string path, string document)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            var header = new List<string> { "run" };
            header.AddRange(Parameters.Select(p => "param:" + p.Name));
            if (HasStartPoints) header.AddRange(Variables.Select(v => "x0:" + v.Name));
            sb.Append(string.Join(",", header)).Append('\n');
            for (int i = 0; i < Count; i++)
            {
                var row = new List<string> { Runs[i].ToString(CultureInfo.InvariantCulture) };
                row.AddRange(P[i].Select(Num));
                if (HasStartPoints) row.AddRange(X0[i].Select(Num));
                sb.Append(string.Join(",", row)).Append('\n');
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));

            var meta = new Dictionary<string, object>
            {
                { "kind", "sampling_plan" },
                { "document", document ?? "" },
                { "created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) },
                { "variables", Variables.Select(v => (object)v.ToMeta()).ToList() },
                { "parameters", Parameters.Select(p => (object)p.ToMeta()).ToList() },
                { "source", Source },
                { "runs", Count },
                { "method", Method },
                { "start", Start },
                { "seed", Seed.HasValue ? (object)Seed.Value : null },
                { "grid_levels", GridLevels != null ? GridLevels.Select(v => (object)v).ToList() : null },
            };
            File.WriteAllText(MetaPath(path), MiniJson.Serialize(meta), new UTF8Encoding(false));
        }

        /// <summary>
        /// Reads a plan CSV and checks it against the sliders. Parameter columns may be named param:&lt;name&gt; or
        /// &lt;name&gt;; x0:&lt;name&gt; columns are optional (missing or blank = current slider value). Values outside the
        /// slider bounds are clipped and unknown columns ignored; both are reported in <see cref="Notes"/>.
        /// </summary>
        public static SamplingPlan ReadCsv(string path, List<SliderRef> parameters, List<SliderRef> variables)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Plan file not found: " + path);
            var lines = File.ReadAllLines(path, Encoding.UTF8).Where(l => l.Trim().Length > 0).ToList();
            if (lines.Count < 2) throw new InvalidDataException("Plan file " + path + " has no rows.");
            List<string> columns = SplitCsv(lines[0].TrimStart('﻿')).Select(c => c.Trim()).ToList();

            var plan = new SamplingPlan { Parameters = parameters, Source = path };
            Func<string, int> Find = name => columns.IndexOf(name);

            var pCols = new List<int>();
            var missing = new List<string>();
            foreach (SliderRef s in parameters)
            {
                int c = Find("param:" + s.Name);
                if (c < 0) c = Find(s.Name);
                if (c < 0) missing.Add(s.Name);
                pCols.Add(c);
            }
            if (missing.Count > 0)
                throw new InvalidDataException("The plan has no column for Sample Parameter(s) " + string.Join(", ", missing) +
                                               ". Expected columns named param:<name> (or <name>).");

            var xCols = new List<int>();
            var noStart = new List<string>();
            if (variables != null)
                foreach (SliderRef s in variables)
                {
                    int c = Find("x0:" + s.Name);
                    if (c < 0) noStart.Add(s.Name);
                    xCols.Add(c);
                }
            bool anyStart = xCols.Any(c => c >= 0);
            if (anyStart)
            {
                plan.Variables = variables;
                if (noStart.Count > 0)
                    plan.Notes.Add("No x0 column for " + string.Join(", ", noStart) + "; those variables start from their current value.");
            }
            else if (columns.Any(c => c.StartsWith("x0:", StringComparison.Ordinal)))
                plan.Notes.Add("The plan has x0 start points but no matching sliders are connected to Variables; they are ignored.");

            int runCol = Find("run");
            var used = new HashSet<int>(pCols.Concat(xCols).Where(c => c >= 0)) { runCol };
            var unknown = columns.Where((c, i) => !used.Contains(i) && !(c.StartsWith("x0:", StringComparison.Ordinal) && !anyStart)).ToList();
            if (unknown.Count > 0) plan.Notes.Add("Ignoring unknown plan column(s): " + string.Join(", ", unknown));

            int clipped = 0;
            Func<double, SliderRef, double> Clip = (v, s) =>
            {
                if (v < s.Min) { clipped++; return s.Min; }
                if (v > s.Max) { clipped++; return s.Max; }
                return v;
            };
            Func<string, string, int, double> Parse = (text, what, row) =>
            {
                double v;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
                throw new InvalidDataException("Plan row " + row + ": \"" + text + "\" is not a number (" + what + ").");
            };

            for (int r = 1; r < lines.Count; r++)
            {
                List<string> cells = SplitCsv(lines[r]);
                Func<int, string> Cell = c => c >= 0 && c < cells.Count ? cells[c].Trim() : "";
                plan.Runs.Add(Cell(runCol).Length > 0 ? (int)Parse(Cell(runCol), "run", r) : r - 1);
                var p = new double[parameters.Count];
                for (int j = 0; j < parameters.Count; j++)
                {
                    string t = Cell(pCols[j]);
                    if (t.Length == 0) throw new InvalidDataException("Plan row " + r + ": no value for parameter \"" + parameters[j].Name + "\".");
                    p[j] = Clip(Parse(t, parameters[j].Name, r), parameters[j]);
                }
                plan.P.Add(p);
                if (anyStart)
                {
                    var x = new double[variables.Count];
                    for (int j = 0; j < variables.Count; j++)
                    {
                        string t = Cell(xCols[j]);
                        x[j] = t.Length > 0 ? Clip(Parse(t, "x0:" + variables[j].Name, r), variables[j]) : variables[j].Value;
                    }
                    plan.X0.Add(x);
                }
            }
            if (clipped > 0) plan.Notes.Add(clipped + " plan value(s) were outside the slider bounds and were clipped to them.");
            if (plan.Runs.Distinct().Count() != plan.Runs.Count) throw new InvalidDataException("The plan has duplicate run numbers.");

            // keep the settings of a plan written by this component or python/sampling_plan.py
            try
            {
                string metaPath = MetaPath(path);
                if (File.Exists(metaPath) && MiniJson.Parse(File.ReadAllText(metaPath)) is Dictionary<string, object> meta &&
                    meta.TryGetValue("kind", out object kind) && (kind as string) == "sampling_plan")
                {
                    if (meta.TryGetValue("method", out object m) && m is string ms) plan.Method = ms;
                    if (meta.TryGetValue("start", out object st) && st is string sts) plan.Start = sts;
                    if (meta.TryGetValue("seed", out object sd) && sd != null) plan.Seed = (int)MiniJson.ToDouble(sd);
                    if (meta.TryGetValue("grid_levels", out object gl) && gl != null)
                        plan.GridLevels = MiniJson.ToDoubleList(gl).Select(v => (int)v).ToArray();
                }
            }
            catch (Exception) { /* the meta file is optional */ }
            return plan;
        }

        /// <summary>Splits one CSV line, honouring double-quoted fields.</summary>
        private static List<string> SplitCsv(string line)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else sb.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cells.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            cells.Add(sb.ToString());
            return cells;
        }
    }
}
