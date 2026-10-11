// The game's pause dialog, rebuilt from Sort Express' LevelPauseDialog.prefab (see PrefabScreen):
// "Paused" ribbon, music / sound / vibration / hints toggles, Continue (green), Quit (red) and the close button.
// The toggles only switch their crossed-out icon here (the prototype has no audio).
using UnityEngine;

namespace Sortex.Game
{
    public sealed class PauseDialog : PrefabScreen
    {
        const string Dialog = "LevelPauseDialog/Root/";
        public static readonly string[] Toggles = { "Music", "Sound", "Vibration", "Hints" };

        public PauseDialog(Transform parent) : base(parent, "PauseDialog", "Level/pause", 2000)
        {
            if (!Ready) return;
            textTint = new Color(1f, 0.96f, 0.885f, 1f);
            SetText(Dialog + "Ribbon/Text", "Paused");
            SetText(Dialog + "ContinueButton/Button/PressDownTransform/BaseText", "Continue");
            SetText(Dialog + "QuitButton/Button/PressDownTransform/BaseText", "Quit");
            // the dialog fades the screen behind it to a dark veil
            var veil = SpriteAt("LevelPauseDialog/Foreground/SpriteRenderer");
            if (veil != null) veil.color = new Color(0.01f, 0.02f, 0.08f, 0.72f);
            SetVisible(false);
        }

        protected override void DefineButtons()
        {
            AddButton("Continue", Dialog + "ContinueButton", Dialog + "ContinueButton/Button/PressDownTransform");
            AddButton("Quit", Dialog + "QuitButton", Dialog + "QuitButton/Button/PressDownTransform");
            AddButton("Close", Dialog + "CloseButton", Dialog + "CloseButton/Button/PressDownTransform");
            foreach (var t in Toggles)
                AddButton(t, Dialog + "ButtonsFrame/" + t + "Button", Dialog + "ButtonsFrame/" + t + "Button/Button/PressDownTransform");
        }

        /// <summary>Switches a toggle's crossed-out icon (the game's OffIcon).</summary>
        public void Toggle(string name)
        {
            var off = SpriteAt(Dialog + "ButtonsFrame/" + name + "Button/Button/PressDownTransform/OffIcon");
            if (off != null) off.enabled = !off.enabled;
        }

        /// <summary>Follows the camera like the HUD; the veil grows with tall screens.</summary>
        public void Layout(Camera cam, float aspect)
        {
            Follow(cam, aspect);
            var fg = Node("LevelPauseDialog/Foreground");
            if (fg != null) fg.localScale = new Vector3(refWidth * 1.1f, ReferenceHeight(aspect) * 1.1f, 1f);
        }
    }
}
