# UniPlaySong Settings Style Kit

A drop-in WPF style set — dark slate palette, a panel-switch toggle, section headings and a
two-level navigation rail. Copy what you want; it has no dependency on UniPlaySong.

Built for Playnite plugin settings windows, but nothing here is Playnite-specific. It is plain
WPF: `ResourceDictionary`, `ControlTemplate`, one small attached property.

**Licence:** MIT, same as UniPlaySong. Attribution appreciated, not required.
**Internal companion:** [SETTINGS_DESIGN.md](SETTINGS_DESIGN.md) explains *why* each choice was
made and how to change it. This document is the *what*, for lifting.

---

## Quick start

1. Copy `SettingsResources.xaml` (and `TrackedCaps.cs`, if you want tracked-caps headings) into
   your project.
2. Merge the dictionary into any page that uses it:

```xml
<UserControl.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="pack://application:,,,/YourAssembly;component/Path/SettingsResources.xaml"/>
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</UserControl.Resources>
```

3. Build a page from the styles:

```xml
<ScrollViewer Style="{StaticResource UpsPage}">
    <StackPanel MaxWidth="820" HorizontalAlignment="Left">

        <TextBlock Text="Common Events" Style="{StaticResource UpsPageTitle}"/>
        <TextBlock Style="{StaticResource UpsPageSubtitle}" Text="What this page is for."/>

        <Border Style="{StaticResource UpsSectionBar}">
            <TextBlock ups:TrackedCaps.Text="Games" Style="{StaticResource UpsSectionTitle}"/>
        </Border>

        <CheckBox Content="Pause on game launch"
                  Style="{StaticResource UpsToggle}"
                  IsChecked="{Binding Settings.PauseOnGameStart}"/>
        <TextBlock Style="{StaticResource UpsHint}" Text="What the setting actually does."/>

        <CheckBox Content="A sub-option of the above"
                  Style="{StaticResource UpsToggleChild}"
                  IsChecked="{Binding Settings.SomeChild}"
                  IsEnabled="{Binding Settings.PauseOnGameStart}"/>
        <TextBlock Style="{StaticResource UpsHintIndented}" Text="Indented to match."/>

    </StackPanel>
</ScrollViewer>
```

---

## The one rule you cannot skip

> **If a page paints its own background, every text element must set its own `Foreground`.**

In WPF an *inherited* text colour **loses** to the host theme's implicit `TextBlock` style, because
a style setter outranks inheritance. Setting `TextElement.Foreground` on the page root is **not
enough** — a Playnite theme (or any host theme) that styles `TextBlock` will override it, and you
get theme-coloured text on your background. On a dark ground with a light host theme, that is
black on near-black.

We shipped this bug. Profile names with no `Foreground` rendered near-invisible while the caption
directly beneath them, which set an explicit colour, always looked fine.

Every `Ups*` text style below sets `Foreground` explicitly. **Compose from the styles and you
cannot hit it.** Write a bare `<TextBlock Text="..."/>` on a painted page and you can.

---

## Palette

```xml
<SolidColorBrush x:Key="UpsGround"     Color="#12171E"/>  <!-- page background -->
<SolidColorBrush x:Key="UpsSurface"    Color="#1A222C"/>  <!-- cards, page strip -->
<SolidColorBrush x:Key="UpsSurfaceHi"  Color="#232D3A"/>  <!-- raised / selected -->
<SolidColorBrush x:Key="UpsStroke"     Color="#31404F"/>
<SolidColorBrush x:Key="UpsStrokeHi"   Color="#4A5F73"/>
<SolidColorBrush x:Key="UpsAccent"     Color="#4CC2FF"/>  <!-- section bars, values -->
<SolidColorBrush x:Key="UpsText"       Color="#E4E9EF"/>
<SolidColorBrush x:Key="UpsTextMuted"  Color="#93A1B0"/>
<SolidColorBrush x:Key="UpsTextFaint"  Color="#6B7A88"/>
<SolidColorBrush x:Key="UpsWarn"       Color="#C8A24B"/>
<SolidColorBrush x:Key="UpsDanger"     Color="#E08070"/>
```

