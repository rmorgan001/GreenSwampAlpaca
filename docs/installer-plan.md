# Installer Plan
# GreenSwamp Alpaca Server — Windows MSI / Linux DEB / Raspberry Pi DEB

**Last updated: 2026-05-19 14:01**

---

## 1. Goals

Produce versioned, installable packages from the five self-contained publish artefacts created by
`.github/workflows/publish.yml`, and attach them automatically to a GitHub Release with a single
`git tag` + `git push`.

| Platform | Format | Install path |
|----------|--------|--------------|
| Windows x64 | `.msi` (WiX v4) | `%ProgramFiles%\GreenSwamp\Alpaca Server\` |
| Windows x86 | `.msi` (WiX v4) | `%ProgramFiles(x86)%\GreenSwamp\Alpaca Server\` |
| Linux x64 (Ubuntu / Debian) | `.deb` | `/opt/greenswamp/alpaca-server/` |
| Raspberry Pi arm64 (64-bit OS) | `.deb` | `/opt/greenswamp/alpaca-server/` |
| Raspberry Pi arm (32-bit OS) | `.deb` | `/opt/greenswamp/alpaca-server/` |

**You do not need a Linux machine.** All five installers are built, packaged, and published
entirely by GitHub Actions on GitHub's own servers — see sections 6 and 7 for the full
explanation.

---

## 2. Code-Signing — Self-Signed Certificate

A commercial Authenticode certificate is not yet available, so a self-signed certificate is used
for now. This has one practical consequence on Windows: **Windows SmartScreen will show a warning
dialog** the first time an end-user runs the installer. The warning disappears once enough users
have installed it and Microsoft's reputation service trusts the binary. For a niche astronomy tool
distributed to known users this is fully acceptable.

### Creating the self-signed certificate (one-time, on your Windows machine)

```powershell
# Run in PowerShell on your local Windows machine — not in CI
$cert = New-SelfSignedCertificate `
	-Subject "CN=GreenSwamp Alpaca Server, O=Principia4834" `
	-Type CodeSigning `
	-CertStoreLocation Cert:\CurrentUser\My `
	-NotAfter (Get-Date).AddYears(3)

# Replace MySecurePassword with something strong and store it safely
$pwd = ConvertTo-SecureString -String "MySecurePassword" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath GreenSwamp-CodeSign.pfx -Password $pwd

# Base64-encode the PFX so it can be stored as a GitHub secret
[Convert]::ToBase64String([IO.File]::ReadAllBytes("GreenSwamp-CodeSign.pfx")) | Set-Clipboard
Write-Host "PFX base64 copied to clipboard — paste it as GitHub secret CODESIGN_PFX_B64"
```

### Adding the secrets to GitHub (one-time)

1. Go to `https://github.com/Principia4834/GreenSwampAlpaca/settings/secrets/actions`
2. Click **New repository secret**
3. Add `CODESIGN_PFX_B64` — paste the base64 string from the clipboard
4. Add `CODESIGN_PFX_PASSWORD` — the plain-text password used above

The CI signing step (inside the `build-msi` job) restores and uses them automatically — no
further changes needed.

> **Upgrade path to commercial cert:** When a commercial certificate is purchased (e.g. from
> DigiCert, Sectigo, or GlobalSign), simply replace the two GitHub secrets with a new PFX export.
> No changes to the workflow YAML or WiX source are required.

---

## 3. Versioning — SemVer Options

### Background
No `<Version>` exists in any `.csproj` today. The app reads `AssemblyInformationalVersion` at
runtime, which currently defaults to `1.0.0.0`. Both the MSI and DEB formats require a
well-formed version number before they can be built.

### SemVer options — choose one

| Option | How it works | Pros | Cons |
|--------|-------------|------|------|
| **A — Git tag drives everything (recommended)** | CI reads the pushed tag (e.g. `v1.2.3`), strips the `v`, and injects `-p:Version=1.2.3` into `dotnet publish`. `Directory.Build.props` holds a `<Version>0.0.0</Version>` fallback used only for local developer builds. | Single source of truth; no file to commit on every release; the tag IS the release | Requires discipline — always push a tag when you want a versioned installer |
| **B — `Directory.Build.props` is the source of truth** | Developer bumps `<Version>1.2.3</Version>`, commits, and CI uses whatever is in the file. | Simple; works without tags; version is visible in the repo at all times | Two-step process (bump file + commit + tag); easy to forget the file bump |
| **C — GitVersion / Nerdbank.GitVersioning** | A tool inspects git history, branches, and commit messages to compute a SemVer automatically. Supports pre-release suffixes from branch naming (e.g. `1.2.3-alpha.4`). | Fully automatic; zero manual steps | Extra tool dependency; learning curve; overkill for a small single-branch project |

**Recommendation: Option A.**

### Required one-time change (prerequisite before any installer build)

Create `Directory.Build.props` at the **solution root** (same folder as `GreenSwampAlpaca.sln`):

