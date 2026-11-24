# BeamShapeExplorer
[BSE_Draft README.pdf](https://github.com/user-attachments/files/19553383/BSE_Draft.README.pdf)


## Optimization Sampler (batch constrained optimization)

The **Sampling Plan** and **Optimization Sampler** components (*Beam Shape Explorer → Optimization*) and the Python driver in [`BSE-Opt/`](BSE-Opt/README.md) run many constrained optimizations of any Grasshopper definition. The workflow has two separate steps. First, the Sampling Plan component creates a sampling plan of the design inputs (Latin Hypercube by default; Sobol, Halton, optimized/centered LHS, random and full-factorial grid are also available) that you can preview on the canvas, save, edit and reload as CSV (`BSE-Opt/sampling_plan.py` does the same from the command line). Then the Optimization Sampler and `BSE-Opt/bse_optimization_sampler.py` run one optimization per plan row, optimizing the variables, which are Number Sliders whose Min/Max set the bounds, against any number of objectives and constraints. The driver saves the variables, objective, constraint values, success code and time of every run to a CSV. See [`BSE-Opt/README.md`](BSE-Opt/README.md) for usage. Start with its [Download and build BSE](BSE-Opt/README.md#1-download-and-build-bse) section, which covers downloading this repository, building the plugin in Visual Studio and installing it in Grasshopper, and its [Python environment setup](BSE-Opt/README.md#3-python-environment-setup) section for installing Python and the required packages.


## Shear and moment diagrams (Beam Loads, Beam Supports, Shear and Moment Diagram)

Three components on the *Beam Shape Explorer → Loads* tab calculate and plot the shear and bending moment along a beam with any number of supports and loads. The existing **Load Envelope from Selfweight** component adds the beam's own weight:

```
 Beam Loads ─────────────────────Loads──┐
 Load Envelope from Selfweight ──Loads──┤   (both into the same Loads input)
 Beam Supports ──────────────────Sup────┼──► Shear and Moment Diagram ──Vu, Mu──► Flexural Analysis / Shear Analysis
 Beam Builder ──SpCrv───────────────────┘
     └──BBrep──► Load Envelope from Selfweight
```

**Beam Loads** defines the loads. Each list item is one load, and shorter lists repeat their last item:

| Input | Meaning |
|---|---|
| **Load Type** | `point` or `distributed` |
| **Magnitude** | Point load (kN), or distributed intensity at its start (kN/m). Positive acts downward; negative acts upward. |
| **Location** | Point-load position, or start of a distributed load, measured along the span curve from its start (m) |
| **End Location** *(optional)* | End of a distributed load. Default: the end of the span. |
| **End Magnitude** *(optional)* | Intensity at the end of a distributed load (kN/m), for triangular or trapezoidal loads. Default: uniform. |
| **Relative** | If true, locations are fractions of the span (0 = start, 1 = end) |

For example, `Load Type = point, distributed`, `Magnitude = 20, 10`, `Location = 2.5, 0` gives a 20 kN point load at 2.5 m and a uniform 10 kN/m load over the whole span. Several Beam Loads components can be connected to the same Loads input.

**Load Envelope from Selfweight** adds the beam's own weight. Connect **Material Properties**, the Beam Builder's **BBrep** and **SpCrv**, then connect its **Loads** output to the same Loads input as Beam Loads. It splits the beam into *n* equal segments (default 10) and weighs each one from its volume and the concrete density. Each segment becomes a uniform distributed load, so a shaped beam that is deeper at the supports gets a heavier load there. Its existing outputs (the `qw` load envelope, the split curves for Karamba, the load graphs and the segments) are unchanged. New inputs and outputs:

| Input / output | Meaning |
|---|---|
| **Load Factor** | Multiplies the selfweight, e.g. 1.2 or 1.4 for a factored dead load. Default 1.0. It applies to `qw` as well. |
| **Density (kg/m3)** *(optional)* | Overrides the concrete density from Material Properties, e.g. about 2400–2500 kg/m³ to include reinforcement. Only the brep's volume is weighed. |
| **Loads** | The selfweight as distributed loads (positive downward) for Shear and Moment Diagram |
| **Total Selfweight (kN)** | The total load |

The reactions from the selfweight are exact. The moments approach the exact values as *n* grows: for a beam with a parabolic depth, *n* = 10 gives moments within about 0.6%, and *n* = 20 within about 0.15%. In the diagram preview, the segments are drawn as one load with one label, for example "Self-weight: w = 4.2 to 6.5 kN/m". The segments are located along the span curve given to this component, so use the same span curve on Shear and Moment Diagram.

**Beam Supports** defines the supports from Grasshopper **points** and a **Support Type** per point: `fixed`, `pinned` or `roller` (`rolling` also works). The points are projected onto the span curve, so the Beam Builder's span end points, or points along it, work directly. Fixed supports restrain movement and rotation; pinned and roller supports restrain movement only. They behave the same for the vertical loads analyzed here.

**Shear and Moment Diagram** takes the **Span Curve** (e.g. the Beam Builder's *SpCrv* output), the loads and the supports, and:

* solves for the reactions, including statically indeterminate beams such as continuous, propped-cantilever and fixed-end beams. The results are exact for point loads and uniform or linearly varying distributed loads, however many there are. Indeterminate beams assume a uniform bending stiffness (EI) along the span; a beam that can't be held by its supports (for example a single pinned support) shows an error.
* outputs **Vu** and **Mu** at *n* evenly spaced analysis sections, like the other load envelopes, so they connect straight to **Flexural Analysis** and **Shear Analysis**. They use the same sign convention: `Vu` is the sum of the upward forces to the left of a section, and `Mu` is negative for sagging and positive for hogging. At a support or point load, `Vu` takes the side with the larger magnitude.
* outputs the **Reactions** (kN, positive upward) and **Reaction Moments** (kN-m, fixed supports only), the section locations and points, the diagram outlines and fills, and a text **Summary** with the maximum shear and sagging/hogging moments and where they occur.
* draws a preview in the Rhino viewport: the filled shear diagram above the beam and the moment diagram below it, with vertical jumps at point loads and supports, load arrows, support symbols, reactions, and labels at the extreme values. The diagrams are scaled automatically. **Shear Scale**, **Moment Scale** and **Diagram Offset** override that, and **Up Direction** sets the vertical direction (default Z).

The loads and supports can come from sliders, so they also work as **Sample Parameters** in the Sampling Plan of the [Optimization Sampler](#optimization-sampler-batch-constrained-optimization) workflow.
