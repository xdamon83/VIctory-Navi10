# Working in this repository

- Keep the lab self-contained and runnable without game files or runtime downloads.
- Preserve the direct reference path when changing culling, geometry emission, or draw submission.
- Build from the root with `build.ps1` and run `tests/verify.ps1` for graphics changes. Use its WARP option where hardware execution is unavailable, and record the adapter in any reported result.
- Store test runs in `runs/`. Keep the recorded alpha data in `benchmarks/raw/` intact.
- Update the benchmark protocol when a change affects what a timing, count, or memory metric represents.
- Use generated or redistributable fixtures. Keep external source and contributions under their applicable licenses and document provenance.
