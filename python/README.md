# BSE Optimization Sampler

This tool runs many constrained shape optimizations of a Grasshopper definition and saves the
results to a CSV file. It follows the sample-collection method in the BSE *Accessible design
methods* paper, split into two separate steps:

1. **Sampling plan** (the **Sampling Plan** component, or `sampling_plan.py`): draw design-input samples (span, loads, f'c, …)
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

There are four parts, all on the *Beam Shape Explorer → Optimization* tab or in `python/`:

| Part | Where | Role |
|---|---|---|
| **Sampling Plan** component | `BeamShapeExplorer/Optimization/SamplingPlanComponent.cs` | Step 1: owns the design inputs (Sample Parameter sliders) and the sampling settings. Creates the plan on the canvas, previews it, and can save it to or load it from CSV. |
| **Optimization Sampler** component | `BeamShapeExplorer/Optimization/OptimizationSampler.cs` | Step 2: takes the variables, objectives, constraints and the plan, and serves them on a localhost TCP socket for the Python driver. |
| **Sampling plan script** *(optional)* | `python/sampling_plan.py` | Makes a plan CSV from the command line instead of the component, e.g. without Rhino running. |
| **Python driver** | `python/bse_optimization_sampler.py` | Step 2: connects to the Optimization Sampler, runs an optimization for every plan row through Grasshopper/Rhino and writes the results CSV. |

The paper linked Grasshopper and NLopt with Socket.IO. This implementation uses plain TCP with
newline-delimited JSON instead, so there are no extra dependencies on either side.

---

## 1. Download and build BSE

The Sampling Plan and Optimization Sampler components are part of the Beam Shape Explorer (BSE)
Grasshopper plugin, so you build the plugin from this repository and install it in Grasshopper.

> The prebuilt `BeamShapeExplorer/BSE.zip` in the repository is an older (2021) build. It does
> **not** contain the Optimization components, so build the plugin as described below.

### Download the repository

Use any one of these options. Choose a folder that is easy to find, such as
`C:\Users\<you>\Documents\BeamShapeExplorer`.

**Option A – Git (command line).** Install [Git](https://git-scm.com/downloads), then in a terminal:

```bash
git clone https://github.com/mohismail/BeamShapeExplorer.git
```

```bash
cd BeamShapeExplorer
```

**Option B – Visual Studio.** On the start window choose **Clone a repository** (or
**Git → Clone Repository…**), enter `https://github.com/mohismail/BeamShapeExplorer` and click
**Clone**.

**Option C – ZIP download (no Git).** On the
[repository page](https://github.com/mohismail/BeamShapeExplorer), choose the branch you need
(see the note below) from the branch menu, then **Code → Download ZIP**, and extract the ZIP.
With this option you download the files again to get updates.

> **Which branch?** Until the Optimization components are merged into the main branch, they are
> on the `claude/youthful-newton-bm3a2i` branch. Switch to it after cloning:
>
> * Git: `git checkout claude/youthful-newton-bm3a2i`
> * Visual Studio: **Git → Fetch**, then click the branch name in the bottom-right of the status
>   bar and pick **Remotes → origin → claude/youthful-newton-bm3a2i**.
>
> You have the right branch if the `BeamShapeExplorer/Optimization` folder and the `python`
> folder exist.

To get later updates, run `git pull` in the repository folder, or use **Git → Pull** in
Visual Studio. Then build again.

### Build the plugin (Windows)

1. Install **Visual Studio 2019 or 2022** (Community is free) with the **.NET desktop
   development** workload. Under *Individual components*, also select the **.NET Framework 4.8
   targeting pack**.
2. Open `BeamShapeExplorer.sln` from the repository folder.
3. In *Solution Explorer*, right-click the solution and choose **Restore NuGet Packages**. This
   downloads the Grasshopper and RhinoCommon references; you don't need Rhino's SDK installed.
4. Set the configuration in the toolbar to **Release**.
5. Choose **Build → Build Solution**. The *Output* window should end with `Build: 1 succeeded`.

The plugin is written to **`BeamShapeExplorer\bin\BeamShapeExplorer.gha`**. The build renames
the `.dll` to `.gha` automatically, so the `.gha` is the file to install.

The plugin is built against Rhino 6, and that build also loads in Rhino 7 and Rhino 8.

**Command-line alternative.** In a *Developer PowerShell for Visual Studio* (Visual Studio 2019
16.5 or later), from the repository folder:

```powershell
msbuild BeamShapeExplorer.sln -restore -p:RestorePackagesConfig=true -p:Configuration=Release
```

### Install it in Grasshopper

1. Start Rhino and run the `Grasshopper` command.
2. In Grasshopper, choose **File → Special Folders → Components Folder**. This opens the
   Libraries folder, usually `%APPDATA%\Grasshopper\Libraries`.
3. Close Rhino completely.
4. If an older `BeamShapeExplorer.gha` is in the Libraries folder (or a subfolder), delete it.
   Grasshopper won't load two copies.
5. Copy the new `BeamShapeExplorer\bin\BeamShapeExplorer.gha` into the Libraries folder. A
   subfolder such as `Libraries\BeamShapeExplorer\` also works.
6. If the `.gha` was downloaded or emailed rather than built on this computer, right-click it,
   choose **Properties**, tick **Unblock** and click **OK**.
7. Start Rhino and Grasshopper again.

Open the **Beam Shape Explorer** tab. The **Optimization** panel should contain **Sampling
Plan** and **Optimization Sampler**. After each new build, close Rhino and repeat steps 4–7.

| Problem | Fix |
|---|---|
| Build error about missing NuGet packages or `RhinoCommon.targets` | Run **Restore NuGet Packages** (step 3 of the build), then build again. |
| Build error about the .NET Framework 4.8 targeting pack | Install it with the *Visual Studio Installer* (*Modify → Individual components*). |
| Other BSE components appear, but the Optimization panel is missing | Grasshopper is loading an old `.gha`. Search the Libraries folder for other copies, and check that you built the right branch. |
| No Beam Shape Explorer tab at all | Check that the `.gha` is unblocked and in the Libraries folder. In Rhino, the `GrasshopperDeveloperSettings` command lists the folders Grasshopper loads from. |
| Rhino 8 shows a load error for the plugin | Rhino 8 runs on .NET Core by default. Run the `SetDotNetRuntime` command, choose **NETFramework**, and restart Rhino. |
| You use Rhino for Mac | Building the solution needs Windows. Loading the Windows-built `.gha` in Rhino for Mac hasn't been tested. |

## 2. Grasshopper setup

The definition uses two components, one per step:

```
 design-input sliders ──► Sampling Plan ──Plan──► Optimization Sampler ◄── variable sliders
 (span, loads, f'c…)      (step 1)                (step 2)              ◄── objectives, constraints
```

### Sampling Plan component (step 1)

| Input | What to connect | Notes |
|---|---|---|
| **Sample Parameters (P)** | Any number of **Number Sliders** | The design inputs sampled between runs (e.g. span, LL, SIDL, f'c) and held fixed during each run. **The slider Min/Max are the sampling bounds.** |
| **Variables (V)** | *(optional)* The same variable sliders that go into the Optimization Sampler | Needed only for start points made by the plan (Start = `sampled`, `random` or `center`). |
| **Samples (N)** | Integer, default 50 | Number of optimization runs. Grid sampling uses Grid Levels instead. |
| **Sampling Method (M)** | Text, default `lhs` | `lhs`, `lhs-optimized`, `lhs-centered`, `sobol`, `halton`, `random` or `grid`. See [Sampling methods](#sampling-methods). |
| **Start (St)** | Text, default `auto` | Where each run starts: `sampled` (drawn with the sampling method), `random`, `center`, or `current` (the Optimization Sampler's **Start Point**, or else the variable values when the study begins). `auto` means `current` when there are Sample Parameters, otherwise `sampled`. |
| **Seed (S)** | Integer, default 0 | The same inputs and seed always give the same plan. |
| **Grid Levels (GL)** | *(optional)* Integers | For `grid`: levels per sampled dimension, one value or one per dimension. |
| **Plan File (F)** | *(optional)* Text | CSV path for Save or Load. A relative path is resolved from the folder of the saved `.gh` file. |
| **Load (Ld)** | Boolean, default false | Load the plan from Plan File instead of generating it, e.g. a plan edited in Excel. See [Editing or writing a plan yourself](#editing-or-writing-a-plan-yourself). |
| **Save (Sv)** | Button or Toggle | Save the generated plan, plus a `.meta.json` with its settings, to Plan File. |

Outputs: **Plan** (connect it to the Optimization Sampler), **Parameter Values** and **Start
Points** (one branch per run, handy for previewing or plotting the plan on the canvas), **Runs**,
and **Status** (a summary plus any notes, such as clipped values when a CSV is loaded).

The plan only changes when its inputs change, not while the optimizer moves the sliders. Save
writes the file when the plan changes or when Save is switched on, not on every solution.

### Optimization Sampler component (step 2)

| Input | What to connect | Notes |
|---|---|---|
| **Variables (V)** | Any number of **Number Sliders** (one wire per slider) | These are the optimization design variables. **The slider Min/Max are the optimization bounds.** |
| **Sampling Plan (Plan)** | *(optional)* The **Plan** output of the Sampling Plan component | The runs to optimize. Without a plan, the driver makes a multi-start plan over the variables, or runs a CSV given with `--plan`. |
| **Objectives (O)** | Any number of numbers or wires | All values are flattened and minimized as a weighted sum. |
| **Objective Weights (W)** | *(optional)* Numbers | One weight per objective. The default is 1.0, and the last weight repeats if the list is shorter. Give an objective a weight of 0 to record it in the CSV without optimizing it. |
| **Constraints (C)** | *(optional)* Any number of numbers or wires | All values are flattened. Each value is checked as `value [type] limit`. |
| **Constraint Limits (CL)** | *(optional)* Numbers | The default is 0.0. The last value repeats, so one value applies to every constraint. |
| **Constraint Types (CT)** | *(optional)* Text: `<=`, `>=` or `=` | The default is `<=`. The last value repeats. |
| **Port** | Integer | The default is 8765. It must match `--port` in Python. |
| **Listen (L)** | **Boolean Toggle** | Set it to True to start the server. |
| **Output Path (Out)** | *(optional)* Text, e.g. from a Panel | Where the results CSV is saved. It can be a file (`D:\studies\beam01\runs.csv`) or a folder, in which case the file is named `optimization_samples.csv`. A relative path such as `results\runs.csv` is resolved from the folder of the saved `.gh` file. Missing folders are created. |
| **Start Point (X0)** | *(optional)* Numbers, one per variable in the order of the Variables input | Where each run's optimization starts. Values outside a slider's Min/Max are clipped, and a null entry keeps that slider's value. It is used by every run that has no start point of its own in the plan (see below). Without it, runs start from the current slider values. |

The **Status** output shows the connected plan and where the results will be saved. The
**Status**, **Variable Names**, **Lower/Upper Bounds**, **Weighted Objective**, **Constraint
Violation** and **Evaluations** outputs let you check the setup before you start the Python
driver. The Optimization Sampler finds the Sample Parameter sliders through the plan, so don't
wire them into the Optimization Sampler as well. If the plan has start points made for different
Variable sliders than the ones connected here, it warns and ignores them.

**Where each run starts.** For each variable, the driver uses the first of these that is set:

1. the run's start point in the Sampling Plan (`x0:<name>` in a plan CSV, or Start = `sampled`, `random` or `center` on the Sampling Plan component)
2. the Optimization Sampler's **Start Point**
3. the slider's current value when the study begins

So a typical setup is a plan with Start = `current` (the default with Sample Parameters) plus a
Start Point, for example a known good design, so every sampled run starts from that design. The
driver prints the start point it uses, and the sliders return to their original values when the
study ends.

Typical BSE example:

* **Sample Parameters** (Sampling Plan): sliders for L, h_max, b, LL, SIDL and f'c.
* **Variables** (Optimization Sampler): sliders for the section depths and widths at the control points of the shaped beam.
* **Objectives**: the embodied carbon from *Total Cost*. Optionally add the mass with weight 0, so it is recorded but not optimized.
* **Constraints**: demand/capacity ratios from the flexure, shear and deflection components with limits of 1 and type `<=`, plus geometric checks such as clear cover or effective flange width.

> **Slider precision matters.** Grasshopper rounds slider values to the slider's decimal places,
> which turns the objective into a staircase. Gradient-free optimizers such as COBYLA then stop
> early on poor designs. Give every variable slider **at least 5 decimal places**. The component
> and the driver both warn about coarse sliders. Integer sliders are allowed, but the optimizers
> treat them as continuous.

The components and the driver name each variable and parameter after the slider **NickName**,
so give the sliders meaningful names. The CSV columns use these names. Objective and constraint
names come from the wired sources, e.g. `Flex.ratio[3]`.

## 3. Python environment setup

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

### Step 2 – Open a terminal in the repository folder

The Python scripts are in the repository you downloaded in
[Download the repository](#download-the-repository). Open a terminal in the repository folder,
the one that contains the `python` folder. Run every command in this README from there:

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
[Testing without Rhino](#5-testing-without-rhino).

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

## 4. Running

Before running:

1. Open the definition in Rhino/Grasshopper and set the Optimization Sampler's **Listen** to
   True. The component message reads `Listening :8765`.
2. Open a terminal in the repository folder and activate the virtual environment
   (`.venv\Scripts\Activate.ps1` on Windows, `source .venv/bin/activate` on macOS; see
   [Python environment setup](#3-python-environment-setup)).

### Step 1 – Create the sampling plan

#### In Grasshopper (recommended)

Set up the **Sampling Plan** component as described in
[Sampling Plan component](#sampling-plan-component-step-1) and connect its Plan output to the
Optimization Sampler. Check the plan on the canvas, through the Parameter Values output or the
Status panel, and press **Save** to keep a copy as CSV. The driver receives the plan from
Grasshopper, so there is nothing to run in Python for this step.

#### With Python (optional)

`sampling_plan.py` makes the same kind of plan from the command line. Use it, for example, to
make plans without Rhino running:

```bash
python python/sampling_plan.py --samples 200 --sampling lhs --out study/plan.csv
```

It reads the slider names and bounds from Grasshopper (the Sample Parameters come from the
connected Sampling Plan component), prints a preview of the first rows, and writes two files:

* `plan.csv`, with one row per optimization run:

  | Column | Content |
  |---|---|
  | `run` | Run number. It becomes the `run` column of the results. |
  | `param:<name>` | Value of each Sample Parameter slider, held fixed during that run |
  | `x0:<name>` | Start value of each Variable slider for that run |

* `plan.meta.json`, with the bounds and the settings used (method, start mode, seed, grid levels).

The Sampling Plan component's **Save** writes exactly the same format, so plans from either
source can be loaded by the component (**Load**) or run by the driver (`--plan`).

Plan options (see `python python/sampling_plan.py --help`):

| Option | Default | Meaning |
|---|---|---|
| `--out FILE` | `sampling_plan.csv` | Plan CSV to write |
| `--samples N` | 50 | Number of runs in the plan |
| `--sampling METHOD` | Sampling Plan component's method, else `lhs` | How the parameters (and sampled start points) are drawn. See [Sampling methods](#sampling-methods). |
| `--grid-levels` | – | For `grid` only: levels per dimension, as one value (`5`) or one per dimension (`3,5,4`) |
| `--start auto\|sampled\|random\|center\|current` | `auto` | Start point of each run, as on the component (`lhs` is accepted for `sampled`). Unlike the component, `current` here writes the start values into the CSV: the Optimization Sampler's Start Point if it is set, else the slider values at the time the plan is made. |
| `--seed` | 0 | Random seed. The same settings and seed always give the same plan. |
| `--from-meta FILE` | – | Read the slider bounds from a `.meta.json` written by an earlier plan or study, instead of connecting to Grasshopper. This makes plans without Rhino running. |
| `--preview N` | 5 | Number of rows to print |
| `--port`, `--host`, `--timeout` | 8765, 127.0.0.1, 60 s | Connection settings |

#### Editing or writing a plan yourself

A plan is an ordinary CSV, so you can open it in Excel, delete or add rows, or create one with
another tool. Load it with the Sampling Plan component (**Plan File** + **Load**) or pass it to
the driver with `--plan`. Both check it against the sliders in the same way:

* Every Sample Parameter needs a column, named `param:<name>` or just `<name>`.
* `x0:<name>` columns are optional. A missing column or blank cell means "start from the current
  slider value".
* The `run` column is optional; without it, the rows are numbered from 0. Run numbers must be unique.
* Values outside the slider bounds are clipped to them, and unknown columns (for example your own
  notes) are ignored. Both are reported: by the component in its Status output, and by the driver
  before it starts.

For example, this is a valid plan for a definition with `L` and `w` parameters:

```csv
L,w
6,10
8,20
10,30
```

### Sampling methods

Choose the method with the Sampling Plan component's **Sampling Method** input, or with
`--sampling` when the plan is made in Python. `python python/sampling_plan.py --list-sampling`
prints the list below. The component and the Python scripts implement the same methods;
`sobol` and `halton` produce the same base sequences (Joe–Kuo direction numbers, as in SciPy),
with a random digital shift (Sobol) or digit permutation (Halton) seeded by **Seed**.

| Method | Description | When to use it |
|---|---|---|
| `lhs` (default) | Random Latin Hypercube: each parameter's range is split into N equal strata with exactly one sample in each. | The method in the BSE paper. A good general choice for surrogate-model training data. |
| `lhs-optimized` | Latin Hypercube arranged to fill the space more evenly. The component keeps the best of up to 100 random Latin Hypercubes by the largest minimum distance between points; Python minimizes the centered discrepancy (SciPy). | Small to medium sample counts, where clusters and gaps matter most |
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
python python/bse_optimization_sampler.py --out study/runs.csv
```

The driver runs one constrained optimization per plan row. It takes the plan from the first of
these that is available:

1. `--plan FILE`, a plan CSV given on the command line
2. the plan connected to the Optimization Sampler's **Sampling Plan** input
3. a plan it generates itself (see [One-step shortcut](#one-step-shortcut))

Whichever plan runs is saved next to the results as `runs.plan.csv`, so every study records
exactly what was run.

The results go to the Optimization Sampler's **Output Path**. `--out` overrides it. If neither is
set, the driver writes `optimization_samples.csv` in the folder it was run from. It prints the
location it uses, and where that location came from, when it starts.

Optimization options (see `python python/bse_optimization_sampler.py --help`):

| Option | Default | Meaning |
|---|---|---|
| `--plan FILE` | – | Run this plan CSV instead of the plan connected in Grasshopper |
| `--ignore-component-plan` | off | Generate the plan in Python (see [One-step shortcut](#one-step-shortcut)) even if a Sampling Plan is connected |
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

Without a Sampling Plan component or `--plan`, the driver makes the plan itself, with the same
options as `sampling_plan.py` (`--samples`, `--sampling`, `--grid-levels`, `--start`, `--seed`).
Without Sample Parameters, this is a multi-start study of the variables:

```bash
python python/bse_optimization_sampler.py --samples 64 --sampling sobol --out study/runs.csv
```

The plan is still saved separately, as `study/runs.plan.csv` next to the results, so you can
inspect it later, rerun it with `--plan`, or resume from it. If a Sampling Plan is connected,
these options are ignored (the driver says so) unless you add `--ignore-component-plan`.

With `--append`, each new batch gets run numbers that continue from the existing results and its
own plan file (`runs.plan.<first run>.csv`). A plan generated by the driver also gets its own
seed stream, so samples are not repeated. A plan from Grasshopper is run as it is, so change its
Seed between batches if you want new samples.

### Resuming an interrupted study

Results are saved after every run, so an interrupted study (Ctrl+C, a Rhino crash, a power cut)
loses at most the run in progress. To finish it, run the same command again with `--resume`:

```bash
python python/bse_optimization_sampler.py --plan study/plan.csv --out study/runs.csv --resume
```

The driver skips every run number that is already in `runs.csv` and appends the rest. It uses
the same plan as before: `--plan` if given, else the connected Sampling Plan, else the
`runs.plan.csv` saved next to the results.

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

## 5. Testing without Rhino

`mock_gh_server.py` speaks the same protocol for a toy shaped beam (5 depth variables; span and
load parameters; flexural utilization `<= 1` and end-depth `>=` constraints):

```bash
python python/mock_gh_server.py --port 8765
```

To mimic a connected Sampling Plan component, add `--plan-samples 10` (and optionally
`--plan-method sobol`, `--plan-start sampled`). To mimic a Start Point, add for example
`--start-point 0.3,0.3,0.3,0.3,0.3`. Then, in a second terminal, either run the
connected plan:

```bash
python python/bse_optimization_sampler.py --port 8765 --out mock_runs.csv
```

or make a plan in Python and run it:

```bash
python python/sampling_plan.py --port 8765 --samples 10 --out mock_plan.csv
```

```bash
python python/bse_optimization_sampler.py --port 8765 --plan mock_plan.csv --out mock_runs.csv
```

## 6. Protocol (for custom drivers)

Each request is one JSON object per line, sent to `127.0.0.1:<Port>`, and each reply is one JSON
object per line. Every reply has `"ok": true|false`; a failed request also has `"error"`.

| `cmd` | Request fields | Reply |
|---|---|---|
| `ping` | – | `{"ok":true,"message":"pong"}` |
| `info` | – | `document`, `output_path`, `start_point` (`null`, or one value per variable, `null` entries = current), `sampling_method`, `variables` / `parameters` (`name`, `min`, `max`, `value`, `decimals`, `integer`; the parameters are the connected plan's sliders), `plan` (`null`, or `runs`, `P`, `X0` (`null` without start points), `parameter_names`, `variable_names`, `method`, `start`, `seed`, `grid_levels`, `source`), `objective_names`, `weights`, `objectives`, `constraint_names`, `limits`, `types`, `constraints` |
| `evaluate` | `variables: [..]`, optional `parameters: [..]` (in the plan's parameter order), optional `timeout` (s) | Sets the sliders, solves once and returns the applied `variables`/`parameters`, `objectives`, `weights`, `objective`, `constraints`, `limits`, `types`, `violations`, `solve_time`, `gh_errors`, `evaluation` |
| `set` | Same as `evaluate` | Same as `evaluate`, but the call does not add to the evaluation counter (used to restore the sliders) |
| `close` | – | Closes the connection |

Non-finite numbers are sent as `null`.
