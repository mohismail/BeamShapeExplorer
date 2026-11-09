# BSE Optimization Sampler

This tool runs many constrained shape optimizations of a Grasshopper definition and saves the
results to a CSV file. It follows the sample-collection method in the BSE *Accessible design
methods* paper, split into two separate steps:

1. **Sampling plan** (`sampling_plan.py`): draw design-input samples (span, loads, f'c, …)
   inside their bounds and pick a start point for each run. The default is **Latin Hypercube
   Sampling (LHS)**, as in the paper; other sampling methods are available (see
   [Sampling methods](#sampling-methods)). The plan is saved as a CSV that you can review, plot,
   edit or replace before anything is optimized.
2. **Optimization sampling** (`bse_optimization_sampler.py`): for each plan row, hold the
   inputs fixed and run a **constrained optimization** (COBYLA by default) of the geometric
   design variables. The objective is minimized subject to the structural and other constraints
   computed by BSE. The driver writes one CSV row per run, with the inputs, the final variables,
   the final objective and constraint values, a success/status code and the run time. You can
   use the rows as training data for a surrogate model (for example, a neural network).

There are three parts:

| Part | Where | Role |
|---|---|---|
| **Optimization Sampler** component | `BeamShapeExplorer/Optimization/OptimizationSampler.cs` (tab *Beam Shape Explorer → Optimization*) | Reads the variable and parameter sliders, collects the objectives and constraints, and serves them on a localhost TCP socket. |
| **Sampling plan script** | `python/sampling_plan.py` | Step 1: reads the slider bounds from the component (or from a saved `.meta.json`) and writes the sampling plan CSV. It doesn't change anything in the definition. |
| **Python driver** | `python/bse_optimization_sampler.py` | Step 2: connects to the component, runs an optimization for every plan row through Grasshopper/Rhino and writes the results CSV. |

The paper linked Grasshopper and NLopt with Socket.IO. This implementation uses plain TCP with
newline-delimited JSON instead, so there are no extra dependencies on either side.

---

## 1. Grasshopper setup

Place **Optimization Sampler** on the canvas and wire it as follows.

| Input | What to connect | Notes |
|---|---|---|
| **Variables (V)** | Any number of **Number Sliders** (one wire per slider) | These are the optimization design variables. **The slider Min/Max are the optimization bounds.** |
| **Sample Parameters (P)** | *(optional)* Any number of **Number Sliders** | These are the design inputs sampled between runs (e.g. span, LL, SIDL, f'c) and held fixed during each run. The slider Min/Max are the sampling bounds. |
| **Objectives (O)** | Any number of numbers or wires | All values are flattened and minimized as a weighted sum. |
| **Objective Weights (W)** | *(optional)* Numbers | One weight per objective. The default is 1.0, and the last weight repeats if the list is shorter. Give an objective a weight of 0 to record it in the CSV without optimizing it. |
| **Constraints (C)** | *(optional)* Any number of numbers or wires | All values are flattened. Each value is checked as `value [type] limit`. |
| **Constraint Limits (CL)** | *(optional)* Numbers | The default is 0.0. The last value repeats, so one value applies to every constraint. |
| **Constraint Types (CT)** | *(optional)* Text: `<=`, `>=` or `=` | The default is `<=`. The last value repeats. |
| **Port** | Integer | The default is 8765. It must match `--port` in Python. |
| **Listen (L)** | **Boolean Toggle** | Set it to True to start the server. |
| **Output Path (Out)** | *(optional)* Text, e.g. from a Panel | Where the results CSV is saved. It can be a file (`D:\studies\beam01\runs.csv`) or a folder, in which case the file is named `optimization_samples.csv`. A relative path such as `results\runs.csv` is resolved from the folder of the saved `.gh` file. Missing folders are created. |
| **Sampling Method (SM)** | *(optional)* Text, e.g. from a Panel or a Value List | How the Sample Parameters are sampled: `lhs` (default), `lhs-optimized`, `lhs-centered`, `sobol`, `halton`, `random` or `grid`. See [Sampling methods](#sampling-methods). |

The **Status** output also shows where the results will be saved. The **Status**, **Variable Names**, **Lower/Upper Bounds**, **Weighted Objective**,
**Constraint Violation** and **Evaluations** outputs let you check the setup before you start
the Python driver.

Typical BSE example:

* **Variables**: sliders for the section depths and widths at the control points of the shaped beam.
* **Sample Parameters**: sliders for L, h_max, b, LL, SIDL and f'c.
* **Objectives**: the embodied carbon from *Total Cost*. Optionally add the mass with weight 0, so it is recorded but not optimized.
* **Constraints**: demand/capacity ratios from the flexure, shear and deflection components with limits of 1 and type `<=`, plus geometric checks such as clear cover or effective flange width.

> **Slider precision matters.** Grasshopper rounds slider values to the slider's decimal places,
> which turns the objective into a staircase. Gradient-free optimizers such as COBYLA then stop
> early on poor designs. Give every variable slider **at least 5 decimal places**. The component
> and the driver both warn about coarse sliders. Integer sliders are allowed, but the optimizers
> treat them as continuous.

The component and the driver name each variable and parameter after the slider **NickName**,
so give the sliders meaningful names. The CSV columns use these names. Objective and constraint
names come from the wired sources, e.g. `Flex.ratio[3]`.

## 2. Python environment setup

The driver runs in a normal, **external** Python installation, not in Rhino's built-in Python.
You only need to do this setup once per computer.

> **Why not Rhino's Python?** Grasshopper solves on Rhino's UI thread. A script running inside
> Rhino's own script editor would block that thread, and every request to the component would
> time out. Always run the driver from a terminal (PowerShell, Command Prompt or Terminal).

### Requirements

| Package | Version | Needed for |
|---|---|---|
| Python | 3.8 or newer (3.10–3.12 recommended) | Running the driver |
| numpy | ≥ 1.20 | Everything |
| scipy | ≥ 1.8 | The optimizers (COBYLA, SLSQP, …) and the `lhs-optimized`, `lhs-centered`, `sobol` and `halton` sampling methods |
| nlopt | *optional* | The `nlopt:<ALGO>` algorithms, such as the NLopt COBYLA used in the BSE paper |

### Step 1 – Install Python

**Windows:** download the installer from [python.org/downloads](https://www.python.org/downloads/).
On the first screen of the installer, tick **"Add python.exe to PATH"**, then choose
*Install Now*. Afterwards, open a **new** PowerShell window and check the installation:

```powershell
py --version
```

**macOS:** install Python from [python.org/downloads](https://www.python.org/downloads/) or with
Homebrew (`brew install python`), then check it in Terminal:

```bash
python3 --version
```

On Windows the `py` launcher is the most reliable command. On macOS use `python3`. The commands
below use these names until the virtual environment is active; after that, plain `python` works
everywhere.

### Step 2 – Get the scripts

Clone the repository, or download it from GitHub as a ZIP (*Code → Download ZIP*) and extract it.
Then open a terminal in the repository folder, the one that contains the `python` folder:

```powershell
cd C:\path\to\BeamShapeExplorer
```

### Step 3 – Create and activate a virtual environment

A virtual environment keeps these packages separate from other Python projects. Create it once:

```powershell
# Windows
py -m venv .venv
```

```bash
# macOS
python3 -m venv .venv
```

Then **activate it every time you open a new terminal** to run the driver:

| Shell | Command |
|---|---|
| Windows PowerShell | `.venv\Scripts\Activate.ps1` |
| Windows Command Prompt | `.venv\Scripts\activate.bat` |
| macOS Terminal (bash/zsh) | `source .venv/bin/activate` |

When it is active, the prompt starts with `(.venv)`. To leave the environment, run `deactivate`.

> **PowerShell says "running scripts is disabled on this system"?** Allow local scripts for
> your user once, then activate again:
>
> ```powershell
> Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
> ```

### Step 4 – Install the packages

With the environment active:

```powershell
python -m pip install --upgrade pip
python -m pip install -r python/requirements.txt
```

Optionally, add NLopt:

```powershell
python -m pip install nlopt
```

### Step 5 – Check the installation

```powershell
python -c "import numpy, scipy; print('numpy', numpy.__version__, '| scipy', scipy.__version__)"
python python/bse_optimization_sampler.py --list-sampling
```

The first command prints the package versions, and the second lists the sampling methods. To
test the full workflow without Rhino, use the mock server described in
[Testing without Rhino](#4-testing-without-rhino).

### Alternative: conda

If you already use Anaconda or Miniconda, you can use a conda environment instead of steps 3–4:

```bash
conda create -n bse python=3.11 numpy scipy
conda activate bse
```

Optionally, add NLopt from conda-forge:

```bash
conda install -c conda-forge nlopt
```

### Troubleshooting

| Problem | Fix |
|---|---|
| `python` or `py` is not recognized, or it opens the Microsoft Store | Python is not on PATH. Re-run the installer, choose *Modify*, and tick *Add Python to environment variables*. Alternatively, turn off the `python.exe` entries under Windows *Settings → Apps → Advanced app settings → App execution aliases*. |
| `ModuleNotFoundError: No module named 'numpy'` (or `scipy`) | The virtual environment is not active in this terminal. Activate it (step 3) and try again. |
| `Sampling method '…' needs scipy >= 1.8` or `unexpected keyword argument 'optimization'` | scipy is too old. Run `python -m pip install --upgrade scipy`. |
| `pip install nlopt` fails | No prebuilt NLopt package exists for your Python version. Use the conda-forge package, or use the SciPy algorithms (the default COBYLA needs no extra packages). |
| `Could not connect to Grasshopper at 127.0.0.1:8765` | In Grasshopper, set the component's **Listen** input to True and check that its **Port** matches `--port`. The connection stays on your own computer, so the firewall should not need changes. |

## 3. Running

Before either step:

1. Open the definition in Rhino/Grasshopper and set **Listen** to True. The component message reads `Listening :8765`.
2. Open a terminal in the repository folder and activate the virtual environment
   (`.venv\Scripts\Activate.ps1` on Windows, `source .venv/bin/activate` on macOS; see
   [Python environment setup](#2-python-environment-setup)).

### Step 1 – Create the sampling plan

```bash
python python/sampling_plan.py --samples 200 --sampling lhs --out study/plan.csv
```

The script reads the slider names and bounds from the component, prints a preview of the first
rows, and writes two files:

* `plan.csv`, with one row per optimization run:

  | Column | Content |
  |---|---|
  | `run` | Run number. It becomes the `run` column of the results. |
  | `param:<name>` | Value of each Sample Parameter slider, held fixed during that run |
  | `x0:<name>` | Start value of each Variable slider for that run |

* `plan.meta.json`, with the bounds and the settings used (method, start mode, seed, grid levels).

Plan options (see `python python/sampling_plan.py --help`):

| Option | Default | Meaning |
|---|---|---|
| `--out FILE` | `sampling_plan.csv` | Plan CSV to write |
| `--samples N` | 50 | Number of runs in the plan |
| `--sampling METHOD` | component Sampling Method, else `lhs` | How the parameters (and sampled start points) are drawn. Overrides the component's Sampling Method. See [Sampling methods](#sampling-methods). |
| `--grid-levels` | – | For `grid` only: levels per dimension, as one value (`5`) or one per dimension (`3,5,4`) |
| `--start auto\|sampled\|random\|center\|current` | `auto` | Start point of each run. `sampled` draws the start points with the sampling method, together with the parameters (`lhs` is accepted as an older name). `current` uses the slider values at the time the plan is made. `auto` uses `current` when there are Sample Parameters; without them it uses `sampled`, which gives a multi-start study of the same problem. |
| `--seed` | 0 | Random seed. The same settings and seed always give the same plan. |
| `--from-meta FILE` | – | Read the slider bounds from a `.meta.json` written by an earlier plan or study, instead of connecting to Grasshopper. This makes plans without Rhino running. |
| `--preview N` | 5 | Number of rows to print |
| `--port`, `--host`, `--timeout` | 8765, 127.0.0.1, 60 s | Connection settings |

#### Editing or writing a plan yourself

A plan is an ordinary CSV, so you can open it in Excel, delete or add rows, or create one with
another tool. When the driver reads a plan, it checks it against the definition:

* Every Sample Parameter needs a column, named `param:<name>` or just `<name>`.
* `x0:<name>` columns are optional. A missing column or blank cell means "start from the current
  slider value".
* The `run` column is optional; without it, the rows are numbered from 0. Run numbers must be unique.
* Values outside the slider bounds are clipped to them, and unknown columns (for example your own
  notes) are ignored. The driver reports both before it starts.

For example, this is a valid plan for a definition with `L` and `w` parameters:

```csv
L,w
6,10
8,20
10,30
```

### Sampling methods

Choose the method with the component's **Sampling Method** input or with `--sampling`, which
overrides the component. `python python/sampling_plan.py --list-sampling` prints the list below.

| Method | Description | When to use it |
|---|---|---|
| `lhs` (default) | Random Latin Hypercube: each parameter's range is split into N equal strata with exactly one sample in each. | The method in the BSE paper. A good general choice for surrogate-model training data. |
| `lhs-optimized` | Latin Hypercube rearranged to fill the space more evenly (lower centered discrepancy) | Small to medium sample counts, where clusters and gaps matter most |
| `lhs-centered` | Latin Hypercube with each sample at the center of its stratum | Even coverage without jitter inside each stratum |
| `sobol` | Scrambled Sobol low-discrepancy sequence | Very even coverage, especially with many parameters. Use a power-of-2 sample count (64, 128, 256…); the driver reminds you if you don't. |
| `halton` | Scrambled Halton low-discrepancy sequence | Similar to Sobol, with no power-of-2 preference |
| `random` | Uniform random (Monte Carlo) | Baselines or unbiased random studies. Expect clusters and gaps. |
| `grid` | Full-factorial grid that includes the slider Min and Max | Few parameters, or when results are needed on a regular grid (for example for contour plots) |

All methods use `--seed`, so a study can be repeated exactly. Every method except `grid` uses
`--samples` runs. A grid has *levels^dimensions* runs: by default the driver uses the largest
equal number of levels that stays within `--samples`, with at least 2 levels. For example,
`--samples 100` with 2 parameters gives a 10 × 10 grid. `--grid-levels` sets the levels
directly, e.g. `--grid-levels 3,6` for 18 runs. The run count grows quickly with more
parameters, and the plan preview shows it before anything runs.

### Step 2 – Run the optimizations

```bash
python python/bse_optimization_sampler.py --plan study/plan.csv --out study/runs.csv
```

The driver runs one constrained optimization per plan row and writes the results to the
component's **Output Path**. `--out` overrides the component. If neither is set, the driver
writes `optimization_samples.csv` in the folder it was run from. It prints the location it uses,
and where that location came from, when it starts.

Optimization options (see `python python/bse_optimization_sampler.py --help`):

| Option | Default | Meaning |
|---|---|---|
| `--plan FILE` | – | The sampling plan to run. Without it, see [One-step shortcut](#one-step-shortcut). |
| `--out PATH` | component Output Path | CSV file or folder for the results. Overrides the component's Output Path. |
| `--resume` | off | Continue an interrupted study: skip the plan runs already in the results CSV and append the rest. See [Resuming](#resuming-an-interrupted-study). |
| `--append` | off | Add the rows to an existing results CSV instead of overwriting it |
| `--algorithm` | `COBYLA` | `COBYLA`, `COBYQA`, `SLSQP`, `trust-constr` (SciPy) or `nlopt:LN_COBYLA`, `nlopt:LN_BOBYQA`, … |
| `--maxiter` | 500 | Maximum iterations/evaluations per run |
| `--tol` | 1e-4 | Convergence tolerance |
| `--rhobeg` | 0.1 | Initial step as a fraction of each slider's range (COBYLA/COBYQA/NLopt) |
| `--feas-tol` | 1e-4 | Largest constraint violation still counted as feasible |
| `--fail-on-gh-error` | off | Treat a solution as failed if any Grasshopper component shows an error |
| `--port`, `--host`, `--timeout` | 8765, 127.0.0.1, 600 s | Connection settings |

The optimizer works in normalized `[0, 1]` coordinates of each slider range, so variables with
very different units are treated evenly. The variables never leave the slider bounds. When the
study finishes or is interrupted, the driver restores the original slider values; use
`--no-restore` to keep the last ones.

### One-step shortcut

If you don't need to review the plan first, leave out `--plan`. The driver then makes the plan
itself, with the same options as `sampling_plan.py` (`--samples`, `--sampling`, `--grid-levels`,
`--start`, `--seed`):

```bash
python python/bse_optimization_sampler.py --samples 200 --sampling sobol --out study/runs.csv
```

The plan is still saved separately, as `study/runs.plan.csv` next to the results, so you can
inspect it later, rerun it with `--plan`, or resume from it. With `--append`, each new batch gets
its own seed stream, run numbers that continue from the existing results, and its own plan file
(`runs.plan.<first run>.csv`), so samples are not repeated.

### Resuming an interrupted study

Results are saved after every run, so an interrupted study (Ctrl+C, a Rhino crash, a power cut)
loses at most the run in progress. To finish it, run the same command again with `--resume`:

```bash
python python/bse_optimization_sampler.py --plan study/plan.csv --out study/runs.csv --resume
```

The driver skips every run number that is already in `runs.csv` and appends the rest. Without
`--plan`, it uses the `runs.plan.csv` saved next to the results.

### Output

`runs.csv` contains one row per optimization run of the plan:

| Column(s) | Content |
|---|---|
| `run` | Run number from the plan |
| `param:<name>` | Sample parameter values used, after slider rounding |
| `x0:<name>` | Start point of the variables, from the plan |
| `x:<name>` | Final variable values, as applied to the sliders |
| `objective` | Final weighted objective |
| `obj:<name>` | Each final objective value |
| `con:<name>` | Each final constraint value |
| `max_violation` | Largest constraint violation; 0 means all constraints are satisfied |
| `feasible` | 1 if `max_violation <= --feas-tol` and the solution is valid |
| `success` | 1 if the optimizer reports convergence |
| `status` | Optimizer status code: the SciPy `status` or the NLopt result code. `-100` means the run raised an error. |
| `message` | Optimizer message or error text |
| `n_evals`, `nit` | Grasshopper evaluations and iterations used in the run |
| `gh_solve_time_s` | Time spent inside Grasshopper solutions |
| `time_s` | Wall-clock time of the run |
| `gh_errors` | Grasshopper error messages at the final state, if any |

The driver writes and flushes each row as soon as its run finishes, so a crash or Ctrl+C keeps
the completed runs. It also writes `runs.meta.json` next to the CSV with the bounds, names,
constraint types/limits, settings and the plan that was run, which you can reuse when you fit a
surrogate model.

## 4. Testing without Rhino

`mock_gh_server.py` speaks the same protocol for a toy shaped beam (5 depth variables; span and
load parameters; flexural utilization `<= 1` and end-depth `>=` constraints):

```bash
python python/mock_gh_server.py --port 8765
```

Then, in a second terminal:

```bash
python python/sampling_plan.py --port 8765 --samples 10 --out mock_plan.csv
```

```bash
python python/bse_optimization_sampler.py --port 8765 --plan mock_plan.csv --out mock_runs.csv
```

## 5. Protocol (for custom drivers)

Each request is one JSON object per line, sent to `127.0.0.1:<Port>`, and each reply is one JSON
object per line. Every reply has `"ok": true|false`; a failed request also has `"error"`.

| `cmd` | Request fields | Reply |
|---|---|---|
| `ping` | – | `{"ok":true,"message":"pong"}` |
| `info` | – | `document`, `output_path`, `variables` / `parameters` (`name`, `min`, `max`, `value`, `decimals`, `integer`), `objective_names`, `weights`, `objectives`, `constraint_names`, `limits`, `types`, `constraints` |
| `evaluate` | `variables: [..]`, optional `parameters: [..]`, optional `timeout` (s) | Sets the sliders, solves once and returns the applied `variables`/`parameters`, `objectives`, `weights`, `objective`, `constraints`, `limits`, `types`, `violations`, `solve_time`, `gh_errors`, `evaluation` |
| `set` | Same as `evaluate` | Same as `evaluate`, but the call does not add to the evaluation counter (used to restore the sliders) |
| `close` | – | Closes the connection |

Non-finite numbers are sent as `null`.
