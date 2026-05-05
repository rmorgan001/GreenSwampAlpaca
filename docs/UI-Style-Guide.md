# GreenSwamp Alpaca — UI Style Guide
## Derived from GSServer WPF App (Principia4834/GSServer) → Blazor Redesign
**Last updated: 2026-05-05 08:16**

---

## 1. Overview & Design Philosophy

The GreenSwamp Alpaca Blazor UI should inherit the **dark, space-themed professional aesthetic** of the original GSServer WPF application, which used **Material Design Dark theme** with a Grey/Red palette. The Blazor redesign translates this into a CSS custom-property system with Bootstrap 5 as the grid/utility foundation.

### Core Principles
- **Dark-first**: the app runs on observatory computers, often in low-light environments. Dark backgrounds protect night vision.
- **High contrast data**: telescope coordinates, status values, and diagnostics must be immediately legible.
- **Minimal chrome**: the WPF app used `WindowStyle="None"` with a custom title bar and no visual clutter. Match this intent in Blazor with a clean, borderless feel.
- **Roboto typography**: the WPF app explicitly specified Roboto (via MaterialDesignThemes). Use Google Fonts Roboto in Blazor.
- **Red accent on grey base**: the WPF theme used `MaterialDesignColor.Grey` as primary and `MaterialDesignColor.Red` as accent. These same tokens should drive the Blazor palette.

---

## 2. Colour Palette

All colours are derived from Material Design Dark + Grey/Red and the existing Blazor sidebar gradient.

### CSS Custom Properties (define in `:root`)

```css
:root {
  /* --- Base surfaces --- */
  --gs-bg-app:         #121212;   /* App background (MaterialDesignTheme.Dark body) */
  --gs-bg-paper:       #1e1e1e;   /* Card / panel surface (MaterialDesignPaper) */
  --gs-bg-elevated:    #2a2a2a;   /* Raised surfaces, drawers, table rows:hover */
  --gs-bg-sidebar:     linear-gradient(180deg, #052767 0%, #3a0647 70%); /* Existing sidebar – KEEP */

  /* --- Primary: Material Grey --- */
  --gs-grey-50:        #fafafa;
  --gs-grey-100:       #f5f5f5;
  --gs-grey-300:       #e0e0e0;
  --gs-grey-500:       #9e9e9e;
  --gs-grey-700:       #616161;
  --gs-grey-900:       #212121;

  /* --- Accent: Material Red --- */
  --gs-red-300:        #e57373;   /* Hover / secondary accent */
  --gs-red-500:        #f44336;   /* Primary accent (buttons, highlights, active states) */
  --gs-red-700:        #d32f2f;   /* Pressed / darker accent */
  --gs-red-a200:       #ff5252;   /* Bright accent (alerts, badges) */

  /* --- Text --- */
  --gs-text-primary:   rgba(255, 255, 255, 0.87);   /* Body text on dark */
  --gs-text-secondary: rgba(255, 255, 255, 0.60);   /* Labels, captions */
  --gs-text-disabled:  rgba(255, 255, 255, 0.38);   /* Disabled / placeholder */
  --gs-text-hint:      rgba(255, 255, 255, 0.38);

  /* --- Dividers --- */
  --gs-divider:        rgba(255, 255, 255, 0.12);

  /* --- Status / Semantic --- */
  --gs-success:        #66bb6a;   /* Connected, OK */
  --gs-warning:        #ffa726;   /* Slewing, in-progress */
  --gs-error:          #f44336;   /* Error, disconnected (same as red-500) */
  --gs-info:           #42a5f5;   /* Informational */

  /* --- Existing fieldset accent (site.css) – retained --- */
  --gs-navy:           #002157;
}
```

### Usage Map

| Context | Token |
|---|---|
| Page / app background | `--gs-bg-app` |
| Cards, panels, fieldsets | `--gs-bg-paper` |
| Table row hover, drawer | `--gs-bg-elevated` |
| Sidebar navigation | `--gs-bg-sidebar` |
| Primary button fill | `--gs-red-500` |
| Primary button hover | `--gs-red-700` |
| Active nav link | `rgba(255,255,255,0.25)` (existing, keep) |
| Body text | `--gs-text-primary` |
| Labels / hints | `--gs-text-secondary` |
| Disabled | `--gs-text-disabled` |
| Dividers / borders | `--gs-divider` |
| Status: connected | `--gs-success` |
| Status: error / disconnected | `--gs-error` |
| Status: slewing / busy | `--gs-warning` |