Slate biased slightly blue, so panels sit apart from neutral host chrome instead of tinting it.
Retint by changing these eleven values; everything else references them.

If you change the accent, check it against your toggle's on-colour. Ours is cyan precisely so the
two agree — and to avoid amber, which would have collided with `UpsWarn` and made "on" borrow the
colour that means "caution".

---

## Type scale

`16 / 10 caps / 12.5 / 10`

| Style | Size | Weight | Colour |
|---|---|---|---|
| `UpsPageTitle` | 16 | Bold | `UpsText` |
| `UpsPageSubtitle` | 11.5 italic | — | `UpsTextMuted` |
| `UpsSectionTitle` | 10 tracked caps | SemiBold | `UpsTextMuted` |
| `UpsToggle` label | 12.5 | SemiBold | `UpsText` |
| `UpsBody` | 12 | — | `UpsText` |
| `UpsHint` | 10 | — | `UpsTextMuted` |
| `UpsWarning` / `UpsDangerNote` | 10 | SemiBold | `UpsWarn` / `UpsDanger` |

**If you take one idea from this section:** headings differentiate by *treatment*, not size. Our
first attempt stepped `18 / 13.5 / 13`, and 13.5 → 13 is a **4% step** — not a hierarchy level, so
headings and setting labels read as one tier and the page looked like undifferentiated rows.
Making the heading small, dimmed and tracked-uppercase leaves the setting label as the largest,
brightest thing in the body, which is correct: the label is what people scan.

### Tracked caps in WPF

`TextBlock` has **neither `text-transform` nor `letter-spacing`**. `TrackedCaps` is a ~40-line
attached property that rewrites the string with a hair space (U+200A) between characters:

```xml
<TextBlock ups:TrackedCaps.Text="Window Focus" Style="{StaticResource UpsSectionTitle}"/>
```

Set `TrackedCaps.Text`, not `Text`. The readable string stays in your markup, so pages remain
greppable and translators see ordinary words.

One detail worth keeping if you reimplement it: **skip the spacer where it would straddle a real
word gap.** Padding both sides of an existing space opens a chasm and "WINDOW FOCUS" reads as two
separate headings.

Real tracking needs `Glyphs` with explicit `Indices`, the font URI and per-character advance
widths. At 10px the hair space is within a fraction of a pixel.

---

## The toggle switch

A panel-mounted rocker. **24 × 13** track, **12 × 10** square cap, sized deliberately *under* the
12.5px label's line box so the switch reads as subordinate to its setting.

### Anatomy

A `Border` takes one child, so the surfaces are siblings in a `Grid` and stack in document order:

| Layer | What it does |
|---|---|
| `Track` | Recessed groove. Gradient dark at the lip, so it reads as casting a shadow into the channel |
| `InsetLip` | Hard, **zero-blur** band under the top edge. WPF has no inset shadow; this is what the CSS `inset 0 6px 0 0` idiom draws anyway |
| `Lip` | Lit hairline along the top, above the inset band, so the groove reads as an edge not a gradient |
| `Thumb` | The cap: moulded bevel (one lit pixel row, one shaded), gloss over its upper 45% |
| `Led` | Indicator sunk **into** the cap |

### Three rules that make it work at this size

**No outward glow, ever.** A settings page carries a dozen or more toggles, and a dozen soft halos
is a smear. On reads from the fill and the cap's position, both hard-edged. It is also a dozen
fewer blur passes per page. The indicator inside the cap is where light is allowed to read as
light, because it is contained.

**Depth is hard-edged.** Zero-blur bands and one-pixel bevels, not blurred shadows. It is the only
depth technique that survives at 10px — soft shadows turn to grey haze — and it renders cheaper.
This is the single most transferable idea in the kit.

**The cap throws, it does not glide.** Do not animate the slide. A trigger animation only runs on
the transition *into* checked, so any toggle already on when the window opens sits at the wrong end
of its track and then visibly jumps. Swap `HorizontalAlignment` instead: correct the instant it
renders, and a panel switch snaps anyway.

Put `SnapsToDevicePixels="True"` and `UseLayoutRounding="True"` on the track and cap. At this size
a half-pixel rim is the difference between crisp and smudged.

### A second colourway without a second template

