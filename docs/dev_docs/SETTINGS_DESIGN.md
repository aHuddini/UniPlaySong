# Settings Window — Design System

Everything the settings window looks like lives in one file:
`src/Controls/Settings/SettingsResources.xaml`. Change a token there and every page follows.
This document explains what the choices are and *why*, so a future change is a decision rather
than a guess.

Companion tool: the **Toggle Bench**, a browser stand-in for a settings page that emits
paste-ready XAML. WPF device-independent pixels are CSS pixels at 96 dpi, so a size tuned in the
browser is the number that goes in the XAML. See [Changing the style](#changing-the-style).

---

## 1. Structure

Three levels, each in its own place:

| Level | Lives in | What it decides |
|---|---|---|
| Shell | `src/UniPlaySongSettingsView.xaml` | Which rail groups exist, which pages sit in each |
| Page | `src/Controls/Settings/<Name>Page.xaml` | One page's content |
| Look | `src/Controls/Settings/SettingsResources.xaml` | Every colour, size and control template |

Navigation is two-level: a **left rail of groups**, and a **top strip of pages** within the
selected group.

**Every rail entry is a group holding a `TabControl`, including ones with a single page.** This is
deliberate — those are the entries most likely to grow, and a group that already exists gains a
sibling by adding one `TabItem` rather than being restructured. Do not collapse single-page groups
back down to a bare page.

The strip also carries the group's **Reset** button, taking the group name from the inner
`TabControl`'s `Tag`. A group with no `Tag` (About, Quick Start) shows no button, which is correct
for groups that own no resettable settings.

---

## 2. Palette

Slate, biased slightly blue so panels sit apart from Playnite's neutral chrome rather than tinting
it. Taken from the Quick Start tab rather than invented beside it — two slate palettes in one
window drift apart on the first tweak.

| Token | Value | Use |
|---|---|---|
| `UpsGround` | `#12171E` | Page background |
| `UpsSurface` | `#1A222C` | Cards, the page strip |
| `UpsSurfaceHi` | `#232D3A` | Raised / selected |
| `UpsStroke` | `#31404F` | Borders |
| `UpsStrokeHi` | `#4A5F73` | Hover borders |
| `UpsAccent` | `#4CC2FF` | Section bars, values, focus |
| `UpsText` | `#E4E9EF` | Primary text |
| `UpsTextMuted` | `#93A1B0` | Hints, section headings |
| `UpsTextFaint` | `#6B7A88` | Chevrons, captions |
| `UpsWarn` | `#C8A24B` | Caution notes |
| `UpsDanger` | `#E08070` | "This will break things" notes |

### The rule that governs all of it

> **If a page paints its own background, every text element must set its own `Foreground`.**

Not belt-and-braces. In WPF an *inherited* text colour **loses** to the host theme's implicit
`TextBlock` style, because a style setter outranks inheritance in the precedence chain. Setting
`TextElement.Foreground` on a page root is therefore **not enough**. Only a `Foreground` on the
element itself wins.

Quick Start shipped this bug: its profile names had no `Foreground` and rendered near-black on the
dark ground, while the `Summary` line directly beneath them set `#9AA0A6` and always looked fine.
That asymmetry is the whole proof.

Every `Ups*` text style sets `Foreground` explicitly, so composing from the styles makes the
mistake impossible. Author pages that way.

---

## 3. Type scale

`16 / 10 caps / 12.5 / 10`.

| Style | Size | Weight | Colour |
|---|---|---|---|
| `UpsPageTitle` | 16 | Bold | `UpsText` |
| `UpsPageSubtitle` | 11.5 italic | — | `UpsTextMuted` |
| `UpsSectionTitle` | 10 tracked caps | SemiBold | `UpsTextMuted` |
| `UpsToggle` label | 12.5 | **SemiBold** | `UpsText` |
| `UpsHint` | 10 | — | `UpsTextMuted` |

The previous scale was `18 / 13.5 / 13 / 11`, and **13.5 → 13 is a 4% step**. Four percent is not
a hierarchy level: section headings and setting labels landed on one tier and the page read as
undifferentiated rows.

The fix differentiates headings by **treatment rather than size** — small, dimmed, tracked
uppercase, with the accent bar doing the announcing. That leaves the setting label as the largest,
brightest thing in the body, which is right: the label is what you scan. The label is SemiBold so
it separates from its hint by weight and not only by colour.

### Tracked caps

WPF's `TextBlock` has **neither `text-transform` nor `letter-spacing`**. `TrackedCaps` (an attached
property in `src/Controls/Settings/TrackedCaps.cs`) supplies both by rewriting the string with a
hair space between letters — skipping any spacer that would straddle a real word gap, since
padding both sides of an existing space opens a chasm and "WINDOW FOCUS" reads as two headings.