---

## 3. Typography

The WPF app used **Roboto** via MaterialDesignThemes with `TextElement.FontSize="14"` and `FontStretch="Normal"`.

### Font Stack

```css
@import url('https://fonts.googleapis.com/css2?family=Roboto:wght@300;400;500;700&family=Roboto+Mono:wght@400;500&display=swap');

:root {
  --gs-font-sans:  'Roboto', 'Helvetica Neue', Helvetica, Arial, sans-serif;
  --gs-font-mono:  'Roboto Mono', 'Courier New', monospace; /* telemetry / coordinate values */
}

html, body {
  font-family: var(--gs-font-sans);
  font-size:   14px;          /* match WPF TextElement.FontSize="14" */
  font-weight: 400;
  color:        var(--gs-text-primary);
  background:   var(--gs-bg-app);
}
```

### Type Scale

| Role | Element | Size | Weight | Notes |
|---|---|---|---|---|
| Page heading | `h1` | 24px / 1.5rem | 300 | Section title (e.g., "Mount Settings") |
| Section heading | `h2` / drawer title | 18px / 1.125rem | 400 | Matches WPF `FontSize="18"` drawer labels |
| Sub-heading | `h3` | 16px / 1rem | 500 | Tab section headings |
| Body | `p`, `td`, `label` | 14px / 0.875rem | 400 | Base size |
| Monitor / log | `.gs-monospace` | 13px / 0.8125rem | 400 | Roboto Mono — matches WPF monitor `FontSize="13"` |
| Caption / hint | `.gs-caption` | 12px / 0.75rem | 400 | Secondary metadata |
| Button | `.btn` | 14px / 0.875rem | 500 | Uppercase letter-spacing |

### Coordinate / Telemetry Values

Telescope RA/Dec, Alt/Az, and sensor readings should use the monospace class for alignment:

```html
<span class="gs-monospace">12h 34m 56.7s</span>
```

```css
.gs-monospace {
  font-family: var(--gs-font-mono);
  font-size:   13px;
  font-weight: 500;
  letter-spacing: 0.03em;
  color: var(--gs-text-primary);
}
```

---

## 4. Layout & Grid

The WPF app used a 3-row `Grid`: custom title bar (30px) → tab bar (Auto) → content (`*`). The Blazor equivalent maps to the existing sidebar + main layout but with the dark treatment applied.

### Page Structure

```
┌─────────────────────────────────────────────────────┐
│  SIDEBAR (250px sticky, dark gradient)              │
│  ┌─────────────────────────────────────────────┐    │
│  │  App logo / brand                           │    │
│  │  ─────────────────────────────────────────  │    │
│  │  Nav links                                  │    │
│  └─────────────────────────────────────────────┘    │
│                                                     │
│  MAIN CONTENT AREA                                  │
│  ┌────────────────────────────────────────────────┐ │
│  │  TOP BAR (3.5rem, dark, sticky)                │ │
│  ├────────────────────────────────────────────────┤ │
│  │  PAGE CONTENT                                  │ │
│  │  ┌──────────────────────────────────────────┐  │ │
│  │  │  Tab bar (connection / location / mount) │  │ │
│  │  ├──────────────────────────────────────────┤  │ │
│  │  │  Content panels (cards / fieldsets)      │  │ │
│  │  └──────────────────────────────────────────┘  │ │
│  └────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
```

### Spacing Scale

Follows Material Design 8dp grid system:

| Token | Value | Use |
|---|---|---|
| `--gs-space-1` | 4px | Tight padding (chip gaps) |
| `--gs-space-2` | 8px | Component inner padding |
| `--gs-space-3` | 12px | Default gap between inline items |
| `--gs-space-4` | 16px | Section padding, card padding |
| `--gs-space-6` | 24px | Card margin, section gap |
| `--gs-space-8` | 32px | Page-level vertical rhythm |

---

## 5. Component Styles

### 5.1 Navigation Sidebar

The existing sidebar gradient (`#052767 → #3a0647`) **must be retained** — it is a distinctive brand element.

