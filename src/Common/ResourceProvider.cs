using System.Windows;

namespace UniPlaySong.Common
{
    /// <summary>
    /// Provides localized strings from the active ResourceDictionary.
    /// Falls back to the key name if a string is missing (safe degradation).
    /// </summary>
    public static class ResourceProvider
    {
        public static string GetString(string key)
        {
            var resource = Application.Current?.TryFindResource(key);
            return resource as string ?? key;
        }
    }
}
