# UE 5.8 Version Gates

Later update: exporter readiness now uses a generic success marker; the build version is checked only by the supported-engine compatibility helper. Exporter/plugin version strings remain informational metadata and are not part of the asset-input gate. The validation counts below describe the earlier run, before this marker simplification.

2026-10-04: the Unreal build and golden-generation entrypoints now accept UE 5.8.x as well as UE 5.9.x. This changes runtime engine gates only; it does not re-identify the locked ALS reference source.

## Changes

- Added a shared PowerShell version check for UE 5.8.x and 5.9.x.
- P2A requires the generic exporter ready marker; supported-engine compatibility is checked separately from the marker.
- P3 accepts either supported version and requires the built Editor receipt to match that exact engine version.
- P5A accepts either supported version in the deployed Editor receipt and still requires a matching module `BuildId`.
- P4/P5A reference `targetEngine`, the patched ALS descriptor identity, and their schemas remain locked to 5.9.0 because they describe the approved ALS reference build.

## Validation

- PowerShell focused suites: 48 passed, 0 failed across engine-version, exporter-build, P3, and P4 tests.
- P2A verifier suite: 50 passed, 0 failed.
- C# source-contract test for the dynamic ready marker: 1 passed, 0 failed.
- The UE 5.8.1 reference-plugin Editor Development compile previously completed 70 actions. The subsequent full BuildPlugin attempt did not complete its Game Development profile because UBT reported `ConflictingInstance`; that profile is not claimed as verified by that attempt.

At the time of this validation, no UE 5.8 asset export had been published, so its export behavior remained unverified. The P2A flow now compares two exports from the current source project and checks each file against that run's manifest; it no longer requires a canonical manifest hash, fixed inventory, or exact exporter metadata version. The separate legacy GASP58 exporter remains UE 5.8-specific.
