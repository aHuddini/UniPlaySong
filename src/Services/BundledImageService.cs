using System.IO;

namespace UniPlaySong.Services
{
    // Resolves bundled UI art (currently the achievement rarity badges) shipped in the extension's
    // Images/ folder. Initialized once at startup with the extension install path, same pattern as
    // BundledJingleService. The badge PNGs are derived from the Playnite Achievements plugin's badge
    // SVGs (MIT, (c) Justin Delano) — see NOTICES.txt.
    public static class BundledImageService
    {
        private static string _imagesDirectory;

        public static void Initialize(string extensionInstallPath)
        {
            _imagesDirectory = Path.Combine(extensionInstallPath, "Images");
        }

        // Full path to an achievement rarity badge PNG, or "" if unavailable.
        // rarity: "bronze" | "silver" | "gold" | "platinum" | "perfect".
        public static string GetAchievementBadgePath(string rarity)
        {
            if (string.IsNullOrEmpty(_imagesDirectory) || string.IsNullOrEmpty(rarity))
                return string.Empty;
            var path = Path.Combine(_imagesDirectory, "Achievements", $"badge-{rarity}.png");
            return File.Exists(path) ? path : string.Empty;
        }
    }
}
