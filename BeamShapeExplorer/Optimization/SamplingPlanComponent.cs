using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

namespace BeamShapeExplorer.Optimization
{
    /// <summary>
    /// Step 1 of the optimization sampling workflow: defines the sampling plan (which design-input values each
    /// optimization run uses, and where each run starts). The plan is previewed on the canvas, can be saved to /
    /// loaded from CSV, and is passed to the Optimization Sampler component (step 2).
    /// </summary>
    public class SamplingPlanComponent : GH_Component
    {
        private const int InParameters = 0;
        private const int InVariables = 1;
        private const int InSamples = 2;
        private const int InMethod = 3;
        private const int InStart = 4;
        private const int InSeed = 5;
        private const int InGridLevels = 6;
        private const int InFile = 7;
        private const int InLoad = 8;
        private const int InSave = 9;

        private SamplingPlan _plan;
        private string _planKey;
        private string _savedKey;
        private bool _previousSave;

        public SamplingPlanComponent()
          : base("Sampling Plan", "SmplPlan",
              "Step 1 of optimization sampling: creates the sampling plan, i.e. the design-input values (Sample " +
              "Parameters) for every optimization run and, optionally, each run's start point. Connect the Plan " +
              "output to the Optimization Sampler. Slider Min/Max are the sampling bounds.",
              "Beam Shape Explorer", "Optimization")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Sample Parameters", "P",
                "Design inputs sampled between optimization runs (e.g. span, loads, f'c) and held fixed during each run. " +
                "Connect any number of Number Sliders; their Min/Max are the sampling bounds.",
                GH_ParamAccess.tree);
            pManager.AddNumberParameter("Variables", "V",
                "(Optional) The same Variable sliders as on the Optimization Sampler. Needed only for Start = sampled, " +
                "random or center, which put a start point for every run in the plan.",
                GH_ParamAccess.tree);
            pManager.AddIntegerParameter("Samples", "N",
                "Number of optimization runs in the plan (ignored for grid sampling, which uses Grid Levels).",
                GH_ParamAccess.item, 50);
            pManager.AddTextParameter("Sampling Method", "M",
                "lhs (Latin Hypercube, default), lhs-optimized, lhs-centered, sobol, halton, random or grid (full factorial).",
                GH_ParamAccess.item, "lhs");
            pManager.AddTextParameter("Start", "St",
                "Start point of each run: auto (default: current values when there are Sample Parameters, otherwise " +
                "sampled), sampled (drawn with the sampling method), random, center or current (each run starts from the " +
                "Optimization Sampler's Start Point, or from the variable values when the optimization study begins).",
                GH_ParamAccess.item, "auto");
            pManager.AddIntegerParameter("Seed", "S", "Random seed. The same inputs and seed always give the same plan.",
                GH_ParamAccess.item, 0);
            pManager.AddIntegerParameter("Grid Levels", "GL",
                "(Optional) For grid sampling: levels per sampled dimension, one value or one per dimension. " +
                "Default: the largest grid with at most Samples runs (min 2 levels).",
                GH_ParamAccess.list);
            pManager.AddTextParameter("Plan File", "F",
                "(Optional) CSV file to save the plan to (Save) or load it from (Load). Relative paths are resolved " +
                "from the folder of this .gh file.", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Load", "Ld",
                "Load the plan from Plan File instead of generating it (e.g. a plan edited in Excel or made with " +
                "BSE-Opt/sampling_plan.py).", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Save", "Sv",
                "Save the generated plan to Plan File (use a Button or Toggle). A .meta.json with the settings is written next to it.",
                GH_ParamAccess.item, false);