```css
/* NavMenu.razor.css — key rules to keep/extend */
.top-row {
  background-color: rgba(0, 0, 0, 0.4);
  height: 3.5rem;
}

.nav-item ::deep a {
  color: var(--gs-text-secondary);   /* was #d7d7d7 */
  border-radius: 4px;
  height: 3rem;
  display: flex;
  align-items: center;
}

.nav-item ::deep a.active {
  background-color: rgba(255, 255, 255, 0.25);
  color: white;
}

.nav-item ::deep a:hover {
  background-color: rgba(255, 255, 255, 0.1);
  color: white;
}

/* App brand in sidebar — use Roboto 500 */
.navbar-brand {
  font-family: var(--gs-font-sans);
  font-weight: 500;
  font-size:   1.1rem;
  color:       var(--gs-text-primary);
  letter-spacing: 0.02em;
}
```

### 5.2 Cards / Panels

Replace Bootstrap default cards with a dark surface variant:

```css
.gs-card {
  background:    var(--gs-bg-paper);
  border:        1px solid var(--gs-divider);
  border-radius: 4px;           /* Material uses 4px corner radius */
  padding:       var(--gs-space-4);
  margin-bottom: var(--gs-space-6);
  color:         var(--gs-text-primary);
}

.gs-card-header {
  font-size:     18px;
  font-weight:   400;
  color:         var(--gs-text-primary);
  border-bottom: 1px solid var(--gs-divider);
  padding-bottom: var(--gs-space-2);
  margin-bottom:  var(--gs-space-4);
}
```

### 5.3 Fieldsets (existing pattern — enhance)

The existing `site.css` fieldset with `--gs-navy` border should be updated:

```css
fieldset {
  border:        2px solid var(--gs-divider);
  background:    var(--gs-bg-paper);
  border-radius: 4px;
  padding:       var(--gs-space-4);
  margin-bottom: var(--gs-space-4);
  width:         100%;          /* was fixed 600px — make responsive */
  max-width:     640px;
}

fieldset legend {
  background:    var(--gs-red-500);   /* Red accent instead of navy */
  color:         #fff;
  padding:       2px 8px;
  font-size:     14px;
  font-weight:   500;
  border-radius: 2px;
  letter-spacing: 0.05em;
  text-transform: uppercase;
  margin-left:   12px;
  max-width:     none;          /* remove 22ch cap */
}
```

### 5.4 Buttons

Derived from Material Design flat / raised button patterns in the WPF app:

```css
/* Primary — red accent */
.btn-gs-primary {
  background-color: var(--gs-red-500);
  color:            #fff;
  border:           none;
  border-radius:    4px;
  padding:          6px 16px;
  font-family:      var(--gs-font-sans);
  font-size:        14px;
  font-weight:      500;
  letter-spacing:   0.08em;
  text-transform:   uppercase;
  transition:       background-color 0.2s ease, box-shadow 0.2s ease;
}

.btn-gs-primary:hover {
  background-color: var(--gs-red-700);
  box-shadow:       0 2px 4px rgba(0,0,0,0.4);
}

/* Secondary — outlined on dark */
.btn-gs-secondary {
  background-color: transparent;
  color:            var(--gs-text-secondary);
  border:           1px solid var(--gs-divider);
  border-radius:    4px;
  padding:          5px 15px;
  font-family:      var(--gs-font-sans);
  font-size:        14px;
  font-weight:      500;
  letter-spacing:   0.08em;
  text-transform:   uppercase;
  transition:       background-color 0.2s ease;
}

.btn-gs-secondary:hover {
  background-color: var(--gs-bg-elevated);
  color:            var(--gs-text-primary);
  border-color:     var(--gs-grey-500);
}

/* Danger / destructive */
.btn-gs-danger {
  background-color: transparent;
  color:            var(--gs-error);
  border:           1px solid var(--gs-error);
  border-radius:    4px;
  padding:          5px 15px;
  font-weight:      500;
  text-transform:   uppercase;
  letter-spacing:   0.08em;
}

.btn-gs-danger:hover {
  background-color: rgba(244, 67, 54, 0.12);
}
```

### 5.5 Tabs (MountSettings.razor pattern)

Override Bootstrap nav-tabs for dark theme:

