// The game's level end screens, rebuilt from its prefabs (see PrefabScreen):
//   WinDialog       LevelWinDialog.prefab      "Level Completed!", the coin reward on its red ribbon, the time, Continue
//   OutOfDialog     NoMoreMatchesPanel.prefab  the last chance when the shelves are full ("Out of Space!") or the
//                                              time is up ("Out of Time!"): the offer, Play On, the close button
//   LoseDialog      LevelLoseDialog.prefab     "Level Failed" with the broken heart, the boosters for the next try,
//                                              Try Again and the close button
// The prefabs hold no texts (the game fills them from code, localised): the words here are the prototype's.
// The win dialog's second button (double reward for an ad) and the lose dialog's streak panel are left out.
using UnityEngine;

namespace Sortex.Game
{
    /// <summary>A full-screen dialog over the level: follows the camera like the HUD and darkens what is behind it.</summary>
    public abstract class LevelDialog : PrefabScreen
    {
        readonly string veilNode, dialogNode;
        public const int BaseOrder = 3000;
        // the game darkens the level behind its end dialogs almost to black (the prefabs' veil: black, alpha 0.99)
        protected static readonly Color Veil = new Color(0.01f, 0.015f, 0.05f, 0.97f);

        protected LevelDialog(Transform parent, string name, string resource, string veilNode)
            : base(parent, name, resource, BaseOrder)
        {
            this.veilNode = veilNode;
            dialogNode = veilNode.Substring(0, veilNode.IndexOf('/'));
            if (!Ready) return;
            textTint = new Color(1f, 0.96f, 0.885f, 1f);
            var v = SpriteAt(veilNode + "/SpriteRenderer");
            if (v != null) v.color = Veil;
            SetVisible(false);
        }

        /// <summary>Follows the camera like the HUD. The dialogs are laid out on the 4.6166 x 10 screen; on a shorter
        /// one (9:16 is 8.2 tall) the whole dialog is scaled down so its top buttons and title stay apart and on screen.</summary>
        public void Layout(Camera cam, float aspect)
        {
            Follow(cam, aspect);
            float refH = ReferenceHeight(aspect);
            float k = Mathf.Min(1f, refH / refHeight);
            var dialog = Node(dialogNode);
            if (dialog != null) dialog.localScale = new Vector3(k, k, 1f);
            var fg = Node(veilNode);
            if (fg != null) fg.localScale = new Vector3(refWidth * 1.1f / k, refH * 1.1f / k, 1f);
        }

        /// <summary>Sets the four layers of a dialog title (Bottom2, Bottom1, Center, Main) to one text.</summary>
        protected void SetTitle(string titlePath, string text)
        {
            foreach (var layer in new[] { "Bottom2", "Bottom1", "Center", "Main" })
                SetText(titlePath + "/" + layer, text);
        }

        protected static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }
    }

    public sealed class WinDialog : LevelDialog
    {
        const string Base = "LevelWinDialog/Root/Base/";

        public WinDialog(Transform parent)
            : base(parent, "WinDialog", "Level/win", "LevelWinDialog/Background")
        {
            if (!Ready) return;
            // the single Continue button (the game's layout without the ad button)
            ShowNode(Base + "ContinueButton", true);
            SetText(Base + "ContinueButton/Button/PressDownTransform/BaseText", "Continue");
            // the time sits right of the clock (the game lays the two out from code)
            SetTextBox(Base + "Time/Text", new Vector2(0.42f, 0f), new Vector2(1.0f, 0.4089f));
            SetTitle(Base + "BigTitleName", "Level Completed!");
        }

        protected override void DefineButtons()
        {
            AddButton("Continue", Base + "ContinueButton", Base + "ContinueButton/Button/PressDownTransform");
        }

        public void Show(int coins, float seconds)
        {
            SetText(Base + "Reward/Coin/Root/Text", coins.ToString());
            SetText(Base + "Time/Text", Clock(seconds));
            SetVisible(true);
        }
    }

    public sealed class OutOfDialog : LevelDialog
    {
        const string Root = "NoMoreMatchesPanel/Root/";
        const string Ego = Root + "Center/LastChance/Root/EGO/";
        const string PlayOn = Root + "PlayOn/PlayOnButton/";
        public const int PlayOnCost = 900;
        public const int ExtraSeconds = 60;

        public OutOfDialog(Transform parent)
            : base(parent, "OutOfDialog", "Level/nomore", "NoMoreMatchesPanel/Foreground")
        {
            if (!Ready) return;
            SetText(PlayOn + "Button/PressDownTransform/BaseText", "Play On");
            SetText(PlayOn + "Button/PressDownTransform/CostText", PlayOnCost.ToString());
        }

        protected override void DefineButtons()
        {
            AddButton("PlayOn", Root + "PlayOn/PlayOnButton", PlayOn + "Button/PressDownTransform");
            AddButton("Close", Root + "Top/CloseButton", Root + "Top/CloseButton/Button/PressDownTransform");
            AddButton("Coins", Root + "Top/CoinsButton", Root + "Top/CoinsButton/Button/PressDownTransform");
        }

        /// <summary>Out of time: the hourglass and 60 more seconds; out of space: the magic hat (the game's offer).</summary>
        public void Show(bool timeUp, int coins)
        {
            SetTitle(Root + "Top/TitleRoot/InfoText", timeUp ? "Out of Time!" : "Out of Space!");
            SetSprite(Ego + "Icon/EGOImage", timeUp ? "HourglassIcon256" : "MagicHatIcon256");
            SetText(Ego + "EGOText", timeUp ? "Get " + ExtraSeconds + " more seconds!" : "Shuffle the shelves with a Magic Hat!");
            SetText(Root + "Top/CoinsButton/Button/PressDownTransform/Text", coins.ToString());
            SetVisible(true);
        }
    }

    public sealed class LoseDialog : LevelDialog
    {
        const string Root = "LevelLoseDialog/Root/";
        const string Boosters = Root + "PreLevelBoosterButtons/";
        public static readonly string[] BoosterNames = { "Hourglass", "Magnet" };

        public LoseDialog(Transform parent)
            : base(parent, "LoseDialog", "Level/lose", "LevelLoseDialog/Foreground")
        {
            if (!Ready) return;
            SetTitle(Root + "Panel/InfoText", "Level Failed");
            SetText(Boosters + "SelectText", "Select Boosters");
            foreach (var b in BoosterNames)
            {
                SetText(Boosters + "PreLevelBooster" + b + "Button/Badge/Counter/Text", "1");
                // not selected: the yellow button (the green one is the selected state)
                ShowNode(Boosters + "PreLevelBooster" + b + "Button/Button/ActiveButton", false);
            }
            SetText(Root + "TryAgainButton/Button/PressDownTransform/BaseText", "Try Again");
        }

        protected override void DefineButtons()
        {
            AddButton("TryAgain", Root + "TryAgainButton", Root + "TryAgainButton/Button/PressDownTransform");
            AddButton("Close", Root + "CloseButton", Root + "CloseButton/Button/PressDownTransform");
            foreach (var b in BoosterNames)
                AddButton(b, Boosters + "PreLevelBooster" + b + "Button", Boosters + "PreLevelBooster" + b + "Button/Button");
        }

        public void Show()
        {
            SetVisible(true);
        }
    }
}
