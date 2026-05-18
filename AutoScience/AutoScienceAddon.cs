using KSP.UI.Screens;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace AutoScience {
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    class AutoScienceAddon : MonoBehaviour {

        // Settings/GUI stuff
        private static Texture2D ToolbarIconTexture = null;
        private static ApplicationLauncherButton ToolbarIcon = null;
        private static PopupDialog DialogWindow = null;
        private static readonly string SettingsPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "/AutoScience.cfg";

        // Mod settings saved in cfg alongside dll (KSPAddon can't put them in savefile - change?)
        public static bool ModActive = true;
        public static bool TrackUnloadedVessels = true;
        public static bool CollectZeroValueScience = false;
        public static bool CollectDuplicateScience = false;

        /// <summary>
        /// Tell vessel module to rebuild its info (experiments list, container, etc) whenever craft is modified
        /// (Docking, Decoupling, Crashing, EVA construction etc)
        /// <summary>
        public void Start() {
            if (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedScene == GameScenes.SPACECENTER || HighLogic.LoadedScene == GameScenes.TRACKSTATION) {
                // Load settings from cfg
                Load();
                
                // Rebuild vessel info whenever it's modified (Docking, Decoupling, Crashing, EVA construction etc)
                GameEvents.onVesselWasModified.Add(vessel => vessel.FindVesselModuleImplementing<AutoScienceVesselModule>().Rebuild());

                // set up toolbar icon and behaviour
                ToolbarIconTexture = GameDatabase.Instance.GetTexture("Auto Science/Icons/AutoScience", false);
                ToolbarIcon = ApplicationLauncher.Instance.AddModApplication(ToggleGUI, ToggleGUI, null, null, null, null,
                    ApplicationLauncher.AppScenes.ALWAYS, ToolbarIconTexture);
            }
        }

        public void OnDisable() {
            ApplicationLauncher.Instance.RemoveModApplication(ToolbarIcon);
        }

        /// <summary>
        /// Toggle settings window when user clicks toolbar icon
        /// </summary>
        private void ToggleGUI() {
            if (DialogWindow != null && DialogWindow.enabled) {
                DialogWindow.Dismiss();
            } else {
                DialogWindow = GetOptionsWindow();
            }
        }

        /// <summary>
        /// Serialize mod settings to cfg and save alongside dll
        /// </summary>
        private void Save() {
            ConfigNode.CreateConfigFromObject(this, new ConfigNode(GetType().Name)).Save(SettingsPath);
            ConfigNode SettingsSave = new ConfigNode();
            SettingsSave.AddValue("ModActive", ModActive.ToString());
            SettingsSave.AddValue("TrackUnloadedVessels", TrackUnloadedVessels.ToString());
            SettingsSave.AddValue("CollectZeroValueScience", CollectZeroValueScience.ToString());
            SettingsSave.AddValue("CollectDuplicateScience", CollectDuplicateScience.ToString());
            SettingsSave.Save(SettingsPath);
        }

        /// <summary>
        /// Load mod settings from cfg alongside dll
        /// </summary>
        private void Load() {
            if (File.Exists(SettingsPath)) {
                ConfigNode SettingsSave = ConfigNode.Load(SettingsPath);
                ModActive = bool.Parse(SettingsSave.GetValue("ModActive"));
                TrackUnloadedVessels = bool.Parse(SettingsSave.GetValue("TrackUnloadedVessels"));
                CollectZeroValueScience = bool.Parse(SettingsSave.GetValue("CollectZeroValueScience"));
                CollectDuplicateScience = bool.Parse(SettingsSave.GetValue("CollectDuplicateScience"));
            }
        }

        /// <summary>
        /// Creates options window each time user clicks toolbar icon
        /// </summary>
        private PopupDialog GetOptionsWindow() {
            DialogGUIVerticalLayout Content = new DialogGUIVerticalLayout(true);

            Content.AddChild(new DialogGUIToggleButton(
                ModActive,
                "Mod Active",
                (Value) => { ModActive = Value; Save(); },
                -1, 30));

            Content.AddChild(new DialogGUIToggleButton(
                TrackUnloadedVessels,
                "Track Unloaded Vessels",
                (Value) => { TrackUnloadedVessels = Value; Save(); },
                -1, 30));

            Content.AddChild(new DialogGUIToggleButton(
                CollectZeroValueScience,
                "Collect Zero Value Science",
                (Value) => { CollectZeroValueScience = Value; Save(); },
                -1, 30));

            Content.AddChild(new DialogGUIToggleButton(
                CollectDuplicateScience,
                "Collect Duplicate Science",
                (Value) => { CollectDuplicateScience = Value; Save(); },
                -1, 30));

            return PopupDialog.SpawnPopupDialog(
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new MultiOptionDialog("AutoScience", "", "AutoScience", HighLogic.UISkin,
                    new Rect(0.5f, 0.5f, 250f, 100f),
                    Content),
                false,
                HighLogic.UISkin,
                false
                );
        }
    }
}
