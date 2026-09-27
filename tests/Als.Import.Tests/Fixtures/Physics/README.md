# Native sleep reference

`v4_physics_sleep_reference.json` is an offline Unreal Chaos trajectory fixture.
Export it with `PhysicsSleepOutput` pointing to this file. Keep its bytes intact;
the trajectory test compares the complete reference against the Core solver.

After a new export, run `pwsh -NoProfile -File tools/physics/Export-SleepRuntimeSettings.ps1`
from the repository root. This writes the compact runtime input at
`assets/config/v4_physics_sleep_settings.json` and records the fixture SHA256.
The runtime settings contain no trajectory `cases`.
