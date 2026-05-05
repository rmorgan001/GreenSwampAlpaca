# GS Alpaca — Clean-Slate UI Audit
**Author:** GitHub Copilot  
**Prepared for:** Andy  
**Date:** 2026-05-05 13:11

---

## Purpose

This document analyses the current Blazor UI as it stands after iterative theming of the default Blazor
template.  It identifies every piece of legacy, redundant, or Bootstrap-fighting code that would **not**
exist in a clean-slate implementation targeting the same GS dark-green design.  It is a read-only audit —
no code has been changed.

---

## 1. What a Clean-Slate Implementation Would Look Like

A greenfield Blazor Server project targeting this design would have:

| Concern | Clean-slate approach |
|---------|---------------------|
| CSS framework | **None, or a minimal reset only.** The GS design system is self-contained; Bootstrap adds ~230 KB and requires constant variable bridging to un-do its defaults. |
| Layout | Pure CSS `grid` or `flexbox` in `MainLayout.razor.css`. No `.page`, `.sidebar`, `main` classes derived from the Blazor template. |
| Navigation | A plain `<nav>` element with `<NavLink>` items. No Bootstrap `.navbar`, `.navbar-dark`, `.navbar-toggler`, `.container-fluid`, `.collapse` wrappers. |
| Icons | `fonts.css` + `wwwroot/fonts/` exactly as-is — self-hosted Material Symbols. This part is clean. |
| Typography | `fonts.css` exactly as-is — self-hosted Roboto. This part is also clean. |
| Design tokens | `site.css` `:root` block exactly as-is — this is the correct single source of truth. |
| Component styles | Scoped `.razor.css` files per component, referencing only `var(--gs-*)` tokens. No Bootstrap overrides needed. |
| Host page | `_Layout.cshtml` with only `fonts.css`, `site.css`, and the Blazor script. No Bootstrap link at all. |

---

## 2. Legacy / Redundant Artefacts Found

### 2.1  Open Iconic — Entire Directory Is Dead Weight

**Location:** `wwwroot/css/open-iconic/`

**What it is:** The default Blazor template ships with Open Iconic as its icon set. All `<span class="oi oi-*">` usages have been replaced with Material Symbols. Open Iconic is no longer referenced by any `.razor` or `.cshtml` file.

**What remains on disk (all unused):**

| File | Size |
|------|------|
| `css/open-iconic/font/css/open-iconic-bootstrap.min.css` | 9.2 KB |
| `css/open-iconic/font/fonts/open-iconic.eot` | 27.5 KB |
| `css/open-iconic/font/fonts/open-iconic.otf` | 20.5 KB |
| `css/open-iconic/font/fonts/open-iconic.svg` | 54 KB |
| `css/open-iconic/font/fonts/open-iconic.ttf` | 27.4 KB |
| `css/open-iconic/font/fonts/open-iconic.woff` | 14.6 KB |
| `css/open-iconic/FONT-LICENSE` | — |
| `css/open-iconic/ICON-LICENSE` | — |
| `css/open-iconic/README.md` | — |
| **Total** | **~153 KB of unused assets** |

**Clean-slate action:** Delete the entire `wwwroot/css/open-iconic/` directory.

---

### 2.2  Bootstrap — Loaded but Largely Fought Against

**Location:** `wwwroot/css/bootstrap/bootstrap.min.css` (226.7 KB + 438.6 KB source map)

**The problem:** Bootstrap was never part of the GS design. It is the default Blazor template dependency.
The current implementation loads Bootstrap and then immediately overrides its core tokens in `site.css` via
the `[data-bs-theme="dark"], :root` bridge block (~30 lines). Additionally, `NavMenu.razor.css` contains
`.navbar { background-color: transparent !important; background-image: none !important; }` specifically to
stop Bootstrap painting the sidebar purple.

**Bootstrap overrides currently required by `site.css`:**

