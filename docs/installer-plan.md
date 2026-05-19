# Installer Plan
# GreenSwamp Alpaca Server — Windows MSI / Linux DEB / Raspberry Pi DEB

**Last updated: 2026-05-19 09:29**

---

## 1. Goals

Produce signed, versioned installers from the five self-contained publish artefacts created by
`.github/workflows/publish.yml`.  The installer targets are:

| Platform | Format | Install path | Notes |
|----------|--------|--------------|-------|
| Windows x64 | `.msi` | `%ProgramFiles%\GreenSwamp\Alpaca Server\` | Signed with code-signing cert |
| Windows x86 | `.msi` | `%ProgramFiles(x86)%\GreenSwamp\Alpaca Server\` | Separate MSI; same WiX source |
| Linux x64 (Ubuntu/Debian) | `.deb` | `/opt/greenswamp/alpaca-server/` | Also usable on Raspberry Pi OS 64-bit |
| Raspberry Pi arm64 | `.deb` | `/opt/greenswamp/alpaca-server/` | `linux-arm64` build |
| Raspberry Pi arm (32-bit OS) | `.deb` | `/opt/greenswamp/alpaca-server/` | `linux-arm` build |

> **APT repository note:** A raw `.deb` file is the deliverable. Publishing to a hosted APT repo
> (e.g. GitHub Releases + `apt-ftparchive`, or Cloudsmith) is a follow-on task and is flagged in
> § 7 but not planned here.

---

## 2. Versioning Strategy

### Problem
No `<Version>` property exists in any `.csproj` today. The app reads `AssemblyInformationalVersion`
at runtime, which defaults to `1.0.0.0`.

### Required change (prerequisite — must be done before any installer build)
Add a single version source in `Directory.Build.props` (create it at the solution root if absent):

```xml
<!-- Directory.Build.props  — solution root -->
<Project>
  <PropertyGroup>
	<!-- Bump this for every release. Installer tooling reads $(Version) from here. -->
	<Version>1.0.0</Version>
	<AssemblyVersion>$(Version).0</AssemblyVersion>
	<FileVersion>$(Version).0</FileVersion>
	<InformationalVersion>$(Version)</InformationalVersion>
  </PropertyGroup>
</Project>
```

The CI workflow injects the Git tag as the version at publish time:
```yaml
-p:Version=${{ github.ref_name }}     # e.g. v1.2.3  → strip leading 'v' in the workflow step
```

A helper step in the workflow strips the `v` prefix and writes it to `$GITHUB_ENV` so all
subsequent steps see `APP_VERSION=1.2.3`.

---

## 3. Windows MSI — WiX Toolset v4

### Toolset choice
[WiX Toolset v4](https://wixtoolset.org/docs/intro/) is the modern, actively maintained MSI
authoring tool for .NET. It integrates with `dotnet build` via the `WixToolset.Sdk` package and
runs cross-platform (the MSI is built on the Linux CI runner with WiX's cross-compile support, or
on `windows-latest`).

### Recommended approach: `wix` global tool + HeatWave harvesting

```
dotnet tool install --global wix
```

The MSI project is a separate `.wixproj` (WiX v4 style) that sits in a new
`Installer\Windows\` folder and references the publish output of `win-x64` (or `win-x86`).

### New files to create

```
Installer\
  Windows\
	GreenSwamp.Alpaca.Installer.wixproj   ← WiX v4 project
	Product.wxs                           ← Component authoring, shortcuts, service
	Directories.wxs                       ← Install-path definitions
	WixUI_Custom.wxs                      ← Optional: custom UI (logo, EULA)
	Resources\
	  banner.bmp                          ← 493×58 px WiX dialog banner
	  dialog.bmp                          ← 493×312 px WiX background
	  License.rtf                         ← EULA in RTF format
```

### Key WiX elements to author

| Element | Notes |
|---------|-------|
| `<Package>` | `UpgradeCode` must be a fixed GUID (generate once, commit forever) |
| `<MajorUpgrade>` | `DowngradeErrorMessage` ensures clean upgrades |
| `<Component>` per harvested file | WiX HeatWave auto-harvests the publish folder |
| `<ServiceInstall>` | Installs `GreenSwamp.Alpaca.Server.exe` as a Windows Service (`DisplayName`, `Start="auto"`) |
| `<ServiceControl>` | Starts/stops the service on install/uninstall |
| `<Shortcut>` | Start-menu shortcut to the management URL (`http://localhost:31426`) |
| `<RegistryValue>` | Write install path to `HKLM\SOFTWARE\GreenSwamp\AlpacaServer` |
| `<FirewallException>` (WixFirewall ext) | Open inbound TCP port `31426` |

