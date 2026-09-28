# Export onboarding verification (2026-09-28)

The `main` checkout has unrelated local changes. All export and Godot runtime checks below ran in `../GodotALS-clone-validation-20260928`, a local clone of earlier `main` commit `991038d` with the already committed importer fix and this exporter-script fix copied in. The source UE project was `../AdvancedLocomotionSystemV` on UE 5.9; no third-party assets were added to Git.

## Finding and fix

The documented P2A command initially failed during `RunUAT BuildPlugin`: its temporary HostProject could not find the project-local `ALS` plugin required by `AlsGodotExporter.uplugin`. UE 5.9 `BuildPlugin` supports `-Dependencies=<plugin descriptor>` and copies that plugin into the HostProject. `build-als-exporter.ps1` now checks for `Plugins/ALS/ALS.uplugin` before packaging and supplies it to UAT. The README lists this prerequisite. P2A now names each long-running phase before starting it.

## Results

- The source project's complete Editor target build and four-plugin audit passed before export. With the dependency supplied, `RunUAT BuildPlugin` and exporter ReadyCheck passed.
- P2A DryRun reported 267 planned assets and 141 exportable files. The formal export reported 141 files and no warnings. The second export matched byte-for-byte (`P2A_DETERMINISM_OK files=146`); manifest audit and joint publication passed.
- The newly exported manifest SHA-256 was `5d942e8566c9c8de0fbbf015e02c6235ac2deef51c8c54446a56302a1d35bfd0`, identical to the committed asset lock. The clone's lock file had no Git diff.
- P2B with `-CleanImport` built the solution with zero warnings and errors. Godot imported all 141 files, passed representative asset checks, and passed single/parallel real-rig checks for one and ten characters (`P2B_VERIFICATION_OK`). The ordinary `als_demo.tscn` then ran headlessly for 180 frames with exit code 0.
- Both modified PowerShell scripts parsed without errors. The focused exporter readiness Pester suite passed 8/8.
- After the packaged exporter was deployed, the complete UE Editor target was rebuilt and audited again. ALS, AlsGodotExporter, AutoTestTools and BlueprintLisp all passed the common BuildId/receipt audit.

This verifies the local UE source and Windows toolchain through a fresh export and import. It does not establish that every independently licensed ALS source revision will produce the locked hash, or that the Demo has passed visual review.