```xml
<!-- Directory.Build.props — solution root                                         -->
<!-- Used for LOCAL developer builds only. CI overrides this with -p:Version=x.y.z -->
<Project>
  <PropertyGroup>
	<Version>0.0.0</Version>
	<AssemblyVersion>$(Version).0</AssemblyVersion>
	<FileVersion>$(Version).0</FileVersion>
	<InformationalVersion>$(Version)</InformationalVersion>
  </PropertyGroup>
</Project>
```

### SemVer contract for this project

- Use `MAJOR.MINOR.PATCH` — e.g. `1.0.0`, `1.2.3`
- Pre-release tags: `v1.2.0-rc.1` — the workflow strips everything after the first three
  numeric parts for the DEB `Version` field (DEB does not support pre-release suffixes), but
  keeps the full tag name as the GitHub Release title
- The MSI `ProductCode` is auto-generated by WiX (`*`) — only `UpgradeCode` is a fixed GUID
  (see § 4)

---

## 4. Windows MSI — WiX Toolset v4 (Detail)

### Why WiX v4?
WiX v4 is the current release of the WiX Toolset, the de-facto standard for authoring Windows
Installer (`.msi`) packages in the .NET ecosystem. It ships as a `dotnet` global tool
(`dotnet tool install --global wix`), so no separate desktop installer is needed on CI. It runs
on both Windows and Linux runners. The older WiX v3 required a full Windows SDK and a separate
GUI installer — avoid it for new projects.

### Pre-generated GUIDs (permanent — never change these once committed)

| GUID | Purpose |
|------|---------|
| `0BFB5E9F-4475-4BFA-A8A5-1F26CD982B1C` | `UpgradeCode` — win-x64 MSI. Identifies the x64 product family across all version upgrades. |
| `D910192D-77C9-499E-80E9-10428B7FB80F` | `UpgradeCode` — win-x86 MSI. Identifies the x86 product family. |

> **Why two UpgradeCodes?** The x64 and x86 MSIs are separate product families. A user upgrading
> from x64 v1.0 to x64 v1.1 must be detected by MSI as the same product — that is what
> `UpgradeCode` does. If both RIDs shared one UpgradeCode, installing the x64 MSI would silently
> uninstall the x86 MSI (and vice versa), which is not what users expect.

> **Why not a fixed `ProductCode`?** MSI rules require a new `ProductCode` GUID every time the
> version number changes. Setting it to `*` in WiX tells the toolset to generate a new GUID
> automatically for every build — this is the correct approach.

### New files to create

```
Installer\
  Windows\
	GreenSwamp.Alpaca.Installer.wixproj    ← WiX v4 SDK-style project file
	Product.wxs                            ← Package definition, service, shortcuts, firewall
	Resources\
	  License.rtf                          ← EULA displayed in the installer dialog
	  banner.bmp                           ← 493x58 px   — dialog top banner
	  dialog.bmp                           ← 493x312 px  — welcome/finish background
```

### `GreenSwamp.Alpaca.Installer.wixproj` (full content to create)

```xml
<?xml version="1.0" encoding="utf-8"?>
<Project Sdk="WixToolset.Sdk/5.0.0">
  <PropertyGroup>
	<!-- Platform is injected by CI: win-x64 or win-x86 -->
	<Platform Condition="'$(Platform)'==''">win-x64</Platform>
	<OutputName>GreenSwamp.Alpaca.Server_$(Version)_$(Platform)</OutputName>
	<InstallerPlatform Condition="'$(Platform)'=='win-x64'">x64</InstallerPlatform>
	<InstallerPlatform Condition="'$(Platform)'=='win-x86'">x86</InstallerPlatform>
	<!-- PublishDir points at the dotnet publish output folder.
		 CI passes this via -p:PublishDir=...  Local default is relative to project. -->
	<PublishDir Condition="'$(PublishDir)'==''">..\..\publish\$(Platform)\</PublishDir>
  </PropertyGroup>
  <ItemGroup>
	<WixExtension Include="WixToolset.UI.wixext" />
	<WixExtension Include="WixToolset.Firewall.wixext" />
	<WixExtension Include="WixToolset.Util.wixext" />
  </ItemGroup>
</Project>
```

### `Product.wxs` (full content to create)