- `--bs-body-bg`, `--bs-body-color` and their `-rgb` variants
- `--bs-secondary-bg`, `--bs-tertiary-bg`
- `--bs-border-color`, `--bs-border-color-translucent`
- `--bs-heading-color`, `--bs-link-color`, `--bs-link-hover-color`
- `--bs-card-bg`, `--bs-card-cap-bg`, `--bs-card-border-color`
- `--bs-emphasis-color`, `--bs-secondary-color`
- `--bs-primary` and status colour `-rgb` triplets
- `color-scheme: dark`
- `.card`, `.card-header`, `.card-body` full re-declarations
- `.table`, `.table-striped` re-declarations
- `.alert-*` full re-declarations
- `.btn-close` filter inversion

**In `NavMenu.razor.css`:** `.navbar { background-color: transparent !important }` and multiple `!important`
overrides on `.nav-item ::deep a.nav-link` exist only because Bootstrap's `.navbar-dark` specificity
would otherwise win.

**In `_Layout.cshtml`:** `data-bs-theme="dark"` on the `<html>` element is required to activate Bootstrap
5.3's own dark-mode variables. In a clean-slate app with no Bootstrap this attribute is meaningless.

**Clean-slate action:** Remove Bootstrap entirely. Remove the `[data-bs-theme]` bridge block from
`site.css`. Remove `data-bs-theme="dark"` from `_Layout.cshtml`. Remove all `!important` overrides that
exist only to beat Bootstrap specificity. The GS design system already defines every token needed.

**Caveat:** Some `.razor` pages use Bootstrap utility classes (`.row`, `.col-*`, `.d-flex`, `.btn`,
`.form-control`, `.nav-tabs`, etc.). These would need to be either replaced with custom classes or kept
with a targeted import of only the Bootstrap modules actually used. A full audit of page-level Bootstrap
usage is a separate task.

---

### 2.3  Orphaned CSS Token: `--gs-bg-sidebar`

**Location:** `site.css`, line 12

```css
--gs-bg-sidebar: linear-gradient(180deg, #052767 0%, #3a0647 70%);
```

**The problem:** This token was defined when the sidebar still used the default Blazor purple gradient.
The sidebar colour was subsequently changed to a hardcoded `#0d1117` directly in `MainLayout.razor.css`.
The `--gs-bg-sidebar` token is now defined but never consumed anywhere.

**Clean-slate action:** Delete this token from `:root`. If a sidebar token is desired, define it as
`--gs-bg-sidebar: #0d1117` and reference it in `MainLayout.razor.css` as
`background-color: var(--gs-bg-sidebar)`.

---

### 2.4  Bootstrap Navbar Markup in `NavMenu.razor`

**Location:** `NavMenu.razor`, lines 1–9

```razor
<div class="top-row ps-3 navbar navbar-dark">
	<div class="container-fluid">
		<a class="navbar-brand gs-brand" href="">
			...
		</a>
		<button title="Navigation menu" class="navbar-toggler" ...>
			<span class="navbar-toggler-icon"></span>
		</button>
	</div>
</div>
```

**The problem:** `navbar`, `navbar-dark`, `container-fluid`, `navbar-brand`, `navbar-toggler`, and
`navbar-toggler-icon` are all Bootstrap classes. They bring Bootstrap's navbar defaults (including the
purple background) which then have to be cancelled in `NavMenu.razor.css`. In a clean-slate app, this
would simply be a `<div class="gs-nav-header">` with a brand link and a hamburger button — no Bootstrap
classes at all.

**Supporting evidence:** `NavMenu.razor.css` opens with:
```css
.navbar {
	background-color: transparent !important;
	background-image: none !important;
}
```
This rule exists solely to undo what the `navbar navbar-dark` classes impose.

**Clean-slate action:** Replace the Bootstrap navbar wrapper with a simple semantic div using only GS
classes. Remove the Bootstrap overrides from `NavMenu.razor.css`.

---

### 2.5  `top-row` Duplication Across Two CSS Files

**Location:** `MainLayout.razor.css` line 17 AND `NavMenu.razor.css` line 12

