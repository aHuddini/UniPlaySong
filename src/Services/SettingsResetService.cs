using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UniPlaySong.Services
{
    // Resets one left-rail group's settings.
    //
    // Values are never written down here. They are read off a pristine UniPlaySongSettings, so the
    // backing-field initialisers stay the single source of truth. The per-tab handlers this
    // replaces restated every default by hand, which let them drift: one had EnableIconGlow reset
    // to true when the shipped default is false, and the Live Effects handler applied the Default
    // visualizer preset's numbers while labelling the selection Punchy.
    internal static class SettingsResetService
    {
        internal static IEnumerable<string> GroupNames => SettingsGroups.Map.Keys;

        // True when the group owns at least one setting. About and Quick Start own none, so they
        // get no reset button rather than an inert one.
        internal static bool HasResettableSettings(string group)
        {
            string[] names;
            return SettingsGroups.Map.TryGetValue(group, out names) && names.Length > 0;
        }

        internal static int ResetGroup(UniPlaySongSettings target, string group)
        {
            string[] names;
            if (!SettingsGroups.Map.TryGetValue(group, out names)) return 0;
            return ResetProperties(target, names);
        }

        // The primitive every reset goes through, so no caller ever writes a default value down.
        internal static int ResetProperties(UniPlaySongSettings target, IEnumerable<string> names)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (names == null) return 0;

            // A fresh instance per call, not a cached one: collection-valued settings would
            // otherwise hand every reset the same List reference to share.
            var pristine = new UniPlaySongSettings();
            var type = typeof(UniPlaySongSettings);
            var applied = 0;

            foreach (var name in names)
            {
                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null || !prop.CanRead || !prop.CanWrite) continue;

                prop.SetValue(target, prop.GetValue(pristine, null), null);
                applied++;
            }

            return applied;
        }

        // Every property a reset is allowed to touch. Used by the coverage test; kept here so the
        // test and the service agree on what counts as a setting.
        internal static IEnumerable<PropertyInfo> ResettableProperties()
        {
            return typeof(UniPlaySongSettings)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0);
        }
    }
}