```xml
<?xml version="1.0" encoding="utf-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"
	 xmlns:fw="http://wixtoolset.org/schemas/v4/wxs/firewall"
	 xmlns:util="http://wixtoolset.org/schemas/v4/wxs/util">

  <!--
	UpgradeCode is injected at build time by CI via -p:UpgradeCode=...
	win-x64: 0BFB5E9F-4475-4BFA-A8A5-1F26CD982B1C
	win-x86: D910192D-77C9-499E-80E9-10428B7FB80F
	ProductCode is set to * so WiX auto-generates a new GUID for each version.
  -->
  <Package Name="GreenSwamp Alpaca Server"
		   Manufacturer="Principia4834"
		   Version="$(Version)"
		   UpgradeCode="$(UpgradeCode)"
		   Compressed="true">

	<MajorUpgrade DowngradeErrorMessage="A newer version of [ProductName] is already installed." />

	<MediaTemplate EmbedCab="true" />

	<!-- Installer UI -->
	<WixVariable Id="WixUILicenseRtf" Value="Resources\License.rtf" />
	<WixVariable Id="WixUIBannerBmp"  Value="Resources\banner.bmp" />
	<WixVariable Id="WixUIDialogBmp"  Value="Resources\dialog.bmp" />
	<UIRef Id="WixUI_Minimal" />

	<!-- Install directory structure -->
	<StandardDirectory Id="ProgramFiles6432Folder">
	  <Directory Id="MANUFACTURERDIR" Name="GreenSwamp">
		<Directory Id="INSTALLDIR" Name="Alpaca Server" />
	  </Directory>
	</StandardDirectory>

	<StandardDirectory Id="ProgramMenuFolder">
	  <Directory Id="STARTMENUFOLDER" Name="GreenSwamp Alpaca Server" />
	</StandardDirectory>

	<!--
	  HeatWave auto-harvesting: <Files Include="..." /> generates a <Component> and <File>
	  for every file under PublishDir at build time. No manual component authoring needed.
	-->
	<ComponentGroup Id="ProductComponents" Directory="INSTALLDIR">
	  <Files Include="$(PublishDir)**" />
	</ComponentGroup>

	<!-- Windows Service registration -->
	<Component Id="ServiceComponent" Directory="INSTALLDIR" Guid="*">
	  <util:ServiceConfig
		ServiceName="GreenSwampAlpacaServer"
		FirstFailureActionType="restart"
		SecondFailureActionType="restart"
		ThirdFailureActionType="none"
		ResetPeriodInDays="1"
		RestartServiceDelayInSeconds="5" />
	  <ServiceInstall
		Id="GreenSwampAlpacaService"
		Name="GreenSwampAlpacaServer"
		DisplayName="GreenSwamp Alpaca Server"
		Description="ASCOM Alpaca telescope mount control server"
		Type="ownProcess"
		Start="auto"
		ErrorControl="normal"
		Account="LocalSystem" />
	  <ServiceControl
		Id="StartGreenSwampAlpacaService"
		Name="GreenSwampAlpacaServer"
		Start="install"
		Stop="both"
		Remove="uninstall"
		Wait="yes" />
	</Component>

	<!-- Firewall exception for the Alpaca server port -->
	<Component Id="FirewallComponent" Directory="INSTALLDIR" Guid="*">
	  <fw:FirewallException
		Id="AlpacaServerFirewall"
		Name="GreenSwamp Alpaca Server"
		Port="31426"
		Protocol="tcp"
		Scope="any"
		IgnoreFailure="yes" />
	</Component>

	<!-- Start-menu shortcut that opens the management UI in the default browser -->
	<Component Id="ShortcutComponent" Directory="STARTMENUFOLDER" Guid="*">
	  <Shortcut Id="ManagementUIShortcut"
				Name="GreenSwamp Alpaca Server (Management UI)"
				Description="Open the Alpaca Server web management interface"
				Target="http://localhost:31426"
				WorkingDirectory="INSTALLDIR" />
	  <RemoveFolder Id="RemoveStartMenu" Directory="STARTMENUFOLDER" On="uninstall" />
	  <!-- KeyPath registry entry is required when a shortcut component has no file -->
	  <RegistryValue Root="HKCU"
					 Key="Software\GreenSwamp\AlpacaServer"
					 Name="installed"
					 Type="integer"
					 Value="1"
					 KeyPath="yes" />
	</Component>

	<!-- Root feature — everything is required -->
	<Feature Id="ProductFeature" Title="GreenSwamp Alpaca Server" Level="1">
	  <ComponentGroupRef Id="ProductComponents" />
	  <ComponentRef Id="ServiceComponent" />
	  <ComponentRef Id="FirewallComponent" />
	  <ComponentRef Id="ShortcutComponent" />
	</Feature>

  </Package>
</Wix>
```

---

## 5. Linux / Raspberry Pi DEB — dotnet-deb vs Hand-Authored: Full Trade-off Analysis

Both approaches produce an identical `.deb` file. The choice is about developer experience,
control, and long-term maintenance.

### Option A — `dotnet-deb` (Quamotion NuGet global tool)

`dotnet-deb` hooks into `dotnet publish` and wraps the `dpkg-deb`/`fakeroot` pipeline.

```bash
dotnet tool install --global dotnet-deb
dotnet deb -r linux-x64 -c Release -o packages/
```

