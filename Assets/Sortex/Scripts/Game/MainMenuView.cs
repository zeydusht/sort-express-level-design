// The game's own home screen, rebuilt from Sort Express' MainScene.prefab (see PrefabScreen):
// the HomePage background, the green LoadLevelButton, the TopUI (avatar, lives, coins) and the NavigationBar.
using UnityEngine;

namespace Sortex.Game
{
    public sealed class MainMenuView : PrefabScreen
    {
        public static readonly Vector3 Origin = new Vector3(0f, 500f, 0f);   // far away from the board

        public MainMenuView(Transform parent) : base(parent, "MainMenu", "Menu/menu", 0)
        {
            root.position = Origin;
        }

        public float Width { get { return refWidth; } }

        // ================================================================== buttons
        protected override void DefineButtons()
        {
            AddButton("Play", "LoadLevelButton", "LoadLevelButton/Root/Button/PressDownTransform");
            AddButton("Lives", "TopUI/LivesButton", "TopUI/LivesButton/Button/PressDownTransform");
            AddButton("Coins", "TopUI/CoinsButton", "TopUI/CoinsButton/Button/PressDownTransform");
            AddButton("Profile", "TopUI/Icons/ProfileAvatarButton", null);
            foreach (var b in new[] { "ShopButton", "LeaderboardButton", "HomeButton", "PacksButton", "SettingsButton" })
                AddButton(b.Replace("Button", ""), "NavigationBar/Root/" + b, null);
        }

        const string Play = "LoadLevelButton/Root/";
        const string PlayText = "LoadLevelButton/Root/Button/PressDownTransform/";

        /// <summary>"Level N" on the green button; Hard and Super Hard levels get the game's red / purple button and tag.</summary>
        public void SetLevel(int number, int difficultyTag)
        {
            string label = "Level " + number;
            int tier = difficultyTag >= 20 ? 2 : difficultyTag >= 10 ? 1 : 0;
            ShowNode(PlayText + "DefaultText", tier == 0);
            ShowNode(PlayText + "HardText", tier == 1);
            ShowNode(PlayText + "SuperHardLevel", tier == 2);
            ShowNode(PlayText + "PackLevel", false);
            string variant = tier == 1 ? "PlayButtonHardLevel" : tier == 2 ? "PlayButtonSuperHardLevel" : "PlayButton";
            SetSprite(PlayText + "BaseLeft", variant);
            SetSprite(PlayText + "BaseRight", variant);
            SetSprite(Play + "BackgroundLeft", variant + "Background");
            SetSprite(Play + "BackgroundRight", variant + "Background");
            if (tier == 0)
            {
                SetText(PlayText + "DefaultText/ShadowText", label);
                SetText(PlayText + "DefaultText/BaseText", label);
            }
            else if (tier == 1)
            {
                SetText(PlayText + "HardText/ShadowText", label);
                SetText(PlayText + "HardText/BaseText", label);
                SetText(PlayText + "HardText/HardLevelText/ShadowText", "Hard Level");
                SetText(PlayText + "HardText/HardLevelText/BaseText", "Hard Level");
            }
            else
            {
                SetText(PlayText + "SuperHardLevel/ShadowText", label);
                SetText(PlayText + "SuperHardLevel/BaseText", label);
                SetText(PlayText + "SuperHardLevel/SuperHardText/ShadowText", "Super Hard");
                SetText(PlayText + "SuperHardLevel/SuperHardText/BaseText", "Super Hard");
            }
        }

        /// <summary>The top bar: lives (full) and coins, like a fresh install of the game.</summary>
        public void SetWallet(int lives, int coins)
        {
            const string life = "TopUI/LivesButton/Button/PressDownTransform/LifeTimerContainer/";
            SetText(life + "Icon/IconText", lives.ToString());
            SetText(life + "FullText", "Full");
            ShowNode(life + "TimerText", false);
            SetText("TopUI/CoinsButton/Button/PressDownTransform/Text", coins.ToString());
            SetText("NavigationBar/Root/ShopButton/Text", "Shop");
            SetText("NavigationBar/Root/LeaderboardButton/Text", "Leaderboard");
            SetText("NavigationBar/Root/HomeButton/Text", "Home");
            SetText("NavigationBar/Root/PacksButton/Text", "Packs");
            SetText("NavigationBar/Root/SettingsButton/Text", "Settings");
        }

        // ================================================================== layout
        /// <summary>Camera size and edge anchors for the current screen (call every frame while visible; cheap).</summary>
        public void Layout(Camera cam)
        {
            Layout(cam, (float)Screen.width / Mathf.Max(1, Screen.height));
        }

        public void Layout(Camera cam, float aspect)
        {
            float halfH = PlaceParts(aspect);
            cam.orthographicSize = halfH;
            cam.transform.position = new Vector3(Origin.x, Origin.y, -10f);
        }
    }
}
