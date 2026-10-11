// The level select, opened by the home screen's Level button and the Packs tab. Sort Express itself has no level
// list, so it is put together from the game's own parts (tools/make_level_select.py -> Menu/levels.json): the pause
// dialog's pop-up panel, ribbon and close button; per level a light blue row (the pause dialog's inner panel) with
// the level HUD's number pill, the name, a line under it and the home screen's Level button (green / red / purple)
// as Play. Every text uses the game's font and text styles.
using UnityEngine;

namespace Sortex.Game
{
    public sealed class LevelSelectDialog : LevelDialog
    {
        const string Root = "LevelSelect/Root/";
        public const int MaxRows = 5;
        const float RowStep = 1.32f;

        public LevelSelectDialog(Transform parent)
            : base(parent, "LevelSelect", "Menu/levels", "LevelSelect/Foreground")
        {
            if (!Ready) return;
            // lighter than the level end dialogs: the home screen stays visible behind the pop-up
            var v = SpriteAt("LevelSelect/Foreground/SpriteRenderer");
            if (v != null) v.color = new Color(0.01f, 0.015f, 0.05f, 0.72f);
            SetText(Root + "Ribbon/Text", "Levels");
            SetRowCount(4);
        }

        protected override void DefineButtons()
        {
            for (int i = 0; i < MaxRows; i++)
            {
                string row = Root + "Level" + i;
                AddButton("Row" + i, row, row);
                AddButton("Play" + i, row + "/Play", row + "/Play/Root/Button/PressDownTransform");
            }
            AddButton("Close", Root + "CloseButton", Root + "CloseButton/Button/PressDownTransform");
        }

        /// <summary>Shows the first count rows and sizes the pop-up for them (it stays centred on the screen).</summary>
        public void SetRowCount(int count)
        {
            int rows = Mathf.Clamp(count, 1, MaxRows);
            // the pause dialog's proportions: 0.38 from the panel's top edge to the first row, 0.5 under the last one
            float height = 2.04f + (rows - 1) * RowStep;
            float top = height * 0.5f - 0.3f;
            foreach (var side in new[] { "BaseLeft", "BaseRight" })
            {
                var sr = SpriteAt(Root + side);
                if (sr == null) continue;
                sr.size = new Vector2(sr.size.x, height);
                MoveY(Root + side, top - height);     // pivot at the bottom edge
            }
            MoveY(Root + "Ribbon", top + 0.315f);
            MoveY(Root + "CloseButton", top + 0.838f);
            for (int i = 0; i < MaxRows; i++)
            {
                ShowNode(Root + "Level" + i, i < rows);
                MoveY(Root + "Level" + i, top - 0.96f - i * RowStep);
            }
        }

        void MoveY(string path, float y)
        {
            var t = Node(path);
            if (t != null) t.localPosition = new Vector3(t.localPosition.x, y, t.localPosition.z);
        }

        /// <summary>One row: number pill and Play button in the level's tier colour (navy / green Normal, red Hard,
        /// purple Super Hard, like the game's home button), the name, the line under it and a check once won.</summary>
        public void SetRow(int i, int number, string name, string info, int difficultyTag, bool won)
        {
            string row = Root + "Level" + i + "/";
            int tier = difficultyTag >= 20 ? 2 : difficultyTag >= 10 ? 1 : 0;
            string pill = tier == 2 ? "LevelNumberBaseSuperHard" : tier == 1 ? "LevelNumberBaseHard" : "LevelNumberBaseDefault";
            SetSprite(row + "Number/BaseLeft", pill);
            SetSprite(row + "Number/BaseRight", pill);
            SetText(row + "Number/Text", number.ToString());
            // a long name ("Unnecessarily Hard") goes on two lines instead of shrinking to half the size of the others
            string shown = name;
            bool twoLines = false;
            if (EmWidth(name) * NameHeight * 0.95f > NameWidth * 0.98f * 1.3f)
            {
                int cut = BestBreak(name);
                if (cut > 0)
                {
                    shown = name.Substring(0, cut) + "\n" + name.Substring(cut + 1);
                    twoLines = true;
                }
            }
            SetText(row + "Name", "");
            SetText(row + "Info", "");
            SetTextBox(row + "Name", new Vector2(-0.19f, twoLines ? 0.12f : 0.15f), new Vector2(NameWidth, twoLines ? 0.64f : NameHeight));
            SetTextBox(row + "Info", new Vector2(-0.19f, twoLines ? -0.34f : -0.23f), new Vector2(NameWidth, twoLines ? 0.2f : 0.24f));
            SetText(row + "Name", shown);
            SetText(row + "Info", info);
            ShowNode(row + "Check", won);

            string play = row + "Play/Root/";
            string press = play + "Button/PressDownTransform/";
            string variant = tier == 1 ? "PlayButtonHardLevel" : tier == 2 ? "PlayButtonSuperHardLevel" : "PlayButton";
            SetSprite(press + "BaseLeft", variant);
            SetSprite(press + "BaseRight", variant);
            SetSprite(play + "BackgroundLeft", variant + "Background");
            SetSprite(play + "BackgroundRight", variant + "Background");
            ShowNode(press + "DefaultText", true);
            ShowNode(press + "HardText", false);
            ShowNode(press + "SuperHardLevel", false);
            ShowNode(press + "PackLevel", false);
            // one line, outlined in the button's colour (the shadow copy takes its colour from BaseText's style)
            SetStyle(press + "DefaultText/BaseText", tier == 1 ? "Play Button Hard" : tier == 2 ? "Play Button Super Hard" : "Play Button Default");
            SetText(press + "DefaultText/ShadowText", "");
            SetText(press + "DefaultText/ShadowText", "Play");
            SetText(press + "DefaultText/BaseText", "Play");
        }

        const float NameWidth = 1.86f, NameHeight = 0.40f;   // the name's box in the row (tools/make_level_select.py)

        /// <summary>The space that splits a name into the two most even lines (-1: none).</summary>
        int BestBreak(string name)
        {
            int best = -1;
            float bestWidth = float.MaxValue;
            for (int i = name.IndexOf(' '); i > 0; i = name.IndexOf(' ', i + 1))
            {
                float w = Mathf.Max(EmWidth(name.Substring(0, i)), EmWidth(name.Substring(i + 1)));
                if (w < bestWidth)
                {
                    bestWidth = w;
                    best = i;
                }
            }
            return best;
        }

        public void Show()
        {
            SetVisible(true);
        }
    }
}