| Pros | Cons |
|------|------|
| Single `dotnet`-style command — no shell scripting | **Sporadically maintained** — check GitHub activity; last release may be months old |
| Familiar workflow for .NET developers | Limited control over `postinst`/`prerm`/`postrm` maintainer scripts |
| Works on Windows (cross-compiles the DEB) | Does not bundle systemd unit files natively — requires post-processing |
| Fine for a simple binary drop | Cannot easily customise `Recommends`, `Section`, `Description` paragraphs |
| | Adds an external tool dependency that can break between .NET SDK versions |

**Verdict: Not suitable here.** This project needs systemd service installation, `dialout` group
membership, and purge cleanup — none of which `dotnet-deb` handles out of the box.

### Option B — Hand-authored `DEBIAN/` tree + `build-deb.sh` (chosen approach)

Manually author the five `DEBIAN/` control files and call `dpkg-deb --build` from a shell script.
This is the same approach used by the official Debian packaging team and by most Linux software
vendors.

| Pros | Cons |
|------|------|
| Full control over every installer behaviour | ~120 lines of shell script to write (once) |
| `postinst`/`prerm`/`postrm` handle service lifecycle precisely | Must be tested on a real Debian system, WSL2, or QEMU container |
| systemd unit bundled cleanly into `/lib/systemd/system/` | Developer needs to understand Debian packaging conventions |
| No external tool dependency beyond `fakeroot` + `dpkg-dev` (pre-installed on `ubuntu-latest`) | |
| Fully stable — `dpkg-deb` is part of Debian itself and will never break | |
| Identical process for all three Linux/Pi RIDs | |

**Verdict: Use Option B.** The ~120 lines of shell is a one-time cost. The correctness, stability,
and service-management capabilities are essential for this project.

---

## 6. GitHub Actions — What Runs Where (Full Explanation for First-Time Users)

### What is GitHub Actions?

GitHub Actions is a CI/CD (Continuous Integration / Continuous Deployment) platform built into
GitHub. When you push code or a tag, GitHub automatically provisions fresh virtual machines
(called **runners**) in Microsoft Azure, executes every step defined in your workflow YAML file,
and discards the machines when done. For **public repositories it is completely free** with no
minute limits.

### What runs on your local Windows machine?

Almost nothing beyond normal git operations:

| Your action | What it does |
|-------------|-------------|
| Author `.github/workflows/release.yml` | Done once, committed to git like any other file |
| `git tag v1.0.0` | Creates a local tag |
| `git push origin v1.0.0` | Pushes the tag to GitHub — **this is the single trigger** for the entire pipeline |
| Watch `https://github.com/Principia4834/GreenSwampAlpaca/actions` | Live log of every build step |
| Download finished installers from the GitHub Release page | The end result |

You do not run `dotnet publish`, `wix build`, or `dpkg-deb` locally unless you want to test
something. **Everything else happens on GitHub's servers.**

### What runs on GitHub's servers?

Two types of hosted runner are used:

| Runner | Used for | Why that runner? |
|--------|----------|-----------------|
| `ubuntu-latest` (Linux VM) | All five `dotnet publish` builds; all three DEB packaging jobs; the final GitHub Release creation | Fast, free, has `fakeroot` and `dpkg-dev` pre-installed. `dotnet publish -r linux-arm64` cross-compiles ARM binaries on x64 — no ARM hardware needed. |
| `windows-latest` (Windows Server VM) | Both MSI builds; code signing with `signtool.exe` | WiX requires Windows (`signtool.exe` is a Windows-only tool). The MSI itself is a Windows format. |

### Why you don't need a Linux machine

The `.deb` packages for `linux-arm64` (Pi 4/5, 64-bit OS) and `linux-arm` (Pi 3/4, 32-bit OS)
are **cross-compiled** by `dotnet publish -r linux-arm64` / `-r linux-arm` running on the Linux
x64 CI runner. The .NET SDK produces the ARM binary; then `dpkg-deb` on the same x64 machine
wraps it into a `.deb` — `dpkg-deb` is architecture-agnostic. No ARM hardware is involved at any
point in the build.

### Workflow trigger summary

| Git action | Jobs that run |
|------------|--------------|
| `git push` to `master` or a PR | `version` + `publish` only — validates the build compiles |
| `git push v*` tag | Full pipeline: version → publish → build-deb → build-msi → release |
| Manual trigger (GitHub Actions UI) | Full pipeline on demand |

### First-time setup checklist (one-time, on your machine)

1. Create the self-signed PFX (§ 2) on your local Windows machine
2. Add two GitHub repo secrets (§ 2) — takes 2 minutes in the browser
3. Commit the workflow YAML and installer source files (§ 9)
4. Push `git tag v0.9.0-rc1 && git push origin v0.9.0-rc1` for a dry run
5. Watch the Actions tab — all jobs should be green within ~10 minutes

---

## 7. One-Step "Create All Distros" — Complete `release.yml`

This single workflow file replaces `publish.yml`. Pushing a `v*` tag triggers the full
build → package → sign → publish pipeline. The GitHub Release is created automatically with
all five installer files attached.

