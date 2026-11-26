# VIctory-Navi10 v0.1.0-alpha

The first experimental release provides a self-contained Windows Direct3D 12 lab for Navi 10. It compares direct instancing, compute culling with indirect drawing, and compute-generated indexed geometry. The source builds with the x64 .NET Framework compiler supplied by Windows.

- The compact geometry layout reduced peak process-local video memory from 95.21 MiB to 58.71 MiB at 100,000 objects in a controlled A/B comparison.
- On the RX 5700 XT, 24 image comparisons matched exactly. A separate run completed 6,000 frames at 100,000 objects across the three paths.
- The repository includes the raw JSON and CSV results, a verification script, and captures from the hardware runs.

This independent project is releasing source only. The release source compiled on the reference PC. Windows Application Control blocked the newly built unsigned executable while Smart App Control was enabled. An earlier unsigned build passed the 10,000-object image test after extraction. Access to the Code Integrity event log was denied, so the specific policy rule remains unconfirmed. See the build and test commands in the README. Full Xbox system emulation is outside the release scope. GTA VI has not been tested.

Contributions of driver and hardware verification, small synthetic shader fixtures, and reproducible performance comparisons are welcome.