### Upgrade GUID
Generate once only:
```powershell
[System.Guid]::NewGuid().ToString("D").ToUpper()
```
Commit this GUID permanently into `Product.wxs`. **Never change it.** Changing it breaks
`MajorUpgrade` detection.

### Service vs. tray-app install
The MSI should support two install modes via a WiX `Feature` structure:
- **Server Feature** (required): installs the binary + service.
- **Desktop Feature** (optional, default on): creates Start-menu shortcuts.

### Code signing
The MSI (and ideally the `.exe` inside it) must be Authenticode-signed before distribution.
The CI workflow should call `signtool.exe` (available on `windows-latest` runners) after the
WiX build:
```yaml
- name: Sign MSI
  run: |
	signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 \
	  /f ${{ secrets.CODESIGN_PFX_PATH }} /p ${{ secrets.CODESIGN_PFX_PASSWORD }} \
	  Installer/Windows/bin/Release/GreenSwamp.Alpaca.Installer.msi
```
Store the PFX as a GitHub secret (`CODESIGN_PFX_B64`) if using a file-based cert.

### CI integration (addition to `publish.yml`)

```yaml
  build-msi:
	name: Build MSI (${{ matrix.rid }})
	needs: publish
	runs-on: windows-latest
	strategy:
	  matrix:
		rid: [ win-x64, win-x86 ]
	steps:
	  - uses: actions/checkout@v4
	  - uses: actions/download-artifact@v4
		with:
		  name: greenswamp-alpaca-${{ matrix.rid }}
		  path: publish/${{ matrix.rid }}
	  - name: Install WiX
		run: dotnet tool install --global wix
	  - name: Add WiX extensions
		run: |
		  wix extension add WixToolset.UI.wixext
		  wix extension add WixToolset.Firewall.wixext
		  wix extension add WixToolset.Util.wixext
	  - name: Build MSI
		run: dotnet build Installer/Windows/GreenSwamp.Alpaca.Installer.wixproj
		  -c Release -p:Platform=${{ matrix.rid }} -p:Version=${{ env.APP_VERSION }}
	  - uses: actions/upload-artifact@v4
		with:
		  name: msi-${{ matrix.rid }}
		  path: Installer/Windows/bin/Release/*.msi
```

---

## 4. Linux / Raspberry Pi DEB Packages