### Pipeline architecture

```
git tag v1.2.3 && git push origin v1.2.3
│
└─ release.yml triggers
   │
   ├─ job: version         (ubuntu)
   │    strips 'v' prefix → app_version=1.2.3
   │
   ├─ job: publish         (ubuntu, matrix × 5 RIDs) — needs: version
   │    dotnet publish -r <RID> --self-contained -p:PublishSingleFile=true -p:Version=1.2.3
   │    uploads artefact: greenswamp-alpaca-<RID>
   │
   ├─ job: build-deb       (ubuntu, matrix × 3 Linux RIDs) — needs: version, publish
   │    downloads publish artefact
   │    runs Installer/Linux/build-deb.sh
   │    uploads artefact: deb-<RID>
   │
   ├─ job: build-msi       (windows-latest, matrix × 2 Windows RIDs) — needs: version, publish
   │    downloads publish artefact
   │    dotnet build GreenSwamp.Alpaca.Installer.wixproj
   │    signtool sign (self-signed PFX from GitHub secret)
   │    uploads artefact: msi-<RID>
   │
   └─ job: release         (ubuntu) — needs: build-deb, build-msi
		if: tag push only
		downloads all artefacts
		creates GitHub Release "GreenSwamp Alpaca Server 1.2.3"
		attaches all 5 installer files
		generates release notes from commit log
```

### `.github/workflows/release.yml` (complete file — replaces `publish.yml`)

