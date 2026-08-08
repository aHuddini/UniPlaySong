using System;
using Playnite.SDK;
using Playnite.SDK.Plugins;

namespace UniPlaySong.Menus
{
    // Handles main menu actions
    public class MainMenuHandler
    {
        private readonly IPlayniteAPI _playniteApi;
        private readonly Guid _pluginId;

        public MainMenuHandler(IPlayniteAPI playniteApi, Guid pluginId)
        {
            _playniteApi = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            _pluginId = pluginId;
        }

        public void OpenSettings()
        {
            _playniteApi.MainView.OpenPluginSettings(_pluginId);
        }
    }
}

