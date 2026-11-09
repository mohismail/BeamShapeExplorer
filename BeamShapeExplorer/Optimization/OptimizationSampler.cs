using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

namespace BeamShapeExplorer.Optimization
{
    /// <summary>
    /// Exposes a Grasshopper definition to an external (C)Python optimization driver so that many
    /// constrained optimization runs can be sampled automatically, following the BSE surrogate-model
    /// data-collection workflow (LHS over design inputs -> one constrained optimization per sample).
    ///
    /// Everything is generic:
    ///   * Variables          - any number of Number Sliders. Their Min/Max are the optimization bounds.
    ///   * Sample Parameters  - optional Number Sliders that are held fixed during one optimization run
    ///                          and sampled (e.g. Latin Hypercube) between runs. Min/Max are the sample bounds.
    ///   * Objectives         - any number of numbers (wires); minimized as a weighted sum.
    ///   * Constraints        - any number of numbers (wires); each is "value [<=|>=|=] limit".
    ///
    /// The component runs a small TCP server on localhost that speaks newline-delimited JSON.
    /// Each "evaluate" request sets the sliders, solves the definition on the Rhino UI thread and returns
    /// the objective and constraint values. See python/README.md for the protocol and the Python driver.
    /// </summary>
    public class OptimizationSampler : GH_Component
    {
        private const int InVariables = 0;
        private const int InParameters = 1;
        private const int InObjectives = 2;
        private const int InWeights = 3;
        private const int InConstraints = 4;
        private const int InLimits = 5;
        private const int InTypes = 6;
        private const int InPort = 7;
        private const int InListen = 8;
        private const int InOutput = 9;
        private const int InSampling = 10;

        private const int DefaultPort = 8765;
        private const int DefaultTimeoutMs = 10 * 60 * 1000;

        // ---- server state (touched from both UI and server threads)
        private readonly object _serverLock = new object();
        private TcpListener _listener;
        private Thread _serverThread;
        private TcpClient _activeClient;
        private volatile bool _running;
        private int _port = -1;
        private volatile string _serverError;

        // ---- latest solution snapshot (written in SolveInstance on the UI thread)
        private readonly object _snapshotLock = new object();
        private Snapshot _snapshot = new Snapshot();
        private int _solveCount;
        private int _evaluationCount;
        private volatile string _outputPath = "";
        private volatile string _samplingMethod = "";

        private sealed class Snapshot
        {
            public List<double> Objectives = new List<double>();
            public List<string> ObjectiveNames = new List<string>();
            public List<double> Weights = new List<double>();
            public List<double> Constraints = new List<double>();
            public List<string> ConstraintNames = new List<string>();
            public List<double> Limits = new List<double>();
            public List<string> Types = new List<string>();
        }

        public OptimizationSampler()
          : base("Optimization Sampler", "OptSampler",
              "Connects this Grasshopper definition to an external Python optimization driver " +
              "(python/bse_optimization_sampler.py) to sample many constrained optimization runs. " +
              "Variables and sample parameters are Number Sliders whose Min/Max define the bounds; " +
              "objectives and constraints are any numbers. Results are saved to CSV by the Python driver.",
              "Beam Shape Explorer", "Optimization")
        {
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Variables", "V",
                "Optimization design variables. Connect any number of Number Sliders; the slider Min/Max " +
                "are used as the optimization bounds. Use enough decimal places on the sliders, since slider values are rounded.",
                GH_ParamAccess.tree);
            pManager.AddNumberParameter("Sample Parameters", "P",
                "(Optional) Design inputs that are sampled between optimization runs (e.g. span, load, f'c) and held " +
                "fixed during each run. Connect any number of Number Sliders; Min/Max are the sampling bounds.",
                GH_ParamAccess.tree);
            pManager.AddNumberParameter("Objectives", "O",
                "Objective value(s) to minimize. Any number of wires/values; they are combined as a weighted sum.",
                GH_ParamAccess.tree);
            pManager.AddNumberParameter("Objective Weights", "W",
                "(Optional) Weight per objective for the weighted sum. Default 1.0. If shorter than the objective list, the last weight is repeated.",
                GH_ParamAccess.tree);
            pManager.AddNumberParameter("Constraints", "C",
                "(Optional) Constraint value(s). Any number of wires/values. Each is checked as: value [type] limit.",
                GH_ParamAccess.tree);
            pManager.AddNumberParameter("Constraint Limits", "CL",
                "(Optional) Limit per constraint. Default 0.0. If shorter than the constraint list, the last limit is repeated.",
                GH_ParamAccess.tree);
            pManager.AddTextParameter("Constraint Types", "CT",
                "(Optional) Type per constraint: \"<=\" (default), \">=\" or \"=\". If shorter than the constraint list, the last type is repeated.",
                GH_ParamAccess.tree);
            pManager.AddIntegerParameter("Port", "Port",
                "Localhost TCP port the Python driver connects to.", GH_ParamAccess.item, DefaultPort);
            pManager.AddBooleanParameter("Listen", "L",
                "Set to true to start listening for the Python driver (use a Boolean Toggle).", GH_ParamAccess.item, false);
            pManager.AddTextParameter("Output Path", "Out",
                "(Optional) Where the Python driver saves the results CSV. Either a file path (e.g. D:\\study\\runs.csv) " +
                "or a folder (the file is then named optimization_samples.csv). Relative paths are resolved from the " +
                "folder of this .gh file. Missing folders are created. The driver's --out option overrides this.",
                GH_ParamAccess.item);