```css
.nav-tabs {
  border-bottom: 1px solid var(--gs-divider);
}

.nav-tabs .nav-link {
  color:            var(--gs-text-secondary);
  background:       transparent;
  border:           none;
  border-bottom:    2px solid transparent;
  border-radius:    0;
  padding:          8px 16px;
  font-size:        14px;
  font-weight:      500;
  letter-spacing:   0.05em;
  transition:       color 0.2s, border-color 0.2s;
}

.nav-tabs .nav-link:hover {
  color:            var(--gs-text-primary);
  border-bottom-color: var(--gs-grey-500);
}

.nav-tabs .nav-link.active {
  color:            var(--gs-red-500);
  background:       transparent;
  border-bottom:    2px solid var(--gs-red-500);
}
```

### 5.6 Form Controls

Dark-themed inputs to match Material Design floating hint pattern:

```css
.form-control,
.form-select {
  background-color: var(--gs-bg-elevated);
  color:            var(--gs-text-primary);
  border:           1px solid var(--gs-divider);
  border-radius:    4px;
  padding:          8px 12px;
  font-family:      var(--gs-font-sans);
  font-size:        14px;
}

.form-control:focus,
.form-select:focus {
  background-color: var(--gs-bg-elevated);
  color:            var(--gs-text-primary);
  border-color:     var(--gs-red-500);
  box-shadow:       0 0 0 1px var(--gs-red-500);
  outline:          none;
}

.form-control::placeholder {
  color: var(--gs-text-hint);
}

.form-label {
  color:       var(--gs-text-secondary);
  font-size:   12px;
  font-weight: 500;
  text-transform: uppercase;
  letter-spacing: 0.08em;
  margin-bottom: 4px;
}

/* Validation states */
.valid.modified:not([type=checkbox]) {
  outline: 1px solid var(--gs-success);
}

.invalid {
  outline: 1px solid var(--gs-error);
}

.validation-message {
  color:     var(--gs-error);
  font-size: 12px;
}
```

### 5.7 Status Badges / Chips

For mount status, connection state, tracking state:

```css
.gs-badge {
  display:        inline-flex;
  align-items:    center;
  padding:        2px 10px;
  border-radius:  12px;      /* pill shape */
  font-size:      12px;
  font-weight:    500;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.gs-badge-connected   { background: rgba(102, 187, 106, 0.2); color: var(--gs-success); }
.gs-badge-disconnected{ background: rgba(244,  67,  54, 0.2); color: var(--gs-error);   }
.gs-badge-slewing     { background: rgba(255, 167,  38, 0.2); color: var(--gs-warning); }
.gs-badge-tracking    { background: rgba( 66, 165, 245, 0.2); color: var(--gs-info);    }
.gs-badge-parked      { background: rgba(158, 158, 158, 0.2); color: var(--gs-grey-300);}
```

Usage:
```html
<span class="gs-badge gs-badge-connected">Connected</span>
<span class="gs-badge gs-badge-slewing">Slewing</span>
```

### 5.8 Monitor / Log Viewer

The WPF `SettingsV.xaml` has a telemetry log viewer (`MonitorEntry`) with a **black background** and fixed-pitch layout. Blazor equivalent:

```css
.gs-monitor {
  background:    #000;
  color:         var(--gs-text-primary);
  font-family:   var(--gs-font-mono);
  font-size:     13px;
  padding:       var(--gs-space-2);
  border-radius: 4px;
  border:        1px solid var(--gs-divider);
  overflow-y:    auto;
  max-height:    300px;
}

.gs-monitor-row {
  display:     grid;
  grid-template-columns: 3rem 6rem 1fr;   /* index | timestamp | message */
  gap:         0 var(--gs-space-2);
  padding:     1px 0;
  white-space: nowrap;
}

.gs-monitor-row:hover {
  background: rgba(255,255,255,0.04);
}

.gs-monitor-index {
  font-weight: 700;
  color:       var(--gs-text-secondary);
}

.gs-monitor-time {
  color: var(--gs-text-secondary);
}

.gs-monitor-message {
  overflow:      hidden;
  text-overflow: ellipsis;
  white-space:   nowrap;
}
```

HTML template:
```html
<div class="gs-monitor">
  @foreach (var entry in MonitorEntries)
  {
	<div class="gs-monitor-row">
	  <span class="gs-monitor-index">@entry.Index</span>
	  <span class="gs-monitor-time">@entry.Datetime.ToString("HH:mm:ss.fff")</span>
	  <span class="gs-monitor-message">@entry.Message</span>
	</div>
  }
</div>
```

