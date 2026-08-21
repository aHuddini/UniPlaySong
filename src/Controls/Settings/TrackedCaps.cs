using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace UniPlaySong.Controls.Settings
{
    // WPF's TextBlock has neither letter-spacing nor text-transform, and the settings section
    // headings want both: small, uppercase and tracked, so they read as band markers rather than
    // as another setting label competing with the toggles beneath them.
    //
    // Spacing is done with a space Run whose FontSize is scaled down, rather than by inserting a
    // fixed spacing character. A hair space is whatever width the font happens to give it, which
    // made Tracking a label rather than a value; scaling a real space makes the em figure mean
    // what it says and lets it be tuned.
    //
    // The readable string stays in the markup - set TrackedCaps.Text, not Text - so pages can
    // still be searched for "Window Focus" and a translator sees ordinary words.
    public static class TrackedCaps
    {
        // A space is roughly a quarter em in the UI faces this ships against, so a spacer of
        // FontSize * (tracking / 0.25) lands close to the requested tracking.
        private const double SpaceWidthEm = 0.25;
        private const double FallbackFontSize = 10.0;

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached(
                "Text", typeof(string), typeof(TrackedCaps),
                new PropertyMetadata(null, OnChanged));

        public static void SetText(DependencyObject e, string v) => e.SetValue(TextProperty, v);
        public static string GetText(DependencyObject e) => (string)e.GetValue(TextProperty);

        public static readonly DependencyProperty TrackingProperty =
            DependencyProperty.RegisterAttached(
                "Tracking", typeof(double), typeof(TrackedCaps),
                new PropertyMetadata(0.1, OnChanged));

        public static void SetTracking(DependencyObject e, double v) => e.SetValue(TrackingProperty, v);
        public static double GetTracking(DependencyObject e) => (double)e.GetValue(TrackingProperty);

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var block = d as TextBlock;
            if (block == null) return;

            Apply(block);

            // FontSize usually arrives from the Style after this fires, and the spacer is derived
            // from it, so rebuild once the block is loaded and the real size is known.
            block.Loaded -= OnLoaded;
            block.Loaded += OnLoaded;
        }

        private static void OnLoaded(object sender, RoutedEventArgs e) => Apply(sender as TextBlock);

        private static void Apply(TextBlock block)
        {
            if (block == null) return;

            var size = block.FontSize > 0 ? block.FontSize : FallbackFontSize;
            block.Inlines.Clear();
            foreach (var seg in Segments(GetText(block), GetTracking(block), size))
            {
                var run = new Run(seg.Text);
                if (seg.FontSize.HasValue) run.FontSize = seg.FontSize.Value;
                block.Inlines.Add(run);
            }
        }

        internal struct Segment
        {
            public string Text;
            public double? FontSize;   // set only on spacers
        }

        // Pure, so the spacing rules can be asserted without a visual tree.
        internal static IEnumerable<Segment> Segments(string source, double tracking, double fontSize)
        {
            var result = new List<Segment>();
            if (string.IsNullOrEmpty(source)) return result;

            var upper = source.ToUpperInvariant();
            var spacer = fontSize * (tracking / SpaceWidthEm);

            // Spacer after every character except the last, spaces included - the same rule CSS
            // letter-spacing follows. Skipping it around word gaps, which is what this did first,
            // leaves a word gap of one plain space while every letter gap is space + tracking; at
            // 10px that is 2.5px against 1px and "STYLE PRESET" closes up into one word. Tracking
            // both sides of the space instead makes the word gap the widest gap in the line.
            for (var i = 0; i < upper.Length; i++)
            {
                result.Add(new Segment { Text = upper[i].ToString() });

                if (i < upper.Length - 1)
                    result.Add(new Segment { Text = " ", FontSize = spacer });
            }

            return result;
        }
    }
}