```yaml
name: Release — Build, Package, Publish

on:
  push:
	branches: [ master ]
	tags:     [ 'v*' ]
  pull_request:
	branches: [ master ]
  workflow_dispatch:

jobs:
  # ─────────────────────────────────────────────────────────────────────
  # Derive a clean SemVer from the git tag (or use 0.0.0 for dev builds)
  # ─────────────────────────────────────────────────────────────────────
  version:
	name: Derive version
	runs-on: ubuntu-latest
	outputs:
	  app_version: ${{ steps.semver.outputs.version }}
	steps:
	  - name: Extract SemVer from tag
		id: semver
		shell: pwsh
		run: |
		  $ref = "${{ github.ref_name }}"
		  # Match MAJOR.MINOR.PATCH, optionally preceded by 'v'
		  $ver = if ($ref -match '^v?(\d+\.\d+\.\d+)') { $Matches[1] } else { "0.0.0" }
		  Write-Output "version=$ver" >> $env:GITHUB_OUTPUT
		  Write-Host "Building version: $ver"

  # ─────────────────────────────────────────────────────────────────────
  # dotnet publish — produces self-contained single-file binary per RID
  # ─────────────────────────────────────────────────────────────────────
  publish:
	name: Publish (${{ matrix.rid }})
	needs: version
	runs-on: ubuntu-latest
	strategy:
	  fail-fast: false
	  matrix:
		rid: [ win-x64, win-x86, linux-x64, linux-arm64, linux-arm ]
	steps:
	  - uses: actions/checkout@v4

	  - uses: actions/setup-dotnet@v4
		with:
		  dotnet-version: '8.0.x'

	  - name: Restore
		run: dotnet restore GreenSwampAlpaca.sln

	  - name: Publish ${{ matrix.rid }}
		run: >
		  dotnet publish GreenSwamp.Alpaca.Server/GreenSwamp.Alpaca.Server.csproj
		  -c Release
		  -r ${{ matrix.rid }}
		  --self-contained true
		  -p:PublishSingleFile=true
		  -p:PublishTrimmed=false
		  -p:Version=${{ needs.version.outputs.app_version }}
		  -o publish/${{ matrix.rid }}

	  - uses: actions/upload-artifact@v4
		with:
		  name: greenswamp-alpaca-${{ matrix.rid }}
		  path: publish/${{ matrix.rid }}/**
		  retention-days: 7

  # ─────────────────────────────────────────────────────────────────────
  # Build .deb packages for Linux x64 and Raspberry Pi (arm64 + armhf)
  # ─────────────────────────────────────────────────────────────────────
  build-deb:
	name: Build DEB (${{ matrix.rid }})
	needs: [ version, publish ]
	runs-on: ubuntu-latest
	strategy:
	  fail-fast: false
	  matrix:
		rid: [ linux-x64, linux-arm64, linux-arm ]
	steps:
	  - uses: actions/checkout@v4

	  - uses: actions/download-artifact@v4
		with:
		  name: greenswamp-alpaca-${{ matrix.rid }}
		  path: publish/${{ matrix.rid }}

	  - name: Install packaging tools
		run: sudo apt-get install -y --no-install-recommends fakeroot dpkg-dev

	  - name: Build DEB
		run: |
		  chmod +x Installer/Linux/build-deb.sh
		  ./Installer/Linux/build-deb.sh \
			${{ matrix.rid }} \
			${{ needs.version.outputs.app_version }}

	  - uses: actions/upload-artifact@v4
		with:
		  name: deb-${{ matrix.rid }}
		  path: "*.deb"
		  retention-days: 7

  # ─────────────────────────────────────────────────────────────────────
  # Build .msi packages for Windows x64 and x86
  # ─────────────────────────────────────────────────────────────────────
  build-msi:
	name: Build MSI (${{ matrix.rid }})
	needs: [ version, publish ]
	runs-on: windows-latest
	strategy:
	  fail-fast: false
	  matrix:
		include:
		  # UpgradeCode is the permanent GUID for each RID's product family
		  - rid: win-x64
			upgrade_code: "0BFB5E9F-4475-4BFA-A8A5-1F26CD982B1C"
		  - rid: win-x86
			upgrade_code: "D910192D-77C9-499E-80E9-10428B7FB80F"
	steps:
	  - uses: actions/checkout@v4

	  - uses: actions/setup-dotnet@v4
		with:
		  dotnet-version: '8.0.x'

	  - uses: actions/download-artifact@v4
		with:
		  name: greenswamp-alpaca-${{ matrix.rid }}
		  path: publish/${{ matrix.rid }}

	  - name: Install WiX v5 and extensions
		run: |
		  dotnet tool install --global wix --version 5.0.0
		  wix extension add WixToolset.UI.wixext/5.0.0
		  wix extension add WixToolset.Firewall.wixext/5.0.0
		  wix extension add WixToolset.Util.wixext/5.0.0

	  - name: Build MSI
		run: >
		  dotnet build Installer/Windows/GreenSwamp.Alpaca.Installer.wixproj
		  -c Release
		  -p:Platform=${{ matrix.rid }}
		  -p:Version=${{ needs.version.outputs.app_version }}
		  -p:UpgradeCode=${{ matrix.upgrade_code }}
		  "-p:PublishDir=${{ github.workspace }}/publish/${{ matrix.rid }}/"

	  - name: Restore signing certificate
		shell: pwsh
		run: |
		  $bytes = [Convert]::FromBase64String("${{ secrets.CODESIGN_PFX_B64 }}")
		  [IO.File]::WriteAllBytes("sign.pfx", $bytes)

	  - name: Sign MSI with self-signed certificate
		shell: pwsh
		run: |
		  $pwd = ConvertTo-SecureString "${{ secrets.CODESIGN_PFX_PASSWORD }}" -Force -AsPlainText
		  Import-PfxCertificate -FilePath sign.pfx -CertStoreLocation Cert:\CurrentUser\My -Password $pwd | Out-Null
		  $thumb = (Get-PfxCertificate sign.pfx).Thumbprint
		  $msi = Get-ChildItem -Recurse -Filter "*.msi" | Select-Object -First 1 -ExpandProperty FullName
		  signtool sign /fd SHA256 /sha1 $thumb /tr http://timestamp.digicert.com /td SHA256 $msi
		  Write-Host "Signed: $msi"

	  - name: Clean up certificate file
		if: always()
		shell: pwsh
		run: Remove-Item sign.pfx -ErrorAction SilentlyContinue

	  - uses: actions/upload-artifact@v4
		with:
		  name: msi-${{ matrix.rid }}
		  path: "**/*.msi"
		  retention-days: 7

  # ─────────────────────────────────────────────────────────────────────
  # Create GitHub Release and attach all installer artefacts
  # Only runs on tag pushes (not on branch pushes or PRs)
  # ─────────────────────────────────────────────────────────────────────
  release:
	name: Create GitHub Release
	needs: [ version, build-deb, build-msi ]
	runs-on: ubuntu-latest
	if: startsWith(github.ref, 'refs/tags/v')
	permissions:
	  contents: write
	steps:
	  - uses: actions/download-artifact@v4
		with:
		  path: release-assets
		  merge-multiple: true

	  - name: List release assets (diagnostic)
		run: find release-assets -type f | sort

	  - name: Create GitHub Release
		uses: softprops/action-gh-release@v2
		with:
		  name: "GreenSwamp Alpaca Server ${{ needs.version.outputs.app_version }}"
		  tag_name: ${{ github.ref_name }}
		  generate_release_notes: true
		  files: |
			release-assets/**/*.msi
			release-assets/**/*.deb
```

---

## 8. Linux Installer Scripts — Complete File Contents

All five files live under `Installer/Linux/` in the repository.

### `Installer/Linux/build-deb.sh`