### Toolset choice
[`dotnet-deb`](https://github.com/quamotion/dotnet-deb) (`Quamotion.DotNetDeb`) is a NuGet-based
tool that calls the native `dpkg-deb` / `fakeroot` pipeline from within `dotnet publish`. It is
the lowest-friction approach for a .NET-native team.

Alternative: hand-author the `DEBIAN/control` + `DEBIAN/postinst` and call `dpkg-deb --build`
directly in CI — more control, no extra package, works on any Debian-capable runner.

**Recommendation: use the hand-authored approach** (no extra SDK dependency; easier to audit;
works identically for all three Linux RIDs).

### Package metadata (one set, three arch values)

| Field | Value |
|-------|-------|
| `Package` | `greenswamp-alpaca-server` |
| `Version` | `$(APP_VERSION)` from CI |
| `Architecture` | `amd64` / `arm64` / `armhf` |
| `Maintainer` | `Principia4834 <user@example.com>` |
| `Description` | `GreenSwamp Alpaca Server — ASCOM Alpaca telescope mount control` |
| `Depends` | *(none — self-contained binary)* |
| `Section` | `science` |
| `Priority` | `optional` |

### New files to create

```
Installer\
  Linux\
	build-deb.sh                 ← shell script; called from CI for each RID
	debian\
	  control.template           ← Architecture token replaced per build
	  postinst                   ← chmod +x, systemd enable + start
	  prerm                      ← systemd stop + disable
	  postrm                     ← purge config / log dirs on --purge
	systemd\
	  greenswamp-alpaca.service  ← unit file bundled into /lib/systemd/system/
```

### `debian/control.template`

```
Package: greenswamp-alpaca-server
Version: @@VERSION@@
Architecture: @@ARCH@@
Maintainer: Principia4834 <maintainer@example.com>
Installed-Size: @@INSTALLED_SIZE@@
Depends:
Recommends: ufw
Section: science
Priority: optional
Homepage: https://github.com/Principia4834/GreenSwampAlpaca
Description: GreenSwamp Alpaca Server
 Self-contained ASCOM Alpaca server for SkyWatcher mount control.
 Provides a Blazor-based web UI accessible on port 31426.
```

### `debian/postinst`

```bash
#!/bin/bash
set -e
chmod +x /opt/greenswamp/alpaca-server/GreenSwamp.Alpaca.Server
adduser --system --no-create-home --group greenswamp 2>/dev/null || true
usermod -aG dialout greenswamp || true
cp /opt/greenswamp/alpaca-server/greenswamp-alpaca.service /lib/systemd/system/
systemctl daemon-reload
systemctl enable greenswamp-alpaca.service
systemctl start  greenswamp-alpaca.service
```

### `debian/prerm`

```bash
#!/bin/bash
set -e
systemctl stop    greenswamp-alpaca.service || true
systemctl disable greenswamp-alpaca.service || true
```

### `debian/postrm`

```bash
#!/bin/bash
set -e
if [ "$1" = "purge" ]; then
	rm -rf /opt/greenswamp /var/log/greenswamp
	systemctl daemon-reload
fi
```

### systemd unit file (`greenswamp-alpaca.service`)

```ini
[Unit]
Description=GreenSwamp Alpaca Server
After=network.target

[Service]
Type=simple
User=greenswamp
Group=greenswamp
ExecStart=/opt/greenswamp/alpaca-server/GreenSwamp.Alpaca.Server
WorkingDirectory=/opt/greenswamp/alpaca-server
Restart=on-failure
RestartSec=5
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
```

### `build-deb.sh` (core logic)

```bash
#!/usr/bin/env bash
# Usage: ./build-deb.sh <RID> <VERSION>
#   e.g. ./build-deb.sh linux-arm64 1.2.3
set -euo pipefail

RID="$1"
VERSION="$2"

case "$RID" in
  linux-x64)   DEB_ARCH="amd64"  ;;
  linux-arm64) DEB_ARCH="arm64"  ;;
  linux-arm)   DEB_ARCH="armhf"  ;;
  *) echo "Unknown RID: $RID"; exit 1 ;;
esac

PKG_ROOT="pkg-${RID}"
INSTALL_DIR="${PKG_ROOT}/opt/greenswamp/alpaca-server"
DEBIAN_DIR="${PKG_ROOT}/DEBIAN"
SYSTEMD_DIR="${PKG_ROOT}/lib/systemd/system"

# Stage publish output
mkdir -p "${INSTALL_DIR}" "${DEBIAN_DIR}" "${SYSTEMD_DIR}"
cp -r "publish/${RID}/." "${INSTALL_DIR}/"
cp "Installer/Linux/systemd/greenswamp-alpaca.service" "${SYSTEMD_DIR}/"
cp "Installer/Linux/debian/postinst" "${DEBIAN_DIR}/"
cp "Installer/Linux/debian/prerm"    "${DEBIAN_DIR}/"
cp "Installer/Linux/debian/postrm"   "${DEBIAN_DIR}/"
chmod 0755 "${DEBIAN_DIR}/postinst" "${DEBIAN_DIR}/prerm" "${DEBIAN_DIR}/postrm"

INSTALLED_SIZE=$(du -sk "${INSTALL_DIR}" | cut -f1)

sed -e "s/@@VERSION@@/${VERSION}/" \
	-e "s/@@ARCH@@/${DEB_ARCH}/"   \
	-e "s/@@INSTALLED_SIZE@@/${INSTALLED_SIZE}/" \
	"Installer/Linux/debian/control.template" > "${DEBIAN_DIR}/control"

fakeroot dpkg-deb --build "${PKG_ROOT}" \
  "greenswamp-alpaca-server_${VERSION}_${DEB_ARCH}.deb"

echo "Built: greenswamp-alpaca-server_${VERSION}_${DEB_ARCH}.deb"
```

### CI integration (addition to `publish.yml`)

```yaml
  build-deb:
	name: Build DEB (${{ matrix.rid }})
	needs: publish
	runs-on: ubuntu-latest
	strategy:
	  matrix:
		rid: [ linux-x64, linux-arm64, linux-arm ]
	steps:
	  - uses: actions/checkout@v4
	  - uses: actions/download-artifact@v4
		with:
		  name: greenswamp-alpaca-${{ matrix.rid }}
		  path: publish/${{ matrix.rid }}
	  - name: Install packaging tools
		run: sudo apt-get install -y fakeroot dpkg-dev
	  - name: Build DEB
		run: |
		  chmod +x Installer/Linux/build-deb.sh
		  ./Installer/Linux/build-deb.sh ${{ matrix.rid }} ${{ env.APP_VERSION }}
	  - uses: actions/upload-artifact@v4
		with:
		  name: deb-${{ matrix.rid }}
		  path: "*.deb"
```

---

## 5. Release Workflow — Stitching It Together

The full CI flow for a tagged release becomes:

```
git tag v1.2.3
git push origin v1.2.3
```

```
publish.yml triggers on tag push
│
├─ publish (matrix: 5 RIDs)  ← existing job
│    └─ uploads 5 artefacts
│
├─ build-msi (matrix: win-x64, win-x86)
│    ├─ downloads publish artefacts
│    ├─ builds & signs 2 × .msi
│    └─ uploads msi-win-x64, msi-win-x86
│
├─ build-deb (matrix: linux-x64, linux-arm64, linux-arm)
│    ├─ downloads publish artefacts
│    ├─ builds 3 × .deb
│    └─ uploads deb-linux-*
│
└─ release (needs: build-msi, build-deb)
	 ├─ creates GitHub Release v1.2.3
	 └─ attaches all 5 installer artefacts
```

### Release job skeleton

```yaml
  release:
	name: Create GitHub Release
	needs: [ build-msi, build-deb ]
	runs-on: ubuntu-latest
	if: startsWith(github.ref, 'refs/tags/v')
	steps:
	  - uses: actions/download-artifact@v4
		with:
		  path: release-assets
		  merge-multiple: true
	  - name: Create Release
		uses: softprops/action-gh-release@v2
		with:
		  files: release-assets/**
		  generate_release_notes: true
```

---

## 6. Pre-requisites Checklist

Before any installer work starts, the following must be in place:

| # | Prerequisite | Owner | Status |
|---|-------------|-------|--------|
| P-1 | `Directory.Build.props` with `<Version>` added to solution root | Dev | ⬜ |
| P-2 | CI workflow strips `v` prefix from Git tag to `APP_VERSION` env var | Dev | ⬜ |
| P-3 | WiX Toolset v4 installed on CI (`windows-latest` runner) | CI | ⬜ |
| P-4 | `fakeroot` + `dpkg-dev` available on `ubuntu-latest` runner (they are by default) | CI | ✅ |
| P-5 | Code-signing certificate (PFX) for Windows MSI | Dev/Business | ⬜ |
| P-6 | Upgrade GUID generated and committed to `Product.wxs` | Dev | ⬜ |
| P-7 | License text (`License.rtf`) authored for MSI UI | Dev | ⬜ |
| P-8 | Firewall port (`31426`) confirmed / made configurable in installer | Dev | ⬜ |
| P-9 | `greenswamp` system user creation tested on target distros | QA | ⬜ |
| P-10 | End-to-end install/uninstall/upgrade tested in a VM before release | QA | ⬜ |

---

## 7. Open Questions / Decisions Needed

| # | Question | Default assumption |
|---|----------|--------------------|
| OQ-1 | **Install as Windows Service vs. desktop tray app?** WiX `<ServiceInstall>` installs headlessly; tray requires a separate `notify-icon` process. | Plan for Service; tray-app is a separate future feature |
| OQ-2 | **Code-signing cert:** self-signed (SmartScreen warning) or commercial CA? | Commercial CA recommended for distribution |
| OQ-3 | **APT repository hosting?** GitHub Releases raw `.deb` download is sufficient for now; a full `apt` repo (with `Release`/`Packages` indices) allows `apt upgrade`. | GitHub Releases for v1; APT repo for future |
| OQ-4 | **Windows: should the installer open a browser to `http://localhost:31426` on first run?** | Recommend yes — `ExePackage` or `CustomAction` post-install |
| OQ-5 | **Upgrade behaviour:** keep existing `appsettings.user.json` on upgrade? | Yes — do not overwrite `%AppData%\GreenSwampAlpaca` on Windows or `~/.config/greenswamp` on Linux |
| OQ-6 | **Port configurable at install time?** WiX supports a text entry dialog; DEB `debconf` can do the same. | No for v1; hardcode `31426` |
| OQ-7 | **RPM support for Fedora/Red Hat?** | Out of scope for v1; add after DEB is validated |

---

## 8. Recommended Implementation Order

1. **P-1 & P-2** — Add `Directory.Build.props` version + CI tag-stripping step. Verify build still succeeds.
2. **MSI skeleton** — Create `Installer\Windows\` folder with `Product.wxs` stub and `GreenSwamp.Alpaca.Installer.wixproj`; get a bare-bones MSI building locally with `wix build`.
3. **MSI service + shortcuts** — Add `<ServiceInstall>`, `<Shortcut>`, and `<FirewallException>`; test install/uninstall on a clean Windows VM.
4. **MSI CI job** — Add `build-msi` job to `publish.yml`; get green CI for both `win-x64` and `win-x86`.
5. **DEB scaffold** — Create `Installer\Linux\` tree; test `build-deb.sh` locally against the `linux-x64` publish output on an Ubuntu host or WSL2.
6. **DEB arm builds** — Extend to `linux-arm64` and `linux-arm`; test on a Pi or QEMU arm64 container.
7. **DEB CI job** — Add `build-deb` job to `publish.yml`.
8. **Release job** — Add the `release` job; do a dry run with a pre-release tag (e.g. `v0.9.0-rc1`).
9. **Signing** — Add code-signing to the MSI CI step once a cert is available.
10. **End-to-end QA** — Install, verify mount connection, upgrade, uninstall on each target platform.

---

*End of installer plan — GreenSwamp Alpaca Server.*