```xml
<TextBlock ups:TrackedCaps.Text="Window Focus" Style="{StaticResource UpsSectionTitle}"/>
```

Set `TrackedCaps.Text`, never `Text` — the readable string stays in the markup, so pages remain
greppable and a translator sees ordinary words. True tracking would need `Glyphs` with explicit
`Indices`, the font URI and per-character advance widths; at 10px the hair space lands within a
fraction of a pixel of that.

---

## 4. The toggle switch

A panel-mounted rocker, not a flat pill. **24 × 13** track, **12 × 10** square cap.

Sized deliberately *under* the 12.5px label's line box, so the switch reads as subordinate to the
setting it belongs to rather than towering over it.

### Anatomy

A `Border` takes one child, so the surfaces are siblings in a `Grid` and stack in document order:

1. **Track** — recessed groove. Gradient runs dark at the lip so it reads as casting a shadow into
   the channel.
2. **InsetLip** — a hard, zero-blur band pinned under the top edge. WPF has no inset shadow, so
   this is a solid band, which is all the CSS `inset 0 6px 0 0` idiom draws anyway.
3. **Lip** — lit hairline along the top, above the inset band, so the groove reads as an edge
   rather than a gradient.
4. **Thumb** — the cap, carrying a moulded bevel (one lit pixel row, one shaded), a gloss over its
   upper 45%, and the indicator.
5. **Led** — the indicator, sunk *into* the cap.

### Three rules it enforces

**No outward bloom, ever.** A page carries a dozen or more toggles, and a dozen soft halos is a
smear. On reads from the fill and the cap's position, both hard-edged. It is also a dozen fewer
blur passes per page. The indicator in the cap is where light is allowed to read as light, because
it is contained.

**Depth is hard-edged.** Zero-blur bands and one-pixel bevels, not blurred shadows. It is the only
depth technique that survives at 10px, where soft shadows turn to grey haze — and it is cheaper to
render.

**The cap throws, it does not glide.** A slide animation only runs on the transition *into*
checked, so any toggle already on when the window opened sat at the wrong end of its track and then
jumped. An alignment swap is correct the instant it renders, and a panel switch snaps anyway.

`SnapsToDevicePixels` and `UseLayoutRounding` are on the track and cap: at this size a half-pixel
rim is the difference between a crisp control and a smudged one.

### Colourways

| State | Groove | Rim | Indicator |
|---|---|---|---|
| Off | `UpsGrooveOff` | `#0B1015` | `#6B7885` @ 0.45 |
| On (primary) | `UpsGrooveOn` — cyan | `UpsOnRim` | `UpsLedOn` |
| On (sub-option) | `UpsGrooveOnSub` — deep blue | `UpsOnRimSub` | `UpsLedOnSub` |

Cyan agrees with `UpsAccent`, so switches and section bars sing the same note. It also avoids an
amber on-state, which would have collided with `UpsWarn` — "on" borrowing the colour that means
"caution".

Sub-options are deep blue: beside the cyan parent without leaving the cool family, and a stop or
two quieter, which keeps a sub-option from looking like a second kind of primary.

**How a second colourway exists without a second template:** `UpsToggle` reads its **on** colours
from its own `Background`, `BorderBrush` and `Tag`. The *off* groove is fixed, so those three
properties are free to carry "on" state, and `UpsToggleChild` overrides just those three. A third
colourway is three lines.

### Hover

Three surfaces move together — cap face, cap rim, and the top-lip hairline — because any one alone
is too quiet at 24 × 13.

**The track's fill is deliberately untouched.** It carries on/off, so a hover that recoloured it
would read as *the setting having changed* under the cursor. Hover says "you are pointing at this",
never "this is now on".

The hover trigger sits **after** the checked trigger, so it still wins on a lit switch — in WPF the
last matching trigger wins for a given property.

### Keyed, never implicit

`UpsToggle` is a keyed style. An implicit `CheckBox` style would replace whatever the Playnite
theme sets for **every** checkbox in the window, including ones outside these pages. Keyed means
opt-in, so the blast radius is exactly the pages that ask for it. The same reasoning applies to
`NavStrip`: an implicit `TabControl` style would cascade into pages and restyle any `TabControl`
added there later.

---

## 5. Sections

Two kinds, and the difference is visible without clicking:

- `UpsSectionBar` — fixed. No chevron.
- `UpsSection` — an `Expander` styled identically, **plus a chevron**. The chevron's presence *is*
  the affordance.

Expanded by default. Pages are short enough after the split that opening to a wall of closed
headings would cost a click per section for nothing; set `IsExpanded="False"` on anything genuinely
secondary.