`UpsToggle` reads its **on** colours from its own `Background`, `BorderBrush` and `Tag`. The *off*
groove is fixed, so those three properties are free to carry "on" state:

```xml
<Style x:Key="UpsToggle" TargetType="CheckBox">
    <Setter Property="Background"  Value="{StaticResource UpsGrooveOn}"/>
    <Setter Property="BorderBrush" Value="{StaticResource UpsOnRim}"/>
    <Setter Property="Tag"         Value="{StaticResource UpsLedOn}"/>
    ...
</Style>

<!-- nested sub-option: same template, different lamp -->
<Style x:Key="UpsToggleChild" TargetType="CheckBox" BasedOn="{StaticResource UpsToggle}">
    <Setter Property="Margin"      Value="24,7,0,0"/>
    <Setter Property="Background"  Value="{StaticResource UpsGrooveOnSub}"/>
    <Setter Property="BorderBrush" Value="{StaticResource UpsOnRimSub}"/>
    <Setter Property="Tag"         Value="{StaticResource UpsLedOnSub}"/>
</Style>
```

Inside the checked trigger, read them back with a `RelativeSource TemplatedParent` binding —
`TemplateBinding` is not reliable in trigger setters:

```xml
<Setter TargetName="Track" Property="Background"
        Value="{Binding Background, RelativeSource={RelativeSource TemplatedParent}}"/>
```

A third colourway is three lines.

### Hover

Move **three** surfaces together — cap face, cap rim, top-lip hairline. Any one alone is too quiet
at 24 × 13.

Leave the track's fill alone. It carries on/off, so a hover that recoloured it would read as *the
setting having changed* under the cursor. Hover says "you are pointing at this", never "this is now
on".

Put the hover trigger **after** the checked trigger — in WPF the last matching trigger wins for a
given property, so hover then still applies on a lit switch.

### Keyed, never implicit

`UpsToggle` is a keyed style, and this is deliberate. An implicit `CheckBox` style replaces whatever
the **host theme** sets for every checkbox in the window, including ones outside your pages. Keyed
means opt-in, so the blast radius is exactly what asks for it.

Same reasoning for the nav strip: an implicit `TabControl` style cascades into your pages and
restyles any `TabControl` added there later.

---

## Sections

Two kinds, and the difference must be visible without clicking:

```xml
<!-- fixed: no chevron -->
<Border Style="{StaticResource UpsSectionBar}">
    <TextBlock ups:TrackedCaps.Text="Games" Style="{StaticResource UpsSectionTitle}"/>
</Border>

<!-- collapsible: same look, plus a chevron -->
<Expander Style="{StaticResource UpsSection}">
    <Expander.Header>
        <TextBlock ups:TrackedCaps.Text="System" Style="{StaticResource UpsSectionTitle}"/>
    </Expander.Header>
    <StackPanel> ... </StackPanel>
</Expander>
```

**The chevron's presence is the affordance.** Do not give a fixed section a chevron, and do not
hide the chevron on a collapsible one.

Not everything should collapse. A section worth folding is one you configure once; the section
carrying the setting people opened the page for — and any warning that most needs seeing — stays
fixed.

Two traps in the `Expander` template, both of which **build and run perfectly happily**:

- The inner `ToggleButton` needs its own `Template`. Without one it picks up the host theme's
  button chrome and your heading renders as a raised grey button across the page.
- It also needs `Content="{TemplateBinding Header}"`. Without it there is nothing to present and
  the heading renders as a bare bar and chevron with **no text**.

---

## Two-level navigation rail

Left rail of groups, top strip of pages within the selected group. Both are `TabControl`s with a
custom `ControlTemplate`; the outer one lays out as a `Grid` with the items host in a vertical
`StackPanel`.

```xml
<TabControl Style="{StaticResource NavRail}">
    <TabItem Header="Pauses">
        <TabControl Style="{StaticResource NavStrip}" Tag="Pauses">
            <TabItem Header="Common Events"><local:CommonEventsPage/></TabItem>
            <TabItem Header="External Audio"><local:ExternalAudioPage/></TabItem>
        </TabControl>
    </TabItem>
</TabControl>
```

