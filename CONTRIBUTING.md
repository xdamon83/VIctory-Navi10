# Contributing

Developers working on Direct3D 12, shader translation, or RDNA 1 are welcome to contribute. Hardware validation from RX 5500, 5600, and 5700 series owners is also useful.

Run `tests/verify.ps1` before submitting a rendering change. Keep each change small enough to compare with the existing direct path. For a performance contribution, state the adapter, driver, object count, resolution, warmup, and result files. Use the same conditions for before and after captures.

For an issue, include the exact command, adapter and driver, Windows version, and the first relevant error. Review any attached logs for personal data. Use synthetic fixtures or resources that can be redistributed. Describe the origin and license of any third-party contribution.