**Not everything should collapse.** A section worth folding is one you configure once. The section
carrying the setting people opened the page for — and any warning that most needs seeing — stays
fixed. On Common Events, *Games* is fixed while *System* and *Window Focus* fold.

The `ToggleButton` inside the Expander template carries its own template; without one it picks up
the host theme's button chrome and the heading renders as a raised grey button. It must also be
handed `Content="{TemplateBinding Header}"` — without that it has nothing to present and the
heading renders as a bare bar and chevron, which builds and runs perfectly happily.

---

## 6. Reset

Per left-rail group, and **no code writes a default value down**.

`SettingsResetService.ResetProperties` copies from a pristine `UniPlaySongSettings`, so the
backing-field initialisers stay the single source of truth. `SettingsGroups.Map` holds the
property/group partition; `SettingsResetCoverageTests` proves it covers every setting exactly once,
failing **by name** if anything is unfiled.

This was a correctness problem, not just ergonomics. The per-tab handlers it replaced hand-wrote
228 assignments covering 193 settings and **silently missed 71** — nothing failed, they simply
could not be reset. They had also drifted from the real defaults in three places.

**When adding a setting:** add the property, then file it in `SettingsGroups.Map` (or `NeverReset`
for machine-specific paths and live runtime state). The test tells you if you forget.

---

## 7. Changing the style

### Tune it in the browser first

The Toggle Bench reproduces a settings page 1:1 and emits paste-ready XAML — geometry, gradients,
the template body, the checked-trigger setters, and the recomputed hint indents. Four independent
axes compose: **shape**, **on colour**, **nested colour**, **type scale**.

Its test (`test-bench.js`, run under Node) does two things:

1. Executes the page's real script against a stub DOM. A syntax check passes on a call to a
   function that no longer exists; executing it does not.
2. **Reads `SettingsResources.xaml` and compares 20 values against the bench's "Shipped" preset.**

The second matters because the bench's whole premise is that "Shipped" *is* the `.pext`, and
nothing enforced that — it drifted twice, and on its first run the check caught an off-groove
colour mismatch nobody had noticed. A reference tool nobody verifies stays plausible while quietly
lying.

### Verify in WPF without launching Playnite

`ControlTemplate` and `StaticResource` faults are **runtime**, not build errors, so a green build
proves nothing about them. Render a page headlessly instead: `Assembly.LoadFrom` the built DLL,
`CreateInstance` the page type, then Measure / Arrange / `RenderTargetBitmap`. Give it a
`DataContext` with a `Settings` property holding a real `UniPlaySongSettings` and the bound state
renders too.

Three real bugs this session were caught only this way, all of which built and ran cleanly: the cap
not moving when checked, section headings rendering with no text, and Quick Start's black profile
names.

### Hint indents track the switch

`UpsHint` is `track width + label gap` (32), `UpsHintIndented` adds 24 (56). Change the track width
and both must follow, or hints stop aligning under their labels. The bench recomputes them in its
output.

### Encoding

`SettingsResources.xaml` is UTF-8 **with BOM**. When rewriting it from PowerShell, decode the bytes
and `TrimStart([char]0xFEFF)` before writing with a BOM — otherwise you get two, and the XAML fails
with the deeply unhelpful `Data at the root level is invalid. Line 1, position 1`.

---

## 8. WPF constraints worth knowing

Collected because each cost time to discover:

| Want | WPF reality | What we do |
|---|---|---|
| `letter-spacing` / `text-transform` | Neither exists on `TextBlock` | `TrackedCaps` attached property |
| `box-shadow: inset` | No inset shadow | Solid band pinned to the top edge |
| `repeating-linear-gradient` | Not a brush type | Tiled `DrawingBrush` |
| Multiple layers in a `Border` | One child only | Siblings in a `Grid` |
| An inherited text colour | Loses to the theme's implicit style | Explicit `Foreground` everywhere |
| Two shadows on one element | One `Effect` per element | Pick one, or fake with a sibling |
| A gradient highlight of fixed height | Offsets are a **proportion** of the element | `MappingMode="Absolute"` + `EndPoint` in device units |

### The gradient-scaling trap

Gradient stop offsets default to a proportion of the element, so a "3% sheen at the top" is 10px on
a 200px card and 3px on a 60px one — a column of cards comes out visibly ragged. Set
`MappingMode="Absolute"` and put `EndPoint` in device units; `SpreadMethod` (Pad, the default)
carries the last stop down the remainder.

A **gradient border** sidesteps it entirely, which is why the cards use one for their bevel: a
border is one pixel by construction, so it cannot scale with its content.
