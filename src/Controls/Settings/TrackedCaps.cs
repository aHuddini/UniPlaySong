using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace UniPlaySong.Controls.Settings
{
    // WPF's TextBlock has neither letter-spacing nor text-transform, and the settings section
    // headings want both: small, uppercase and tracked, so they read as band markers rather than
    // as another setting label competing with the toggles beneath them.
    //
    // Glyphs with explicit Indices would give true tracking, but it needs the font URI and the
    // advance width of every character. For headings this short, a hair space (U+200A) between
    // characters lands within a fraction of a pixel of the same result at 10px, and costs one
    // attached property instead of a text-rendering pipeline.
    //
    // The readable string stays in the markup — set TrackedCaps.Text, not Text — so the pages can
    // still be searched for "Window Focus" and a translator sees ordinary words.
    public static class TrackedCaps
    {
        private const char HairSpace = ' ';

        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached(
                "Text",
                typeof(string),
                typeof(TrackedCaps),
                new PropertyMetadata(null, OnTextChanged));

        public static void SetText(DependencyObject element, string value) =>
            element.SetValue(TextProperty, value);

        public static string GetText(DependencyObject element) =>
            (string)element.GetValue(TextProperty);

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var block = d as TextBlock;
            if (block == null) return;

            block.Text = Render(e.NewValue as string);
        }

        // Internal rather than private so the transform can be asserted directly in tests.
        internal static string Render(string source)
        {
            if (string.IsNullOrEmpty(source)) return string.Empty;

            var upper = source.ToUpperInvariant();
            var sb = new StringBuilder(upper.Length * 2);

            for (var i = 0; i < upper.Length; i++)
            {
                sb.Append(upper[i]);

                // No spacer after the last character, and none straddling a real space — a word
                // gap that already reads as a gap does not want widening further.
                if (i == upper.Length - 1) continue;
                if (upper[i] == ' ' || upper[i + 1] == ' ') continue;

                sb.Append(HairSpace);
            }

            return sb.ToString();
        }
    }
}
