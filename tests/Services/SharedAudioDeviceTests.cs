using System.Reflection;
using NUnit.Framework;
using UniPlaySong.Services;

namespace UniPlaySong.Tests.Services
{
    // SDL2's audio device is process-wide: one static Mix_OpenAudio/Mix_CloseAudio, not one per
    // player. Reported by a user - "even if UPS doesn't play any audio, just as long as it's
    // enabled it holds that system driver open and stops the PC from going to sleep."
    //
    // Cause: PrewarmExternalPlayer opens the device through a THROWAWAY probe and drops it. The
    // disposed probe still reported IsAudioDeviceOpen (the flag is static) while its own
    // ReleaseAudioDevice early-returned on _isDisposed, and the release was additionally gated on
    // enableIdleTeardown - which only the MAIN SDL2 player is given. With Live Effects on there is
    // no main SDL2 player at all, so nothing in the process could ever close it.
    //
    // These pin the shape of the fix by reflection: the type cannot be constructed here because
    // the constructor calls InitializeSDL() and the test host has no native SDL2 binary.
    [TestFixture]
    public class SharedAudioDeviceTests
    {
        // The whole point: no instance has to own the device for it to be closable, because the
        // probe that opened it is already gone by the time anything wants it closed.
        [Test]
        public void CloseSharedDevice_IsStatic_SoADroppedProbeCannotStrandIt()
        {
            var m = typeof(SDL2MusicPlayer).GetMethod(
                nameof(SDL2MusicPlayer.CloseSharedDeviceIfUnused),
                BindingFlags.Public | BindingFlags.Static);

            Assert.That(m, Is.Not.Null, "must be callable without an instance");
            Assert.That(m.GetParameters(), Is.Empty, "there is no owner to pass in");
        }

        // Safe when SDL was never initialised - every Live-Effects-only session.
        [Test]
        public void CloseSharedDevice_IsANoOpWhenNothingWasOpened()
        {
            Assert.DoesNotThrow(() => SDL2MusicPlayer.CloseSharedDeviceIfUnused());
            Assert.DoesNotThrow(() => SDL2MusicPlayer.CloseSharedDeviceIfUnused(), "and idempotent");
        }

        // The registry must attempt the shared close even when no holder claims an open device -
        // the disposed-probe case, where the holder list is misleading.
        [Test]
        public void Registry_AsksForTheSharedClose_EvenWithNoOpenHolders()
        {
            var registry = new AudioDeviceRegistry(null);
            Assert.That(registry.IsAnyDeviceOpen, Is.False, "no holders registered");
            Assert.DoesNotThrow(() => registry.ReleaseAllDevices("test"),
                "must still reach the process-wide close rather than returning early");
        }

    }
}
