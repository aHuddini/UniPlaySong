using System.Runtime.CompilerServices;

// Exposes internal members (e.g. SpotifyLauncher's testable ResolveSpotifyPath overload) to the
// test assembly. Kept out of AssemblyInfo.cs, which the packaging script version-stamps.
[assembly: InternalsVisibleTo("UniPlaySong.Tests")]