            pManager[InParameters].Optional = true;
            pManager[InVariables].Optional = true;
            pManager[InGridLevels].Optional = true;
            pManager[InFile].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Plan", "Plan", "The sampling plan. Connect it to the Optimization Sampler's Sampling Plan input.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Parameter Values", "PV", "Sample Parameter values, one branch per run.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Start Points", "X0", "Start values of the variables, one branch per run (empty without start points).", GH_ParamAccess.tree);
            pManager.AddIntegerParameter("Runs", "R", "Run numbers.", GH_ParamAccess.list);
            pManager.AddTextParameter("Status", "S", "Plan summary and notes.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int samples = 50, seed = 0;
            string method = "lhs", start = "auto", file = "";
            bool load = false, save = false;
            var levels = new List<int>();
            DA.GetData(InSamples, ref samples);
            DA.GetData(InMethod, ref method);
            DA.GetData(InStart, ref start);
            DA.GetData(InSeed, ref seed);
            DA.GetDataList(InGridLevels, levels);
            DA.GetData(InFile, ref file);
            DA.GetData(InLoad, ref load);
            DA.GetData(InSave, ref save);

            var warnings = new List<string>();
            List<SliderRef> parameters = GhUtil.SliderRefs(GhUtil.GetSliders(Params.Input[InParameters], warnings), "p");
            List<SliderRef> variables = GhUtil.SliderRefs(GhUtil.GetSliders(Params.Input[InVariables], warnings), "x");
            foreach (string w in warnings) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, w);

            string methodKey = PlanSampling.NormalizeMethod(method);
            if (methodKey == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Unknown Sampling Method \"" + method + "\". Use one of: " +
                                  string.Join(", ", PlanSampling.Methods) + ".");
                return;
            }
            string startKey = PlanSampling.NormalizeStart(start);
            if (startKey == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Unknown Start \"" + start + "\". Use one of: " +
                                  string.Join(", ", PlanSampling.StartModes) + ".");
                return;
            }

            GH_Document doc = OnPingDocument();
            string path = GhUtil.ResolvePath(file, doc, out string pathRemark);
            if (path == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Plan File contains invalid characters.");
                return;
            }
            if (pathRemark != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, pathRemark);

            // ---- get the plan (cached: the sliders' values change during optimization, the plan must not)
            string sliderKey = string.Join(";", parameters.Concat(variables).Select(s =>
                s.Id + "|" + s.Name + "|" + Num(s.Min) + "|" + Num(s.Max)));
            string key;
            try
            {
                if (load)
                {
                    if (path.Length == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Set Plan File to load a plan."); return; }
                    string stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture) : "missing";
                    key = "load|" + path + "|" + stamp + "|" + sliderKey;
                    if (key != _planKey) _plan = SamplingPlan.ReadCsv(path, parameters, variables);
                }
                else
                {
                    key = "gen|" + methodKey + "|" + startKey + "|" + seed + "|" + samples + "|" +
                          string.Join(",", levels) + "|" + sliderKey;
                    if (key != _planKey)
                        _plan = SamplingPlan.Generate(parameters, variables, samples, methodKey, startKey, seed, levels);
                }
                _planKey = key;
            }
            catch (Exception ex)
            {
                _plan = null;
                _planKey = null;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                return;
            }
            SamplingPlan plan = _plan;

            // ---- save (only when the plan changed or Save was just switched on, not on every solution)
            var status = new List<string> { plan.ToString() };
            if (save && !load)
            {
                if (path.Length == 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Set Plan File to save the plan.");
                else if (key + "|" + path != _savedKey || !_previousSave)
                {
                    try
                    {
                        plan.WriteCsv(path, doc?.DisplayName);
                        _savedKey = key + "|" + path;
                        status.Add("Saved to " + path);
                    }
                    catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Could not save the plan: " + ex.Message); }
                }
                else status.Add("Saved to " + path);
            }
            _previousSave = save;
            if (load) status.Add("Loaded from " + path);

            foreach (string note in plan.Notes)
            {
                status.Add("Note: " + note);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, note);
            }
            if (plan.Parameters.Count == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No Sample Parameters connected: the plan only varies the start points.");

            Message = plan.Count + " run(s)" + (load ? " · file" : " · " + plan.Method);

            var pv = new GH_Structure<GH_Number>();
            var x0 = new GH_Structure<GH_Number>();
            for (int i = 0; i < plan.Count; i++)
            {
                var branch = new GH_Path(i);
                pv.AppendRange(plan.P[i].Select(v => new GH_Number(v)), branch);
                if (plan.HasStartPoints) x0.AppendRange(plan.X0[i].Select(v => new GH_Number(v)), branch);
            }

            DA.SetData(0, plan);
            DA.SetDataTree(1, pv);
            DA.SetDataTree(2, x0);
            DA.SetDataList(3, plan.Runs);
            DA.SetDataList(4, status);
        }

        private static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("d142d71e-b343-41f1-9a88-c6f4c9aef0a6");
    }
}
