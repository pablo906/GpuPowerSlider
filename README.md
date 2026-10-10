# GPU Power Slider

[![Build, test and release](https://github.com/pablo906/GpuPowerSlider/actions/workflows/build-release.yml/badge.svg)](https://github.com/pablo906/GpuPowerSlider/actions/workflows/build-release.yml)

A small Windows app for setting a **runtime total GPU power target** on the verified
ASUS Zephyrus G14 GA403WW / RTX 5080 Laptop. Choose a target with a slider or wattage
box, apply it, and see the enforced limit separately from actual power draw.

**Requires the separately installed NvpwrControl 2.4.6 backend.** This repository
contains the GUI and adapter code; it does not redistribute NvpwrControl, its
license, driver resolver caches, or any NVIDIA/ASUS firmware.

## Download and requirements

Download the Windows x64 ZIP from [Releases](https://github.com/pablo906/GpuPowerSlider/releases/latest),
extract it into a writable folder, and run `GpuPowerSlider.exe`. Accept the Windows
administrator prompt. Opening the app reads state; it does not apply a power target.

- Windows x64 with .NET Framework 4.8; locally exercised on Windows 11.
- ASUS GA403WW with the verified NVIDIA PCI identity `DEV_2C59&SUBSYS_39C81043`.
- NVIDIA driver with `nvidia-smi.exe` available in Windows System32.
- Installed NvpwrControl **2.4.6**, with an already validated driver and existing
  resolver caches. Expected executable path:
  `%LOCALAPPDATA%\Programs\NvpwrControl 2.4.6\NvpwrControl.exe`.
- Exact reviewed backend SHA-256:
  `B12885BD6A2BB9B193758E67AF574ABC6566404E8786206D5E5B749CE950648A`.

A different backend executable is rejected. Driver updates may require validation
in NvpwrControl before writes are available again. This adapter does not perform
that validation, dump ROMs, or remove trust checks automatically.

## Use

1. Move the slider or type an integer wattage.
2. Click **Apply limit**. The range is bounded by the validated backend and live
   maximum, currently 120–175 W on the verified machine.
3. Pause GPU workloads if utilization is above 30%. The request waits until usage
   drops to 30% or below; **Cancel pending** cancels it.
4. **Restore 25 W boost** restores that ASUS Dynamic Boost request instead of the
   manually requested total limit.

Apply sends ASUS C0=0 to disable Dynamic Boost, then runs the backend's guarded
CURRENT transaction. It checks the NVIDIA enforced limit and unchanged maximum
three times. The target is a total limit, not a base value plus an extra boost.
Disabling Dynamic Boost alone does not set a higher manual limit.

The enforced limit, available maximum, actual draw, usage and temperature refresh
about every two seconds. The window follows Windows light/dark preferences,
including changes while open, and sizes its text for display scaling.

G-Helper mode or boost changes and reboots can replace the runtime target. The app
reports external policy changes and does not repeatedly overwrite them. Closing
it cancels a pending request and leaves an already applied limit active. It does
not edit G-Helper profiles, CPU settings, firmware, registry maximums, boot settings
or startup tasks.

If a request fails, the controller attempts to restore the prior CURRENT. When
that value is outside the manual backend range, it requests 25 W boost instead.
The displayed result distinguishes confirmed restoration from a failed rollback.
Local actions are logged in `actions.log`; resolver caches are copied into `state/`.
Neither belongs in a public source checkout or release.

## Build and test

No SDK download or NuGet restore is required. On Windows with the .NET Framework
4.x C# compiler:

```powershell
.\build.ps1 -Test
```

Build output goes to `artifacts/bin/` so an existing installed executable is not
replaced. The default build also compiles `SmokeTests.exe`; `-Test` runs it without
administrator rights. The fake device never accesses the installed backend or
writes GPU/ACPI settings.

Twenty checks cover busy-GPU and driver/range guards, success readback, rollback,
false-success detection, slider/text synchronization, pending cancellation,
queued application, no startup writes, no automatic write loop, readable light
and dark themes, and text fitting at normal and 150% layout scaling. Test logs and
rendered form previews are created under `artifacts/bin/`.

To package a release:

```powershell
.\scripts\package.ps1 -Version 1.0.0
```

The ZIP contains only `GpuPowerSlider.exe`, `README.md`, and `LICENSE`.
`SHA256SUMS.txt` covers the executable and ZIP. The executable is unsigned.

GitHub Actions builds and tests pushes and pull requests, and uploads build/test
artifacts. Pushing a `vMAJOR.MINOR.PATCH` tag builds, tests and publishes a release
with its ZIP and checksums. Actions are pinned to commit SHAs, and only the
release job receives repository contents write permission.

## Hardware verification

Earlier local tests applied and verified 140 W and 150 W runtime limits with the
installed backend. The GUI action log also recorded successful 150 W and 175 W
requests, with a later NVIDIA read reporting 175 W enforced and maximum. These
are observations from one GA403WW, not a claim of universal compatibility or
sustained 175 W workload stability. The automated tests use simulated hardware.

For an explicit elevated hardware check, close any other instance first:

```powershell
.\artifacts\bin\GpuPowerSlider.exe --verify-live 150
```

This command **writes hardware settings**: it disables Dynamic Boost and applies
150 W. Without an explicit number, it uses the currently enforced supported
integer value, or declines a value below the backend's supported manual range.
Normal startup never invokes this mode.

## Source and related work

- `MainForm.cs`: Windows Forms controls, theme, layout, queue and readback.
- `Backend.cs`: installed-backend adapter, ASUS C0 request, transaction controller.
- `Program.cs`: administrator/single-instance entry point and explicit live check.
- `Tests.cs`: fake device and controller/UI checks.
- `build.ps1`, `scripts/package.ps1`: local build, tests and release packaging.

The ASUS request uses the existing ATKACPI DEVS endpoint `0x001200C0`.
[G-Helper's power code](https://github.com/seerge/g-helper/blob/main/app/Mode/ModeControl.cs)
and [ASUS constants](https://github.com/seerge/g-helper/blob/main/app/AsusACPI.cs)
provide the existing power-control context. [G-Helper PR #6150](https://github.com/seerge/g-helper/pull/6150)
adds a model-scoped Dynamic Boost Off setting; that setting is separate from this
adapter's elevated CURRENT request.

## License

MIT for this repository's code. The separately installed NvpwrControl backend
retains its own terms; it is neither included nor relicensed here.
