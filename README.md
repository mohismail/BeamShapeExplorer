# BeamShapeExplorer
[BSE_Draft README.pdf](https://github.com/user-attachments/files/19553383/BSE_Draft.README.pdf)


## Optimization Sampler (batch constrained optimization)

The **Sampling Plan** and **Optimization Sampler** components (*Beam Shape Explorer → Optimization*) and the Python driver in [`python/`](python/README.md) run many constrained optimizations of any Grasshopper definition. The workflow has two separate steps. First, the Sampling Plan component creates a sampling plan of the design inputs (Latin Hypercube by default; Sobol, Halton, optimized/centered LHS, random and full-factorial grid are also available) that you can preview on the canvas, save, edit and reload as CSV (`python/sampling_plan.py` does the same from the command line). Then the Optimization Sampler and `python/bse_optimization_sampler.py` run one optimization per plan row, optimizing the variables, which are Number Sliders whose Min/Max set the bounds, against any number of objectives and constraints. The driver saves the variables, objective, constraint values, success code and time of every run to a CSV. See [`python/README.md`](python/README.md) for usage, and its [Python environment setup](python/README.md#2-python-environment-setup) section for installing Python and the required packages.