Worth copying: **make every rail entry a group, even one holding a single page.** Those are the
entries most likely to grow, and a group that already exists gains a sibling by adding one
`TabItem` rather than being restructured.

The strip also hosts a per-group action button (ours is Reset), taking the group name from the
inner `TabControl`'s `Tag`. Leave `Tag` unset and the button hides — useful for informational
groups with nothing to act on.

Selection *is* retained per group: `TabItem.Content` survives being unloaded, so the inner
`SelectedIndex` persists across rail switches.

---

## Verifying it, without launching the host app

`ControlTemplate` and `StaticResource` faults are **runtime** errors, not build errors. A green
build tells you nothing about whether your settings window opens.

Render a page headlessly instead — `Assembly.LoadFrom` the built DLL, `CreateInstance` the page
type, then Measure / Arrange / `RenderTargetBitmap` to a PNG. Give it a `DataContext` exposing your
settings object and the bound state renders too.

Three real bugs in this kit's development were caught only that way, and every one of them built
cleanly: the cap not moving when checked, section headings rendering with no text, and text
rendering black-on-black.

---

## WPF constraints, and what to do instead

| You want | WPF reality | Do this |
|---|---|---|
| `letter-spacing` / `text-transform` | Neither exists on `TextBlock` | Attached property that rewrites the string |
| `box-shadow: inset` | No inset shadow | Solid band pinned to the top edge |
| `repeating-linear-gradient` | Not a brush type | Tiled `DrawingBrush` |
| Several layers in a `Border` | One child only | Siblings in a `Grid` |
| Two shadows on one element | One `Effect` per element | Pick one, or fake with a sibling |
| Inherited text colour to survive | Loses to the host theme's implicit style | Explicit `Foreground` everywhere |
| `TemplateBinding` in a trigger setter | Unreliable | `{Binding X, RelativeSource={RelativeSource TemplatedParent}}` |
| `DisplayMemberPath` on a **restyled** ComboBox | Templates the dropdown items only; the closed box reads `ToString()` | Set `ItemTemplate` instead |

### The DisplayMemberPath trap

If you retemplate `ComboBox`, **stop using `DisplayMemberPath`.** It templates the dropdown *items*
only. The closed selection box renders from `SelectionBoxItemTemplate`, which WPF leaves **null**
when only `DisplayMemberPath` is set — so the `ContentPresenter` falls back to `ToString()` and your
box reads `MyApp.Models.Thing`.

The stock template hides this, so it surfaces the moment you restyle and looks like your template's
fault. Setting `ItemTemplate` populates `SelectionBoxItemTemplate` from it and fixes both:

```xml
<DataTemplate x:Key="ThingTemplate">
    <TextBlock Text="{Binding DisplayName}" TextTrimming="CharacterEllipsis"/>
</DataTemplate>

<ComboBox ItemTemplate="{StaticResource ThingTemplate}" SelectedValuePath="Id" .../>
```
| A gradient highlight of fixed height | Offsets are a **proportion** of the element | `MappingMode="Absolute"` + `EndPoint` in device units |
| Backdrop blur / acrylic | `BlurEffect` blurs the element, not what is behind it | Translucent fill over a ground you paint yourself, plus a lit edge |

### Two traps worth copying the fix for

**Gradient offsets scale with the element.** A "3% sheen along the top" renders 10px tall on a
200px card and 3px on a 60px one, and a column of them looks ragged. Set `MappingMode="Absolute"`
with `EndPoint` in device units. Better still, put the highlight in a **gradient border** — a
border is one pixel by construction and cannot develop the problem:

```xml
<LinearGradientBrush x:Key="Bevel" StartPoint="0,0" EndPoint="0,1">
    <GradientStop Offset="0"    Color="#63768A"/>  <!-- lit top edge -->
    <GradientStop Offset="0.35" Color="#3D4B5B"/>
    <GradientStop Offset="1"    Color="#222A34"/>  <!-- shadowed bottom -->
</LinearGradientBrush>
```

**Translucency needs a backdrop you own.** Panels at 7–14% white over a *host theme's* background
have no colour of their own — every one comes out the same washed grey, whatever hue you intended.
The same technique over a ground your page paints is entirely predictable. Own the backdrop first,
then glaze.