```bash
#!/usr/bin/env bash
# Build a .deb package for one RID.
# Usage: ./build-deb.sh <RID> <VERSION>
#   e.g. ./build-deb.sh linux-arm64 1.2.3
set -euo pipefail

RID="${1:?First argument required: RID (linux-x64 | linux-arm64 | linux-arm)}"
VERSION="${2:?Second argument required: VERSION (e.g. 1.2.3)}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

case "$RID" in
  linux-x64)   DEB_ARCH="amd64"  ;;
  linux-arm64) DEB_ARCH="arm64"  ;;
  linux-arm)   DEB_ARCH="armhf"  ;;
  *) echo "ERROR: Unknown RID '$RID'. Expected linux-x64, linux-arm64, or linux-arm." >&2; exit 1 ;;
esac

PKG_NAME="greenswamp-alpaca-server_${VERSION}_${DEB_ARCH}"
PKG_ROOT="${PKG_NAME}"
INSTALL_DIR="${PKG_ROOT}/opt/greenswamp/alpaca-server"
DEBIAN_DIR="${PKG_ROOT}/DEBIAN"
SYSTEMD_DIR="${PKG_ROOT}/lib/systemd/system"

echo "Building ${PKG_NAME}.deb ..."
rm -rf "${PKG_ROOT}"

mkdir -p "${INSTALL_DIR}" "${DEBIAN_DIR}" "${SYSTEMD_DIR}"

# Stage publish output
cp -r "publish/${RID}/." "${INSTALL_DIR}/"
chmod +x "${INSTALL_DIR}/GreenSwamp.Alpaca.Server" 2>/dev/null || true

# Stage systemd unit file
cp "${SCRIPT_DIR}/systemd/greenswamp-alpaca.service" "${SYSTEMD_DIR}/"

# Stage and chmod maintainer scripts
for script in postinst prerm postrm; do
  cp "${SCRIPT_DIR}/debian/${script}" "${DEBIAN_DIR}/"
  chmod 0755 "${DEBIAN_DIR}/${script}"
done

# Calculate installed size in KB (required by Debian control)
INSTALLED_SIZE=$(du -sk "${INSTALL_DIR}" | cut -f1)

# Substitute tokens in the control template
sed \
  -e "s/@@VERSION@@/${VERSION}/g"                \
  -e "s/@@ARCH@@/${DEB_ARCH}/g"                  \
  -e "s/@@INSTALLED_SIZE@@/${INSTALLED_SIZE}/g"  \
  "${SCRIPT_DIR}/debian/control.template" > "${DEBIAN_DIR}/control"

# Build the .deb
fakeroot dpkg-deb --build "${PKG_ROOT}" "${PKG_NAME}.deb"

echo "SUCCESS: ${PKG_NAME}.deb"
```

### `Installer/Linux/debian/control.template`

```
Package: greenswamp-alpaca-server
Version: @@VERSION@@
Architecture: @@ARCH@@
Installed-Size: @@INSTALLED_SIZE@@
Maintainer: Principia4834 <maintainer@example.com>
Depends:
Recommends: ufw
Section: science
Priority: optional
Homepage: https://github.com/Principia4834/GreenSwampAlpaca
Description: GreenSwamp Alpaca Server
 Self-contained ASCOM Alpaca server for SkyWatcher mount control.
 Provides a Blazor web management interface on port 31426.
 .
 Supported mounts: SkyWatcher EQ series (AZ-EQ5, AZ-EQ6, EQ6-R, etc.)
 Access the management UI at http://<host>:31426 from any browser on the
 local network.
```

### `Installer/Linux/debian/postinst`

```bash
#!/bin/bash
set -e

# Create dedicated system user; add to dialout group for serial port access
adduser --system --no-create-home --group greenswamp 2>/dev/null || true
usermod -aG dialout greenswamp 2>/dev/null || true

# Enable and start the systemd service
if command -v systemctl >/dev/null 2>&1; then
	systemctl daemon-reload
	systemctl enable greenswamp-alpaca.service
	systemctl start  greenswamp-alpaca.service || true
fi

echo "GreenSwamp Alpaca Server installed successfully."
echo "Management UI: http://localhost:31426"
```

### `Installer/Linux/debian/prerm`

```bash
#!/bin/bash
set -e

if command -v systemctl >/dev/null 2>&1; then
	systemctl stop    greenswamp-alpaca.service 2>/dev/null || true
	systemctl disable greenswamp-alpaca.service 2>/dev/null || true
fi
```

### `Installer/Linux/debian/postrm`

```bash
#!/bin/bash
set -e

if command -v systemctl >/dev/null 2>&1; then
	systemctl daemon-reload 2>/dev/null || true
fi

# On purge: remove application files and system user
if [ "$1" = "purge" ]; then
	rm -rf /opt/greenswamp
	rm -rf /var/log/greenswamp
	deluser --system greenswamp 2>/dev/null || true
fi
```

### `Installer/Linux/systemd/greenswamp-alpaca.service`

```ini
[Unit]
Description=GreenSwamp Alpaca Server
Documentation=https://github.com/Principia4834/GreenSwampAlpaca
After=network.target
Wants=network.target

[Service]
Type=simple
User=greenswamp
Group=greenswamp
ExecStart=/opt/greenswamp/alpaca-server/GreenSwamp.Alpaca.Server
WorkingDirectory=/opt/greenswamp/alpaca-server
Restart=on-failure
RestartSec=5s
TimeoutStopSec=10s
StandardOutput=journal
StandardError=journal
SyslogIdentifier=greenswamp-alpaca

[Install]
WantedBy=multi-user.target
```