Both files define styles for `.top-row`. The version in `NavMenu.razor.css` uses `!important` on
`background-color` to override the version in `MainLayout.razor.css`. This is a classic symptom of
iterative patching rather than clean ownership — each file patches what the other set.

**Clean-slate action:** Own `.top-row` in exactly one place (logically `MainLayout.razor.css`, since that
is the layout component that renders the top row). Delete the duplicate from `NavMenu.razor.css`.

---

### 2.6  Scoped `::deep` Combinator Used to Fight Bootstrap Specificity

**Location:** `NavMenu.razor.css`, multiple rules

```css
.nav-item ::deep a.nav-link { ... !important }
.nav-item ::deep a.nav-link.active { ... !important }
.nav-item ::deep a.nav-link:hover { ... !important }
```

**The problem:** `::deep` is Blazor's scoped CSS penetration combinator. It is legitimately used to style
child component elements. However, the `!important` declarations attached to these rules exist solely
because Bootstrap's `.nav-link` rules otherwise win the cascade. In a Bootstrap-free implementation, the
`::deep` combinator could still be used if needed, but `!important` would not be required.

**Clean-slate action:** Once Bootstrap is removed, drop `!important` from these nav-link rules. The
`::deep` combinator itself may still be needed for Blazor scoping reasons — that is correct usage.

---

### 2.7  Font Asset `download-fonts.ps1` Deployed to `wwwroot`

**Location:** `wwwroot/fonts/download-fonts.ps1`

**The problem:** This is the PowerShell script used during development to fetch font files. It was saved
to `wwwroot/fonts/` as a working directory convenience. In production, `wwwroot` is the web root and this
file is technically a publicly accessible server resource (though `.ps1` is unlikely to be executed by a
browser, it is unnecessary noise).

**Clean-slate action:** Move `download-fonts.ps1` to a `tools/` or `scripts/` folder at solution root
(or simply delete it once the fonts are committed). It should not live in `wwwroot`.

---

### 2.8  `partly_cloudy_day` Icon — Not Confirmed in Font

**Location:** `NavMenu.razor`, line 70 (ObservingConditions device loop)

```razor
<span class="material-symbols-outlined" aria-hidden="true">partly_cloudy_day</span>
```

**The problem:** During the icon audit that discovered `telescope` was not a valid Material Symbols name,
`partly_cloudy_day` was identified as a potential issue (the batch request containing it returned HTTP 400
from the Google Fonts subset API). While this could have been an API limit issue rather than an invalid
name, it warrants verification.

**Clean-slate action:** Verify `partly_cloudy_day` renders correctly in the browser. If it shows as text,
replace it with `cloud` or `wb_cloudy` which are confirmed valid names.

---

### 2.9  `--gs-bg-sidebar` Hardcoded in `MainLayout.razor.css`

**Location:** `MainLayout.razor.css`, `.sidebar` rule

```css
.sidebar {
	background-color: #0d1117;
	border-right:     1px solid rgba(76, 175, 80, 0.2);
}
```

**The problem:** Both values are hardcoded hex/rgba rather than referencing GS design tokens. In a
clean-slate implementation they would use `var(--gs-bg-sidebar)` and
`1px solid rgba(var(--gs-accent-500-rgb), 0.2)`. This makes it harder to retheme later.

**Clean-slate action:** Define `--gs-bg-sidebar: #0d1117` and `--gs-accent-500-rgb: 76, 175, 80` in `:root`,
then reference them.

---

### 2.10  Bootstrap Source Map Served to Browser

**Location:** `wwwroot/css/bootstrap/bootstrap.min.css.map` (438.6 KB)

**The problem:** The Bootstrap source map is 438 KB and is served as a static web asset. Browser DevTools
will request it automatically. In production this is unnecessary bandwidth.

**Clean-slate action:** If Bootstrap is retained, remove the `.map` file for production builds or exclude
it via `<StaticWebAssets>` configuration. If Bootstrap is removed (see 2.2), this disappears entirely.

---

## 3. Summary Table

