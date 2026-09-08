# Kubernetes

Deliberately empty for now.

Plan section 20: Docker Compose is the first complete environment. Kubernetes manifests
stay ready for later scale but are kept out of the initial development loop, because
forcing k8s into the inner loop slows every change without buying anything yet.

Add manifests here when one of these becomes true:

- The worker needs independent horizontal scaling from the API.
- GPU scheduling across a fleet is required for vLLM.
- More than one environment needs the same deployment topology.
