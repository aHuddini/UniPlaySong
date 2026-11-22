using System.Windows.Media;

namespace UniPlaySong.DeskMediaControl
{
    // The Now Playing card's look: art blur and colour, tint, veil, vignette, rim, glass. Values from the banner tuner.
    // Three are used (see Pick); the others are kept, unused, as design material for the pop-out player.
    internal sealed class BannerLook
    {
        public string Name;
        public double HeavyBlur, SoftBlur;                 // px on the 290-wide card: the blurred layer, the "sharp" layer
        public double ArtOpacity, SharpOpacity;
        public byte MaskMidAlpha; public double MaskMidOffset, MaskEndOffset; // the sharp layer's fade-out
        public double Saturation, Contrast, Brightness;    // applied to the art after the blur
        public double TintShade; public byte VividAlpha, DarkAlpha;
        public Color Veil;
        public double VignetteClear, VignetteRadiusX, VignetteRadiusY, VignetteCentreY;
        public Color VignetteMid, VignetteEdge;
        public Color RimTopLeft;
        public Color RimMid = Color.FromArgb(0x05, 0xFF, 0xFF, 0xFF), RimEnd = Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF);
        public bool WholeCard;                              // the backdrop fills the card, not just the banner
        public double TintValue, TintDarkValue;             // > 0: tint colours set to this brightness, keeping their hue
        public Color Frost;                                 // milky layer over the backdrop
        public double Grain;                                // noise opacity
        public uint Glass = 0x992C201C;                     // the window blur's own tint, AABBGGRR
        public Color BannerShade;                           // the banner alone, over the backdrop
        public Color ListShade;                             // the list alone (whole-card looks)
        public byte BannerTintAlpha;                        // > 0: the banner shaded with the art's two colours instead
        public double BannerTintValue, BannerTintDarkValue; // their brightness, hue kept
        public double BannerTintDarkSaturation = 1;         // the dark colour's saturation lift (dark colours read greyer)

