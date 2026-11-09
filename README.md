# BeamShapeExplorer
[BSE_Draft README.pdf](https://github.com/user-attachments/files/19553383/BSE_Draft.README.pdf)


## Optimization Sampler (batch constrained optimization)

The **Optimization Sampler** component (*Beam Shape Explorer → Optimization*) and the Python driver in [`python/`](python/README.md) run many constrained optimizations of any Grasshopper definition. Each run samples the design inputs with Latin Hypercube Sampling, then optimizes the variables, which are Number Sliders whose Min/Max set the bounds, against any number of objectives and constraints. The driver saves the variables, objective, constraint values, success code and time of every run to a CSV. See [`python/README.md`](python/README.md) for setup and usage.