            for (int i = InParameters; i <= InTypes; i++)
                if (i != InObjectives) pManager[i].Optional = true;
            pManager[InVariables].Optional = true;
            pManager.AddTextParameter("Sampling Method", "SM",
                "(Optional) How the sampling plan (python/sampling_plan.py, or the driver when it makes the plan itself) " +
                "samples the Sample Parameters: " +
                "lhs (Latin Hypercube, default), lhs-optimized, lhs-centered, sobol, halton, random, or grid " +
                "(full factorial). The --sampling option overrides this. Not used when the driver runs an existing plan (--plan).",
                GH_ParamAccess.item);

            pManager[InOutput].Optional = true;
            pManager[InSampling].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "Server status and diagnostics.", GH_ParamAccess.list);
            pManager.AddTextParameter("Variable Names", "VN", "Names of the detected variable sliders (in order).", GH_ParamAccess.list);
            pManager.AddNumberParameter("Lower Bounds", "LB", "Lower bounds of the variables (slider Min).", GH_ParamAccess.list);
            pManager.AddNumberParameter("Upper Bounds", "UB", "Upper bounds of the variables (slider Max).", GH_ParamAccess.list);
            pManager.AddNumberParameter("Weighted Objective", "F", "Current weighted objective value.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Constraint Violation", "CV",
                "Current violation of each constraint (0 = satisfied, >0 = violated by that amount).", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Evaluations", "N", "Number of evaluations requested by the Python driver since listening started.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var status = new List<string>();

            // ---- read data (tree access so the component solves exactly once per solution)
            List<double> objectives = ReadNumbers(DA, InObjectives);
            List<double> weights = ReadNumbers(DA, InWeights);
            List<double> constraints = ReadNumbers(DA, InConstraints);
            List<double> limits = ReadNumbers(DA, InLimits);
            List<string> types = ReadStrings(DA, InTypes);
            int port = DefaultPort;
            bool listen = false;
            DA.GetData(InPort, ref port);
            DA.GetData(InListen, ref listen);
            string outputPath = "";
            DA.GetData(InOutput, ref outputPath);
            _outputPath = ResolveOutputPath(outputPath);
            string sampling = "";
            DA.GetData(InSampling, ref sampling);
            string samplingKey = NormalizeSampling(sampling);
            if (samplingKey == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Unknown Sampling Method \"" + sampling +
                    "\". Use one of: " + string.Join(", ", SamplingMethods) + ".");
                samplingKey = "";
            }
            _samplingMethod = samplingKey;