        public static readonly BannerLook[] All =
        {
            // The first progressive blur, before the tuner: lightly blurred art under the art itself, no colour change
            new BannerLook
            {
                Name = "Original", HeavyBlur = 4.5, SoftBlur = 0, ArtOpacity = 0.5, SharpOpacity = 0.7,
                MaskMidAlpha = 0x66, MaskMidOffset = 0.4, MaskEndOffset = 0.75,
                Saturation = 1, Contrast = 1, Brightness = 1,
                TintShade = 0.65, VividAlpha = 0x59, DarkAlpha = 0x80,
                Veil = Color.FromArgb(0x55, 0, 0, 0),
                VignetteClear = 0.3, VignetteRadiusX = 0.7, VignetteRadiusY = 0.95, VignetteCentreY = 0.45,
                VignetteMid = Color.FromArgb(0x59, 0, 0, 0), VignetteEdge = Color.FromArgb(0x99, 0, 0, 0),
                RimTopLeft = Color.FromArgb(0x5C, 0xFF, 0xFF, 0xFF), RimMid = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
            },
            new BannerLook
            {
                Name = "A", HeavyBlur = 16, SoftBlur = 10, ArtOpacity = 0.58, SharpOpacity = 0.64,
                MaskMidAlpha = 0x78, MaskMidOffset = 0.39, MaskEndOffset = 0.65,
                Saturation = 1.10, Contrast = 1.17, Brightness = 0.86,
                TintShade = 0.61, VividAlpha = 0x45, DarkAlpha = 0x61,
                Veil = Color.FromArgb(0x2B, 0, 0, 0),
                VignetteClear = 0.33, VignetteRadiusX = 0.77, VignetteRadiusY = 0.83, VignetteCentreY = 0.45,
                VignetteMid = Color.FromArgb(0x4F, 0, 0, 0), VignetteEdge = Color.FromArgb(0x6B, 0, 0, 0),
                RimTopLeft = Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF),
            },
            new BannerLook
            {
                Name = "B", HeavyBlur = 15, SoftBlur = 1, ArtOpacity = 0.78, SharpOpacity = 0.92,
                MaskMidAlpha = 0x8A, MaskMidOffset = 0.51, MaskEndOffset = 0.65,
                Saturation = 0.80, Contrast = 0.71, Brightness = 0.51,
                TintShade = 0.81, VividAlpha = 0xD4, DarkAlpha = 0xBD,
                Veil = Color.FromArgb(0x36, 0, 0, 0),
                VignetteClear = 0.34, VignetteRadiusX = 0.89, VignetteRadiusY = 0.85, VignetteCentreY = 0.31,
                VignetteMid = Color.FromArgb(0x78, 0, 0, 0), VignetteEdge = Color.FromArgb(0x69, 0, 0, 0),
                RimTopLeft = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
            },
            new BannerLook
            {
                Name = "C", HeavyBlur = 18, SoftBlur = 1.5, ArtOpacity = 0.55, SharpOpacity = 0.60,
                MaskMidAlpha = 0x4D, MaskMidOffset = 0.30, MaskEndOffset = 0.65,
                Saturation = 0.75, Contrast = 0.85, Brightness = 0.80,
                TintShade = 0.45, VividAlpha = 0x73, DarkAlpha = 0xA6,
                Veil = Color.FromArgb(0x66, 0, 0, 0),
                VignetteClear = 0.20, VignetteRadiusX = 0.65, VignetteRadiusY = 0.90, VignetteCentreY = 0.40,
                VignetteMid = Color.FromArgb(0x80, 0, 0, 0), VignetteEdge = Color.FromArgb(0xCC, 0, 0, 0),
                RimTopLeft = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF),
            },
            // True glass: no art; the window's own blur shows what's behind the card, lightly frosted, with an even rim
            new BannerLook
            {
                Name = "D (true glass)", WholeCard = true, Glass = 0xB3201812,
                Frost = Color.FromArgb(0x05, 0xFF, 0xFF, 0xFF), Grain = 0.02,
                Veil = Colors.Transparent, VignetteMid = Colors.Transparent, VignetteEdge = Colors.Transparent,
                RimTopLeft = Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF), RimMid = Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF),
                RimEnd = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF),
                HeavyBlur = 1, SoftBlur = 1, MaskMidOffset = 0.5, MaskEndOffset = 1,
                Saturation = 1, Contrast = 1, Brightness = 1, TintShade = 1,
                VignetteClear = 0.5, VignetteRadiusX = 1, VignetteRadiusY = 1, VignetteCentreY = 0.5,
            },
            // Smoked art panel: the art fills the whole card, blurred to colour fields, darkened within its own hue
            new BannerLook
            {
                Name = "E (smoked art)", WholeCard = true, Glass = 0x80201812,
                HeavyBlur = 48, SoftBlur = 48, ArtOpacity = 0.5, SharpOpacity = 0,
                MaskMidAlpha = 0, MaskMidOffset = 0.5, MaskEndOffset = 1,
                Saturation = 1.35, Contrast = 0.9, Brightness = 0.75,
                TintShade = 1, TintValue = 0.32, TintDarkValue = 0.12, VividAlpha = 0x66, DarkAlpha = 0x99,
                Veil = Color.FromArgb(0x1A, 0, 0, 0),
                VignetteClear = 0.45, VignetteRadiusX = 0.8, VignetteRadiusY = 0.75, VignetteCentreY = 0.35,
                VignetteMid = Color.FromArgb(0x33, 0, 0, 0), VignetteEdge = Color.FromArgb(0x66, 0, 0, 0),
                Frost = Colors.Transparent, Grain = 0.025,
                RimTopLeft = Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF), RimMid = Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF),
                RimEnd = Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF),
            },
            new BannerLook
            {
                Name = "F", HeavyBlur = 14, SoftBlur = 0.5, ArtOpacity = 0.50, SharpOpacity = 0.71,
                MaskMidAlpha = 0x54, MaskMidOffset = 0.34, MaskEndOffset = 0.72,
                Saturation = 1.06, Contrast = 1.00, Brightness = 0.88,
                TintShade = 0.72, VividAlpha = 0x99, DarkAlpha = 0x63,
                Veil = Color.FromArgb(0x75, 0, 0, 0),
                VignetteClear = 0.25, VignetteRadiusX = 0.70, VignetteRadiusY = 0.95, VignetteCentreY = 0.45,
                VignetteMid = Color.FromArgb(0x73, 0, 0, 0), VignetteEdge = Color.FromArgb(0xBF, 0, 0, 0),
                RimTopLeft = Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF),
            },
            // D with the banner shaded darker than the list, in D's own blue-black
            new BannerLook
            {
                Name = "D2 (glass, dark banner)", WholeCard = true, Glass = 0x80201812,
                BannerTintAlpha = 0x73, BannerTintValue = 0.32, BannerTintDarkValue = 0.32, BannerTintDarkSaturation = 1.25,
                ListShade = Color.FromArgb(0x40, 0x12, 0x18, 0x20),
                Frost = Color.FromArgb(0x05, 0xFF, 0xFF, 0xFF), Grain = 0.02,
                Veil = Colors.Transparent, VignetteMid = Colors.Transparent, VignetteEdge = Colors.Transparent,
                RimTopLeft = Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF), RimMid = Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF),
                RimEnd = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF),
                HeavyBlur = 1, SoftBlur = 1, MaskMidOffset = 0.5, MaskEndOffset = 1,
                Saturation = 1, Contrast = 1, Brightness = 1, TintShade = 1,
                VignetteClear = 0.5, VignetteRadiusX = 1, VignetteRadiusY = 1, VignetteCentreY = 0.5,
            },
            // D2 without the list shade: the list as light as the window tint alone leaves it
            new BannerLook
            {
                Name = "D3 (D2, lighter list)", WholeCard = true, Glass = 0x80201812,
                BannerTintAlpha = 0x73, BannerTintValue = 0.40, BannerTintDarkValue = 0.18,
                Frost = Color.FromArgb(0x05, 0xFF, 0xFF, 0xFF), Grain = 0.02,
                Veil = Colors.Transparent, VignetteMid = Colors.Transparent, VignetteEdge = Colors.Transparent,
                RimTopLeft = Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF), RimMid = Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF),
                RimEnd = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF),
                HeavyBlur = 1, SoftBlur = 1, MaskMidOffset = 0.5, MaskEndOffset = 1,
                Saturation = 1, Contrast = 1, Brightness = 1, TintShade = 1,
                VignetteClear = 0.5, VignetteRadiusX = 1, VignetteRadiusY = 1, VignetteCentreY = 0.5,
            },
        };

        public static BannerLook GlassLook => All[7];  // D2, the default
        public static BannerLook ArtLook => All[6];    // F, with "Show the game's art on the card"
        public static BannerLook NoArtLook => All[4];  // D, when there's no art to show or tint from

        // The look on screen; set by the card, read by the model to prepare the art for it
        public static BannerLook Current = GlassLook;

        public static BannerLook Pick(bool showArt, bool hasArt) =>
            DevIndex is int i ? All[i] : !hasArt ? NoArtLook : showArt ? ArtLook : GlassLook;

        // Hidden, for the owner's look work: right-click the banner cycles every look, Ctrl+right-click toggles the
        // glass banner. Not persisted.
        public static int? DevIndex;
        public static void Next() => DevIndex = ((DevIndex ?? -1) + 1) % All.Length;

        // Glass banner: the art layers off, so the window's live blur of what's behind the card shows through the
        // look's tint, veil and vignette. Works with every look.
        public static bool GlassBanner;
        public double ArtShown => GlassBanner ? 0 : ArtOpacity;
        public double SharpShown => GlassBanner ? 0 : SharpOpacity;
    }
}
