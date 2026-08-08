using System.Windows;

namespace UniPlaySong.Common
{
    // Provides localized strings from the active ResourceDictionary. Falls back to the key name if a
    // string is missing (safe degradation).
    public static class ResourceProvider
    {
        public static string GetString(string key)
        {
            var resource = Application.Current?.TryFindResource(key);
            return resource as string ?? key;
        }
    }
}
