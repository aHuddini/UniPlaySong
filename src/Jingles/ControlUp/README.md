# ControlUp event sounds

The bundled sound played when ControlUp reports a controller detected via
`playnite://uniplaysong/controlup/detecttrigger`.

**Shipped:** `PIXABAY - FREESOUND COMMUNITY - Coin Pickup.mp3` (Pixabay Content License).

The ControlUp Events section offers exactly two choices — this sound, or a custom file —
so there is no preset picker. The settings default
(`BundledJingleService.DefaultControlUpJingle`) names this file, and the manifest entry in
`../jingles.json` carries `"category": "controlup"`, which is what keeps it out of the
celebration / abandoned / achievement pickers and keeps theirs out of this one.

## Replacing the bundled sound

1. Drop the new file here and delete the old one.
2. Update its entry in `../jingles.json` (keep `"category": "controlup"`).
3. Update `DefaultControlUpJingle` in `src/Services/BundledJingleService.cs` and the
   display label in the ControlUp Events section of `UniPlaySongSettingsView.xaml`.

`ControlUpBundledSoundTests` fails if these fall out of sync — the filename lives in three
places, and a mismatch silences the feature without logging anything.

## Adding a second ControlUp event

The URI is namespaced `controlup/{event}`, so a new event (disconnect, battery-low) adds a
case in `ExternalControlService.HandleControlUp` and its own settings triple. If several
sounds ever ship for one event, `BundledJingleService.GetControlUpJingles()` already returns
the whole category and can back a picker again.