| # | Artefact | Location | Type | Impact | Clean-slate Action |
|---|----------|----------|------|--------|-------------------|
| 2.1 | Open Iconic directory | `wwwroot/css/open-iconic/` | Dead asset | 153 KB wasted | Delete directory |
| 2.2 | Bootstrap CSS | `wwwroot/css/bootstrap/` | Framework conflict | 227 KB + constant overrides | Remove Bootstrap; audit page utility class usage first |
| 2.3 | `--gs-bg-sidebar` token | `site.css` line 12 | Orphaned token | Minor / misleading | Delete or update to `#0d1117` and use in layout |
| 2.4 | Bootstrap navbar markup | `NavMenu.razor` lines 1–9 | Legacy structure | Causes purple sidebar without CSS counter-patch | Replace with plain GS-classed divs |
| 2.5 | `.top-row` duplication | `MainLayout.razor.css` + `NavMenu.razor.css` | CSS conflict | Requires `!important` to resolve | Own in one file only |
| 2.6 | `::deep` + `!important` nav rules | `NavMenu.razor.css` | Bootstrap specificity patch | Fragile, hard to maintain | Remove `!important` once Bootstrap gone |
| 2.7 | `download-fonts.ps1` | `wwwroot/fonts/` | Dev script in web root | Publicly accessible | Move to `tools/` or `scripts/` at solution root |
| 2.8 | `partly_cloudy_day` icon | `NavMenu.razor` line 70 | Unverified icon name | May render as text | Verify in browser; replace if broken |
| 2.9 | Hardcoded colours in layout | `MainLayout.razor.css` | Token bypass | Harder to retheme | Use `var(--gs-*)` tokens |
| 2.10 | Bootstrap source map | `wwwroot/css/bootstrap/*.map` | Dev artefact in web root | 438 KB unnecessary download | Remove for production |

---

## 4. What Is Already Clean

Not everything is legacy. The following are well-structured and would carry over unchanged to a clean-slate
implementation:

- **`wwwroot/css/fonts.css`** — Self-hosted font declarations. Correct, no CDN dependency.
- **`wwwroot/fonts/`** — Roboto + Roboto Mono + Material Symbols woff2 files. Correct approach.
- **`site.css` `:root` token block** — The GS design token palette is well-structured and complete.
- **`site.css` component classes** (`.gs-card`, `.gs-badge-*`, `.gs-monitor`, `.gs-table`, `.gs-monospace`) — These are clean utility classes that reference only `var(--gs-*)` tokens.
- **`TelescopeState.razor.css`** — Clean scoped styles using only GS tokens.
- **`NavMenu.razor.css` brand and nav-item rules** — The `.gs-brand`, `.gs-brand-prefix`, `.gs-brand-suffix` and icon sizing rules are clean and correct.
- **Icon usage in `.razor` files** — The `<span class="material-symbols-outlined">icon_name</span>` pattern is correct Material Symbols usage.
- **`_Layout.cshtml` head section** — Now clean: `fonts.css` → `bootstrap.min.css` → `site.css`. No CDN links.

---

## 5. Recommended Clean-Slate Sequence

If a clean implementation were started from scratch, the recommended order would be:

1. Create a new Blazor Server project (no Bootstrap template — use the empty template or remove Bootstrap immediately).
2. Copy `wwwroot/fonts/` and `wwwroot/css/fonts.css` verbatim.
3. Copy the `site.css` `:root` token block and component utility classes. Remove the Bootstrap bridge block and all `!important` overrides.
4. Write `MainLayout.razor` using pure flex layout with GS token references.
5. Write `NavMenu.razor` with plain semantic HTML — no Bootstrap navbar classes.
6. Write `NavMenu.razor.css` with GS token rules only — no `!important`, no `.navbar` override.
7. Write page `.razor.css` files referencing only `var(--gs-*)` tokens.
8. Move `download-fonts.ps1` to `tools/` at solution root.
9. Add `.gitignore` exclusion or build target to strip `*.map` files from production publish.