### 5.9 Data Tables (TelescopeState)

For coordinate/telemetry display tables:

```css
.gs-table {
  width:           100%;
  border-collapse: collapse;
  font-size:       14px;
}

.gs-table th {
  color:           var(--gs-text-secondary);
  font-size:       12px;
  font-weight:     500;
  text-transform:  uppercase;
  letter-spacing:  0.08em;
  padding:         8px 12px;
  border-bottom:   1px solid var(--gs-divider);
  text-align:      left;
}

.gs-table td {
  color:           var(--gs-text-primary);
  padding:         8px 12px;
  border-bottom:   1px solid var(--gs-divider);
}

.gs-table td.gs-value {
  font-family:     var(--gs-font-mono);
  font-size:       13px;
}

.gs-table tbody tr:hover {
  background: var(--gs-bg-elevated);
}
```

### 5.10 Alerts

Override Bootstrap alerts for dark theme:

```css
.alert-success {
  background: rgba(102, 187, 106, 0.15);
  color:      var(--gs-success);
  border:     1px solid rgba(102, 187, 106, 0.3);
}

.alert-danger {
  background: rgba(244, 67, 54, 0.15);
  color:      var(--gs-red-300);
  border:     1px solid rgba(244, 67, 54, 0.3);
}

.alert-warning {
  background: rgba(255, 167, 38, 0.15);
  color:      var(--gs-warning);
  border:     1px solid rgba(255, 167, 38, 0.3);
}

.btn-close {
  filter: invert(1);   /* white × on dark alerts */
}
```

---

## 6. Icons

The WPF app used **Material Design Icons** via `MaterialDesignThemes.Wpf`. The Blazor app currently uses **Open Iconic** (`oi`). For the redesign:

- **Option A (recommended):** Replace Open Iconic with **Material Symbols** (Google's official web icon font). This directly mirrors the WPF icon set.
- **Option B:** Supplement with **Material Design Icons CDN** (`mdi` classes).

### Material Symbols import (add to `_Layout.cshtml`):
```html
<link rel="stylesheet"
	  href="https://fonts.googleapis.com/css2?family=Material+Symbols+Outlined:opsz,wght,FILL,GRAD@20..48,100..700,0..1,-50..200" />
```

### Icon usage:
```html
<!-- Instead of <span class="oi oi-home"> -->
<span class="material-symbols-outlined">home</span>
<span class="material-symbols-outlined">telescope</span>
<span class="material-symbols-outlined">settings</span>
<span class="material-symbols-outlined">monitor_heart</span>
<span class="material-symbols-outlined">device_hub</span>
```

### Key icon mapping

| WPF / context | Open Iconic (current) | Material Symbol (recommended) |
|---|---|---|
| Home | `oi-home` | `home` |
| Telescope | `oi-aperture` | `telescope` |
| Settings | — | `settings` |
| Monitor / log | — | `monitor_heart` |
| Connect | — | `link` |
| Disconnect | — | `link_off` |
| Slewing | — | `rotate_right` |
| Park | — | `stop_circle` |

---

## 7. Top Bar & Branding

The WPF app used a custom `WindowTitleBar` (30px, `WindowStyle="None"`, `AllowsTransparency="True"`). In Blazor, the top bar serves an equivalent anchoring role.

```css
/* MainLayout.razor.css */
.top-row {
  background-color: var(--gs-bg-paper);
  border-bottom:    1px solid var(--gs-divider);
  height:           3.5rem;
  justify-content:  flex-end;
  display:          flex;
  align-items:      center;
  position:         sticky;
  top:              0;
  z-index:          10;
}

.top-row ::deep a {
  color:         var(--gs-text-secondary);
  white-space:   nowrap;
  margin-left:   1.5rem;
  font-size:     13px;
  text-decoration: none;
}

.top-row ::deep a:hover {
  color: var(--gs-text-primary);
}
```

### App Brand / Title

The WPF title is `"GS Server"`. The Blazor brand should be:

```html
<a class="navbar-brand gs-brand" href="">
  <span class="gs-brand-prefix">GS</span>
  <span class="gs-brand-suffix">Alpaca</span>
</a>
```

```css
.gs-brand {
  display:     flex;
  align-items: center;
  gap:         2px;
  font-family: var(--gs-font-sans);
  font-weight: 300;
  font-size:   1.25rem;
  line-height: 1;
  text-decoration: none;
}

.gs-brand-prefix {
  font-weight: 700;
  color:       var(--gs-red-500);     /* Red accent on the "GS" */
  letter-spacing: 0.02em;
}

.gs-brand-suffix {
  color: var(--gs-text-primary);
}
```

---

## 8. Page-Level Patterns

### 8.1 Settings Page (tab-based — MountSettings.razor)

The WPF `DrawerHost` pattern maps to **Bootstrap tabs + off-canvas drawer** for the drawer sub-forms. Standard tab layout rules:

- Tab bar is flush to top of content, no card wrapper around it.
- Tab panels have `var(--gs-bg-paper)` surface with `var(--gs-space-4)` padding.
- Section headings within a tab use `h4` at 16px/500.
- Row layout: `row` → `col-md-6` for two-column settings.

### 8.2 Telescope State Page

- Data displayed as a **two-column definition list** or `gs-table`.
- All coordinate values in `gs-monospace`.
- Status badge prominently placed in page header area.
- Live/updating values should use a subtle CSS pulse animation:

```css
@keyframes gs-live-pulse {
  0%   { opacity: 1; }
  50%  { opacity: 0.6; }
  100% { opacity: 1; }
}

.gs-live {
  animation: gs-live-pulse 2s ease-in-out infinite;
  color: var(--gs-success);
}
```

### 8.3 Monitor Settings Page

- Full-width `gs-monitor` block below filter controls.
- Filter checkboxes (Device / Category) above the log.
- Monochrome log on black background (as WPF).

---

## 9. Responsive Breakpoints

| Breakpoint | Width | Behaviour |
|---|---|---|
| Mobile | < 641px | Sidebar collapses to hamburger; top-row hidden |
| Tablet | 641px–992px | Sidebar visible; single-column settings |
| Desktop | > 992px | Full two-column settings layout |

These match the existing `NavMenu.razor.css` breakpoint at `641px` — retain it.

---

## 10. Implementation Checklist

When implementing this style guide in the Blazor app:

- [ ] **Step 1** — Add Google Fonts Roboto + Material Symbols links to `_Layout.cshtml`
- [ ] **Step 2** — Add all CSS custom properties (Section 2) to top of `site.css`
- [ ] **Step 3** — Update `html, body` base styles in `site.css`
- [ ] **Step 4** — Add component classes (`.gs-card`, `.gs-badge`, `.gs-monitor`, `.gs-table`, `.gs-monospace`) to `site.css`
- [ ] **Step 5** — Update `MainLayout.razor.css` top-row to use tokens
- [ ] **Step 6** — Update `NavMenu.razor.css` to use tokens and add `.gs-brand` styles
- [ ] **Step 7** — Update `MountSettings.razor` tab styles to use new nav-tabs overrides
- [ ] **Step 8** — Replace fieldset legend colour from `#002157` to `var(--gs-red-500)`
- [ ] **Step 9** — Apply `gs-badge` to status indicators in `TelescopeState.razor`
- [ ] **Step 10** — Replace `oi` icon classes with `material-symbols-outlined` across Razor files

---

## 11. Source References

| Source | Path |
|---|---|
| WPF App theme config | `T:\source\repos\Principia4834\GSServer\GS.Server\App.xaml` |
| WPF Main window layout | `T:\source\repos\Principia4834\GSServer\GS.Server\Main\MainWindow.xaml` |
| WPF SkyTelescope view | `T:\source\repos\Principia4834\GSServer\GS.Server\SkyTelescope\SkyTelescopeV.xaml` |
| WPF Settings view | `T:\source\repos\Principia4834\GSServer\GS.Server\Settings\SettingsV.xaml` |
| Blazor current CSS | `GreenSwamp.Alpaca.Server\wwwroot\css\site.css` |
| Blazor layout CSS | `GreenSwamp.Alpaca.Server\Shared\MainLayout.razor.css` |
| Blazor nav CSS | `GreenSwamp.Alpaca.Server\Shared\NavMenu.razor.css` |
| Blazor settings page | `GreenSwamp.Alpaca.Server\Pages\MountSettings.razor` |

---

*End of style guide. Review offline before commencing implementation.*