            // validate constraint types
            for (int i = 0; i < types.Count; i++)
            {
                string t = NormalizeType(types[i]);
                if (t == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid constraint type \"" + types[i] + "\". Use <=, >= or =.");
                    return;
                }
                types[i] = t;
            }

            var snap = new Snapshot
            {
                Objectives = objectives,
                ObjectiveNames = SourceNames(InObjectives, objectives.Count, "obj"),
                Weights = Expand(weights, objectives.Count, 1.0),
                Constraints = constraints,
                ConstraintNames = SourceNames(InConstraints, constraints.Count, "con"),
                Limits = Expand(limits, constraints.Count, 0.0),
                Types = Expand(types, constraints.Count, "<="),
            };
            lock (_snapshotLock)
            {
                _snapshot = snap;
                _solveCount++;
            }

            // ---- inspect sliders
            List<string> warnings = new List<string>();
            List<GH_NumberSlider> vars = GetSliders(InVariables, warnings);
            List<GH_NumberSlider> pars = GetSliders(InParameters, warnings);
            foreach (string w in warnings) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, w);
            if (vars.Count == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Connect at least one Number Slider to Variables (V).");
            if (objectives.Count == 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No objective values received.");
            if (objectives.Any(double.IsNaN) || constraints.Any(double.IsNaN))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Some objective/constraint values are null or not numbers.");
            if (vars.Concat(pars).Any(s => s.Slider.Type == Grasshopper.GUI.Base.GH_SliderAccuracy.Float && s.Slider.DecimalPlaces < 4))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Some sliders have fewer than 4 decimal places. Slider values are rounded, which makes the objective stepped and can stop the optimizer early; 5+ decimal places are recommended.");

            // ---- server lifecycle
            if (listen)
            {
                if (port < 1 || port > 65535)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Port must be between 1 and 65535.");
                    StopServer();
                }
                else if (!_running || _port != port)
                {
                    StopServer();
                    StartServer(port);
                }
            }
            else if (_running)
            {
                StopServer();
            }

            if (_serverError != null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _serverError);

            Message = _running ? "Listening :" + _port : "Idle";

            status.Add(_running ? "Listening on 127.0.0.1:" + _port : "Not listening (set Listen to true)");
            status.Add(vars.Count + " variable(s), " + pars.Count + " sample parameter(s), " +
                       objectives.Count + " objective(s), " + constraints.Count + " constraint(s)");
            if (_serverError != null) status.Add("Error: " + _serverError);
            status.Add("Sampling method: " + (_samplingMethod.Length > 0 ? _samplingMethod : "lhs (default)"));
            status.Add(_outputPath.Length > 0
                ? "Results will be saved to: " + _outputPath
                : "Output Path not set: the driver uses its --out option (default optimization_samples.csv in its working folder)");

            var violations = new List<double>();
            for (int i = 0; i < constraints.Count; i++)
                violations.Add(Violation(constraints[i], snap.Limits[i], snap.Types[i]));

            double weighted = WeightedSum(objectives, snap.Weights);

            DA.SetDataList(0, status);
            DA.SetDataList(1, UniqueNames(vars.Select(s => s.NickName).ToList(), "x"));
            DA.SetDataList(2, vars.Select(s => (double)s.Slider.Minimum).ToList());
            DA.SetDataList(3, vars.Select(s => (double)s.Slider.Maximum).ToList());
            DA.SetData(4, weighted);
            DA.SetDataList(5, violations);
            DA.SetData(6, _evaluationCount);
        }

        // ===================================================================== data helpers

        private static List<double> ReadNumbers(IGH_DataAccess DA, int index)
        {
            var result = new List<double>();
            if (!DA.GetDataTree(index, out GH_Structure<GH_Number> tree) || tree == null) return result;
            // keep nulls as NaN so indices stay aligned with names/limits/types
            foreach (List<GH_Number> branch in tree.Branches)
                foreach (GH_Number n in branch)
                    result.Add(n == null ? double.NaN : n.Value);
            return result;
        }

        private static List<string> ReadStrings(IGH_DataAccess DA, int index)
        {
            var result = new List<string>();
            if (!DA.GetDataTree(index, out GH_Structure<GH_String> tree) || tree == null) return result;
            foreach (List<GH_String> branch in tree.Branches)
                foreach (GH_String s in branch)
                    if (s != null) result.Add(s.Value);
            return result;
        }

        /// <summary>
        /// Resolves the Output Path input: relative paths are taken relative to the saved .gh file's folder
        /// (left relative if the document is unsaved, so the driver resolves them from its working folder).
        /// </summary>
        private string ResolveOutputPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Output Path contains invalid characters.");
                return "";
            }
            if (!Path.IsPathRooted(path))
            {
                string ghFile = OnPingDocument()?.FilePath;
                if (!string.IsNullOrEmpty(ghFile))
                    path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ghFile), path));
                else
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        "Output Path is relative and this definition is unsaved; it will be resolved from the Python driver's working folder.");
            }
            return path;
        }

        private static readonly string[] SamplingMethods =
            { "lhs", "lhs-optimized", "lhs-centered", "sobol", "halton", "random", "grid" };

        /// <summary>Returns the canonical sampling method name, "" when not set, or null when unknown.</summary>
        private static string NormalizeSampling(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            string key = name.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
            switch (key)
            {
                case "latin": case "latin-hypercube": key = "lhs"; break;
                case "lhs-maximin": case "optimized-lhs": key = "lhs-optimized"; break;
                case "centered-lhs": key = "lhs-centered"; break;
                case "monte-carlo": case "montecarlo": case "uniform": case "mc": key = "random"; break;
                case "factorial": case "full-factorial": key = "grid"; break;
            }
            return Array.IndexOf(SamplingMethods, key) >= 0 ? key : null;
        }

        private static List<T> Expand<T>(List<T> values, int count, T fallback)
        {
            var result = new List<T>(count);
            for (int i = 0; i < count; i++)
            {
                if (values.Count == 0) result.Add(fallback);
                else result.Add(values[Math.Min(i, values.Count - 1)]);
            }
            return result;
        }

        private static string NormalizeType(string t)
        {
            if (t == null) return null;
            switch (t.Trim().ToLowerInvariant())
            {
                case "<=": case "<": case "=<": case "le": case "leq": case "max": case "upper": return "<=";
                case ">=": case ">": case "=>": case "ge": case "geq": case "min": case "lower": return ">=";
                case "=": case "==": case "eq": return "=";
                default: return null;
            }
        }

        private static double Violation(double value, double limit, string type)
        {
            if (double.IsNaN(value)) return double.PositiveInfinity;
            switch (type)
            {
                case ">=": return Math.Max(0.0, limit - value);
                case "=": return Math.Abs(value - limit);
                default: return Math.Max(0.0, value - limit);
            }
        }

        private static double WeightedSum(List<double> values, List<double> weights)
        {
            double sum = 0.0;
            for (int i = 0; i < values.Count; i++) sum += weights[i] * values[i];
            return sum;
        }

        /// <summary>Best-effort names for each value of a list input, derived from the wired sources.</summary>
        private List<string> SourceNames(int inputIndex, int count, string prefix)
        {
            var names = new List<string>();
            IGH_Param input = Params.Input[inputIndex];
            foreach (IGH_Param src in input.Sources)
            {
                string name = src.NickName;
                IGH_DocumentObject owner = src.Attributes?.GetTopLevel?.DocObject;
                if (owner != null && !ReferenceEquals(owner, src) && !string.IsNullOrWhiteSpace(owner.NickName))
                    name = owner.NickName + "." + src.NickName;
                if (string.IsNullOrWhiteSpace(name)) name = prefix;
                int n = src.VolatileDataCount;
                if (n == 1) names.Add(name);
                else for (int k = 0; k < n; k++) names.Add(name + "[" + k + "]");
            }
            if (names.Count != count)
            {
                names.Clear();
                for (int i = 0; i < count; i++) names.Add(prefix + i);
            }
            return UniqueNames(names, prefix);
        }

        private static List<string> UniqueNames(List<string> names, string prefix)
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

        private List<GH_NumberSlider> GetSliders(int inputIndex, List<string> warnings)
        {
            var sliders = new List<GH_NumberSlider>();
            IGH_Param input = Params.Input[inputIndex];
            foreach (IGH_Param src in input.Sources)
            {
                if (src is GH_NumberSlider s) sliders.Add(s);
                else if (warnings != null)
                    warnings.Add("Input \"" + input.Name + "\" source \"" + src.NickName +
                                 "\" is not a Number Slider and will be ignored by the optimizer.");
            }
            return sliders;
        }

        private static Dictionary<string, object> SliderInfo(GH_NumberSlider s, string name)
        {
            return new Dictionary<string, object>
            {
                { "name", name },
                { "min", (double)s.Slider.Minimum },
                { "max", (double)s.Slider.Maximum },
                { "value", (double)s.Slider.Value },
                { "decimals", s.Slider.DecimalPlaces },
                { "integer", s.Slider.Type != Grasshopper.GUI.Base.GH_SliderAccuracy.Float },
            };
        }

        // ===================================================================== server

        private void StartServer(int port)
        {
            lock (_serverLock)
            {
                _serverError = null;
                try
                {
                    _listener = new TcpListener(IPAddress.Loopback, port);
                    _listener.Start();
                }
                catch (Exception ex)
                {
                    _listener = null;
                    _running = false;
                    _serverError = "Could not listen on port " + port + ": " + ex.Message;
                    return;
                }
                _port = port;
                _running = true;
                _evaluationCount = 0;
                _serverThread = new Thread(ServerLoop) { IsBackground = true, Name = "BSE Optimization Sampler" };
                _serverThread.Start();
            }
        }

        private void StopServer()
        {
            lock (_serverLock)
            {
                _running = false;
                try { _listener?.Stop(); } catch { /* ignore */ }
                try { _activeClient?.Close(); } catch { /* ignore */ }
                _listener = null;
                _activeClient = null;
                _serverThread = null;
                _port = -1;
            }
        }

        private void ServerLoop()
        {
            TcpListener listener = _listener;
            while (_running && listener != null)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch { break; } // listener stopped
                lock (_serverLock) _activeClient = client;
                try { HandleClient(client); }
                catch { /* client dropped; keep listening */ }
                finally
                {
                    try { client.Close(); } catch { /* ignore */ }
                    lock (_serverLock) if (ReferenceEquals(_activeClient, client)) _activeClient = null;
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            client.NoDelay = true;
            var utf8 = new UTF8Encoding(false);
            using (NetworkStream stream = client.GetStream())
            using (var reader = new StreamReader(stream, utf8))
            using (var writer = new StreamWriter(stream, utf8) { AutoFlush = true, NewLine = "\n" })
            {
                string line;
                while (_running && (line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    bool close = false;
                    Dictionary<string, object> response;
                    try { response = HandleRequest(line, out close); }
                    catch (Exception ex) { response = Error(ex.InnerException?.Message ?? ex.Message); }
                    writer.WriteLine(MiniJson.Serialize(response));
                    if (close) break;
                }
            }
        }

        private static Dictionary<string, object> Error(string message)
        {
            return new Dictionary<string, object> { { "ok", false }, { "error", message } };
        }

        private Dictionary<string, object> HandleRequest(string line, out bool close)
        {
            close = false;
            var request = MiniJson.Parse(line) as Dictionary<string, object>;
            if (request == null) return Error("Request must be a JSON object.");
            string cmd = request.TryGetValue("cmd", out object c) ? Convert.ToString(c, CultureInfo.InvariantCulture) : "";
            int timeout = DefaultTimeoutMs;
            if (request.TryGetValue("timeout", out object t) && t != null)
                timeout = (int)Math.Max(1000, MiniJson.ToDouble(t) * 1000.0);

            switch (cmd)
            {
                case "ping":
                    return new Dictionary<string, object> { { "ok", true }, { "message", "pong" } };
                case "info":
                    return RunOnUiThread(() => BuildInfo(), timeout);
                case "evaluate":
                case "set":
                    {
                        List<double> x = request.TryGetValue("variables", out object v) ? MiniJson.ToDoubleList(v) : null;
                        List<double> p = request.TryGetValue("parameters", out object pp) ? MiniJson.ToDoubleList(pp) : null;
                        bool count = cmd == "evaluate";
                        return RunOnUiThread(() => Evaluate(x, p, count), timeout);
                    }
                case "close":
                    close = true;
                    return new Dictionary<string, object> { { "ok", true }, { "message", "bye" } };
                default:
                    return Error("Unknown cmd \"" + cmd + "\". Use ping, info, evaluate, set or close.");
            }
        }

        /// <summary>Runs a function on the Rhino UI thread (where Grasshopper solves) and waits for it.</summary>
        private static Dictionary<string, object> RunOnUiThread(Func<Dictionary<string, object>> func, int timeoutMs)
        {
            Dictionary<string, object> result = null;
            Exception error = null;
            using (var done = new ManualResetEvent(false))
            {
                Action action = () =>
                {
                    try { result = func(); }
                    catch (Exception ex) { error = ex; }
                    finally { try { done.Set(); } catch (ObjectDisposedException) { } }
                };
                Rhino.RhinoApp.InvokeOnUiThread(action);
                if (!done.WaitOne(timeoutMs))
                    return Error("Timed out after " + (timeoutMs / 1000) + " s waiting for the Grasshopper solution.");
            }
            if (error != null) return Error(error.Message);
            return result;
        }

        private Dictionary<string, object> BuildInfo()
        {
            List<GH_NumberSlider> vars = GetSliders(InVariables, null);
            List<GH_NumberSlider> pars = GetSliders(InParameters, null);
            List<string> varNames = UniqueNames(vars.Select(s => s.NickName).ToList(), "x");
            List<string> parNames = UniqueNames(pars.Select(s => s.NickName).ToList(), "p");
            Snapshot snap;
            lock (_snapshotLock) snap = _snapshot;

            return new Dictionary<string, object>
            {
                { "ok", true },
                { "document", OnPingDocument()?.DisplayName ?? "" },
                { "output_path", _outputPath },
                { "sampling_method", _samplingMethod },
                { "variables", vars.Select((s, i) => SliderInfo(s, varNames[i])).ToList() },
                { "parameters", pars.Select((s, i) => SliderInfo(s, parNames[i])).ToList() },
                { "objective_names", snap.ObjectiveNames },
                { "weights", snap.Weights },
                { "objectives", snap.Objectives },
                { "constraint_names", snap.ConstraintNames },
                { "limits", snap.Limits },
                { "types", snap.Types },
                { "constraints", snap.Constraints },
            };
        }

        private Dictionary<string, object> Evaluate(List<double> x, List<double> p, bool countEvaluation)
        {
            GH_Document doc = OnPingDocument();
            if (doc == null) throw new InvalidOperationException("Component is not in a Grasshopper document.");
            if (!doc.Enabled) throw new InvalidOperationException("The Grasshopper solver is disabled (unlock it to evaluate).");
            if (doc.SolutionState == GH_ProcessStep.Process)
                throw new InvalidOperationException("A Grasshopper solution is already in progress.");

            List<GH_NumberSlider> vars = GetSliders(InVariables, null);
            List<GH_NumberSlider> pars = GetSliders(InParameters, null);
            if (x != null && x.Count != vars.Count)
                throw new ArgumentException("Expected " + vars.Count + " variable value(s) but received " + x.Count + ".");
            if (p != null && p.Count > 0 && p.Count != pars.Count)
                throw new ArgumentException("Expected " + pars.Count + " parameter value(s) but received " + p.Count + ".");

            if (x != null) for (int i = 0; i < vars.Count; i++) SetSlider(vars[i], x[i]);
            if (p != null && p.Count > 0) for (int i = 0; i < pars.Count; i++) SetSlider(pars[i], p[i]);

            var watch = Stopwatch.StartNew();
            doc.NewSolution(false);
            watch.Stop();
            if (countEvaluation) _evaluationCount++;

            Snapshot snap;
            lock (_snapshotLock) snap = _snapshot;

            // collect errors raised anywhere in the definition so the driver can flag failed solutions
            var ghErrors = new List<string>();
            foreach (IGH_DocumentObject obj in doc.Objects)
            {
                if (obj is IGH_ActiveObject active && active.RuntimeMessageLevel == GH_RuntimeMessageLevel.Error)
                    foreach (string m in active.RuntimeMessages(GH_RuntimeMessageLevel.Error))
                        ghErrors.Add(active.NickName + ": " + m);
            }

            var violations = new List<double>();
            for (int i = 0; i < snap.Constraints.Count; i++)
                violations.Add(Violation(snap.Constraints[i], snap.Limits[i], snap.Types[i]));

            return new Dictionary<string, object>
            {
                { "ok", true },
                { "variables", vars.Select(s => (double)s.Slider.Value).ToList() },
                { "parameters", pars.Select(s => (double)s.Slider.Value).ToList() },
                { "objectives", snap.Objectives },
                { "weights", snap.Weights },
                { "objective", WeightedSum(snap.Objectives, snap.Weights) },
                { "constraints", snap.Constraints },
                { "limits", snap.Limits },
                { "types", snap.Types },
                { "violations", violations },
                { "solve_time", watch.Elapsed.TotalSeconds },
                { "gh_errors", ghErrors },
                { "evaluation", _evaluationCount },
            };
        }

        private static void SetSlider(GH_NumberSlider slider, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException("Non-finite value sent for slider \"" + slider.NickName + "\".");
            decimal min = slider.Slider.Minimum, max = slider.Slider.Maximum;
            decimal v;
            try { v = (decimal)value; }
            catch (OverflowException) { v = value > 0 ? max : min; }
            if (v < min) v = min;
            if (v > max) v = max;

            // Change the value silently, then expire without triggering a solution per slider;
            // a single NewSolution is run after all sliders are set.
            bool raise = slider.Slider.RaiseEvents;
            slider.Slider.RaiseEvents = false;
            try { slider.SetSliderValue(v); }
            finally { slider.Slider.RaiseEvents = raise; }
            slider.ExpireSolution(false);
        }

        // ===================================================================== lifecycle

        public override void RemovedFromDocument(GH_Document document)
        {
            StopServer();
            base.RemovedFromDocument(document);
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded)
                StopServer();
            base.DocumentContextChanged(document, context);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("d5080fd5-ca02-4447-91f9-ed3a3a2494d8");
    }
}
