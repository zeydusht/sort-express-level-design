// The game's in-level HUD, rebuilt from Sort Express' LevelSceneUI.prefab (see PrefabScreen):
//   top:    LevelNumber ("Level N" on the navy / red / purple plate), the Timer (dark track, green ring,
//           cream digits) and the PauseButton
//   bottom: the four boosters (vacuum, magic hat, painter, freeze) with their count badges, on a wooden plank
// The boosters are 3D models in the game; their icons stand in for them here. They are not part of this
// prototype (the case is about level design), so tapping one only tells that.
using UnityEngine;

namespace Sortex.Game
{
    public sealed class LevelHud : PrefabScreen
    {
        const string Level = "TopUI/LevelNumber/Root/";
        const string Timer = "TopUI/Timer/";
        const string Digits = "TopUI/Timer/TextParent/:";
        static readonly string[] BoosterButtons = { "VacuumButton", "ShuffleButton", "PainterButton", "FreezeButton" };
        public static readonly string[] BoosterNames = { "Vacuum", "Shuffle", "Painter", "Freeze" };

        // where the game keeps the board: below the top bar and above the boosters (reference screen units, from a
        // screenshot of the game: the shelves fill the screen between ~2.0 below the top and ~2.0 above the bottom)
        public const float BoardTop = 2.0f, BoardBottom = 2.0f;

        static readonly Color Green = new Color(0.157f, 1f, 0.157f, 1f);
        static readonly Color Red = new Color(1f, 0.16f, 0.12f, 1f);
        int shownSeconds = -1;

        public LevelHud(Transform parent) : base(parent, "LevelHud", "Level/level", 1000)
        {
            if (!Ready) return;
            // the game tints its TextMeshPro texts cream (screenshot: 249, 235, 207) and keeps them below the box height
            textTint = new Color(1f, 0.96f, 0.885f, 1f);
            SetTextScale(Level + "Text", 0.78f);
            SetTextScale(Digits, 0.78f);
            // the game writes minutes, ":" and seconds into three boxes from code; one centred text here
            ShowNode(Timer + "TextParent/MinuteText", false);
            ShowNode(Timer + "TextParent/SecondsText", false);
            SetTextBox(Digits, new Vector2(0f, 0f), new Vector2(1.0f, 0.3388f));
            SetVisible(false);
        }

        protected override void DefineButtons()
        {
            AddButton("Pause", "TopUI/PauseButton", "TopUI/PauseButton/Button/PressDownTransform");
            for (int i = 0; i < BoosterButtons.Length; i++)
                AddButton(BoosterNames[i], "BoosterContainer/" + BoosterButtons[i], "BoosterContainer/" + BoosterButtons[i]);
        }

        /// <summary>"Level N" on the plate of the level's tier (navy, red for Hard, purple for Super Hard).</summary>
        public void SetLevel(int number, int difficultyTag)
        {
            string plate = difficultyTag >= 20 ? "LevelNumberBaseSuperHard" : difficultyTag >= 10 ? "LevelNumberBaseHard" : "LevelNumberBaseDefault";
            SetSprite(Level + "BaseLeft", plate);
            SetSprite(Level + "BaseRight", plate);
            SetText(Level + "Text", "Level " + number);
            shownSeconds = -1;
        }

        /// <summary>m:ss in the timer; the ring turns red and the warning glow pulses in the last 10 seconds.</summary>
        public void SetTime(float secondsLeft, bool running)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(secondsLeft));
            if (s != shownSeconds)
            {
                shownSeconds = s;
                SetText(Digits, (s / 60) + ":" + (s % 60).ToString("00"));
            }
            bool low = running && secondsLeft <= 10f;
            float pulse = low ? 0.5f + 0.5f * Mathf.Sin(Time.time * 8f) : 0f;
            var ring = SpriteAt(Timer + "NewTimerSetup/TimerFill");
            if (ring != null) ring.color = low ? Color.Lerp(Red, Green, 0.15f * pulse) : Green;
            foreach (var side in new[] { "BaseLeft", "BaseRight" })
            {
                var glow = SpriteAt(Timer + "NewTimerSetup/Warning/" + side);
                if (glow != null) glow.color = new Color(1f, 0.1f, 0.05f, low ? 0.35f + 0.55f * pulse : 0f);
            }
        }

        /// <summary>The count badges of the four boosters.</summary>
        public void SetBoosters(params int[] counts)
        {
            for (int i = 0; i < BoosterButtons.Length && i < counts.Length; i++)
                SetText("BoosterContainer/" + BoosterButtons[i] + "/Badge/Counter/Text", counts[i].ToString());
        }
    }
}