---

## 9. Pre-requisites Checklist

| # | Prerequisite | Owner | Status |
|---|-------------|-------|--------|
| P-1 | `Directory.Build.props` created at solution root with `<Version>0.0.0</Version>` | Dev | ✅ |
| P-2 | `release.yml` created in `.github/workflows/` (replaces `publish.yml`) | Dev | ✅ |
| P-3 | Self-signed PFX created locally and added as GitHub secrets `CODESIGN_PFX_B64` + `CODESIGN_PFX_PASSWORD` | Dev | ⬜ |
| P-4 | `Installer\Windows\GreenSwamp.Alpaca.Installer.wixproj` created (content in § 4) | Dev | ✅ |
| P-5 | `Installer\Windows\Product.wxs` created (content in § 4) | Dev | ✅ |
| P-6 | `Installer\Windows\Resources\License.rtf` authored (plain text acceptable for v1) | Dev | ✅ |
| P-7 | `Installer\Windows\Resources\banner.bmp` (493×58 px) and `dialog.bmp` (493×312 px) created | Dev | ✅ |
| P-8 | `Installer\Linux\build-deb.sh` created (content in § 8) | Dev | ✅ |
| P-9 | `Installer\Linux\debian\` folder created with all four files from § 8 | Dev | ✅ |
| P-10 | `Installer\Linux\systemd\greenswamp-alpaca.service` created (content in § 8) | Dev | ✅ |
| P-11 | `fakeroot` + `dpkg-dev` on `ubuntu-latest` — pre-installed by default | CI | ✅ |
| P-12 | `GITHUB_TOKEN` `contents: write` permission — set in `release.yml` (§ 7) | CI | ✅ |
| P-13 | WiX v5 installed by workflow step — no pre-installation needed | CI | ✅ |
| P-14 | Test install/uninstall/upgrade on a clean Windows 10 or 11 VM | QA | ⬜ |
| P-15 | Test DEB install on Ubuntu 22.04 and Raspberry Pi OS (64-bit and 32-bit) | QA | ⬜ |

---

## 10. Open Questions — Status After Refinement

| # | Question | Decision |
|---|----------|----------|
| OQ-1 | Service vs. tray app? | **Windows Service** (headless). Start-menu shortcut opens `http://localhost:31426` in the default browser. Tray-app is a separate future feature. |
| OQ-2 | Code-signing cert? | **Self-signed for v1** (§ 2). Swap GitHub secrets when a commercial cert is purchased — no workflow changes needed. |
| OQ-3 | APT repository? | **GitHub Releases raw `.deb` download** for v1. A full `apt` repo with `apt upgrade` support is a future follow-on. |
| OQ-4 | Open browser post-install? | **Follow-on.** A WiX `<CustomAction>` calling `ShellExec` to open `http://localhost:31426` can be added after initial release. |
| OQ-5 | Preserve user settings on upgrade? | **Yes** — `%AppData%\GreenSwampAlpaca` (Windows) and user home directories (Linux) are never touched by any installer action. |
| OQ-6 | Port configurable at install time? | **No for v1** — hardcoded `31426`. |
| OQ-7 | RPM support? | **Out of scope for v1.** Add after DEB is validated. |
| OQ-8 | dotnet-deb vs hand-authored? | **Hand-authored** — full control over service lifecycle. See § 5 for full analysis. |

---

## 11. Recommended Implementation Order

1. Create `Directory.Build.props` at solution root — run `dotnet build` locally to verify nothing breaks.
2. Create `Installer\Linux\` tree with all files from § 8 — test `build-deb.sh` locally in WSL2 against a `linux-x64` publish output.
3. Create `Installer\Windows\` files from § 4 — install WiX locally (`dotnet tool install --global wix`) and do a local test build.
4. Create `Installer\Windows\Resources\` — author `License.rtf` (plain text is fine for v1), and placeholder `banner.bmp` / `dialog.bmp` images.
5. Create `.github/workflows/release.yml` from § 7 — delete or disable the existing `publish.yml` to avoid duplicate runs.
6. Create the self-signed PFX locally (§ 2) and add both secrets to the GitHub repo settings.
7. Push a pre-release tag (`git tag v0.9.0-rc1 && git push origin v0.9.0-rc1`) — watch the Actions tab; all five jobs should go green in ~10 minutes.
8. Download each installer artefact from the GitHub Release and test on its target platform.
9. Bump to `v1.0.0` — the GitHub Release is created automatically with all five installers attached and release notes generated from the commit log.
10. Swap to a commercial code-signing cert when available by replacing the two GitHub secrets only.

---

*End of installer plan — GreenSwamp Alpaca Server.*
