using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using Playnite.SDK;
using Playnite.SDK.Events;

namespace UniPlaySong.Controls
{
    // The right stick's analog position, for seeking. Playnite SDK 6.18 added GetConnectedControllers2() with analog
    // values; UPS builds against an older SDK so it keeps loading on older Playnite, so this finds the method at run
    // time. On a Playnite without it, TryCreate returns null and seeking is mouse-only.
    internal sealed class RightStick
    {
        private readonly IPlayniteAPI _api;
        private readonly MethodInfo _getControllers;
        private readonly PropertyInfo _analogState;
        private readonly ControllerInput _x, _y;

        private RightStick(IPlayniteAPI api, MethodInfo getControllers, PropertyInfo analogState, ControllerInput x, ControllerInput y)
        {
            _api = api; _getControllers = getControllers; _analogState = analogState; _x = x; _y = y;
        }

        // Looked up on the interface, not the API object's class: an explicit interface implementation has no public
        // method of that name on the class. `why` says what's missing when it returns null.
        public static RightStick TryCreate(IPlayniteAPI api, out string why)
        {
            why = null;
            try
            {
                var method = typeof(IPlayniteAPI).GetMethod("GetConnectedControllers2", Type.EmptyTypes);
                if (api == null) { why = "no API"; return null; }
                if (method == null) { why = "this Playnite has no GetConnectedControllers2 (SDK before 6.18)"; return null; }
                var gamepad = method.ReturnType.IsGenericType ? method.ReturnType.GetGenericArguments()[0] : null;
                var analog = gamepad?.GetProperty("AnalogInputState");
                if (analog == null) { why = "controllers report no AnalogInputState"; return null; }
                if (!Enum.IsDefined(typeof(ControllerInput), "RightStickX") || !Enum.IsDefined(typeof(ControllerInput), "RightStickY"))
                {
                    why = "no RightStickX/Y inputs";
                    return null;
                }
                return new RightStick(api, method, analog,
                    (ControllerInput)Enum.Parse(typeof(ControllerInput), "RightStickX"),
                    (ControllerInput)Enum.Parse(typeof(ControllerInput), "RightStickY"));
            }
            catch (Exception ex) { why = ex.Message; return null; }
        }

        // -1..1 per axis, from the first controller that reports one; y grows downwards. False while no Playnite window
        // is active: with a game in front, the stick belongs to the game, not to the music.
        public bool TryRead(out double x, out double y)
        {
            x = y = 0;
            if (Application.Current?.Windows.Cast<Window>().Any(w => w.IsActive) != true) return false;
            try
            {
                if (!(_getControllers.Invoke(_api, null) is IEnumerable pads)) return false;
                foreach (var pad in pads)
                {
                    if (!(_analogState.GetValue(pad) is IReadOnlyDictionary<ControllerInput, short> state)) continue;
                    if (!state.TryGetValue(_x, out short rx) || !state.TryGetValue(_y, out short ry)) continue;
                    x = rx / 32768.0;
                    y = ry / 32768.0;
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
