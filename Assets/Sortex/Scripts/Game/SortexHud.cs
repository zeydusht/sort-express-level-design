// All screen-space UI (uGUI, built from code): level select, in-game HUD and the result card.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Sortex.Game
{
    public sealed class LevelCardInfo
    {
        public string Title;
        public string Tier;
        public Color Color;
        public string Board;
        public string Mechanics;
        public string Sim;
        public bool Compact;      // a level saved with the Level Editor: one short row
        public Action OnPlay;
    }

    public sealed class SortexHud
    {
        public const float TopHeight = 300f;     // reference pixels (1080 x 1920 canvas)
        public const float BottomHeight = 190f;

        readonly Canvas canvas;
        readonly CanvasScaler scaler;
        readonly RectTransform root;

        GameObject menu, game, result;
        Text levelTitle, levelTier, timerText, progressText, hintText, toastText;
        Image timerPill, progressFill, tierBadge;
        Text resultTitle, resultBody;
        Image resultIcon;
        Button resultPrimary;
        Text resultPrimaryLabel;
        float toastTime;
        Action onMenu, onRetry, onNext;

        public SortexHud(Transform parent)
        {
            var go = new GameObject("HUD");
            go.transform.SetParent(parent, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            go.AddComponent<GraphicRaycaster>();
            root = go.GetComponent<RectTransform>();

            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                var module = es.AddComponent<InputSystemUIInputModule>();
                module.AssignDefaultActions();
            }
            UpdateScaler();
        }

        /// <summary>Pixels taken by the top bar and the bottom hint, so the camera can fit the board between them.</summary>
        public float TopPixels { get { return TopHeight * canvas.scaleFactor; } }
        public float BottomPixels { get { return BottomHeight * canvas.scaleFactor; } }

        public void UpdateScaler()
        {
            // tall phones: fit the width; anything wider than 9:16 (tablets, desktop browsers): fit the height
            scaler.matchWidthOrHeight = Screen.width > Screen.height * 0.5625f ? 1f : 0f;
        }

        // ================================================================== building blocks
        static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static Image Img(Transform parent, string name, Sprite sprite, Color color, bool raycast = false)
        {
            var rt = Node(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            if (sprite != null && sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
            return img;
        }

        static Text Txt(Transform parent, string name, string text, int size, Color color, TextAnchor align)
        {
            var rt = Node(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = SortexArt.Font;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.text = text;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            return t;
        }

        static Button Btn(Transform parent, string label, Color color, Vector2 size, int fontSize, Action onClick, out Text labelText)
        {
            var img = Img(parent, "Button " + label, SortexArt.Button, color, true);
            img.rectTransform.sizeDelta = size;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.highlightedColor = new Color(1.06f, 1.06f, 1.06f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            b.colors = colors;
            b.onClick.AddListener(() => onClick());
            labelText = Txt(img.transform, "Label", label, fontSize, Color.white, TextAnchor.MiddleCenter);
            Stretch(labelText.rectTransform);
            labelText.rectTransform.offsetMin = new Vector2(10f, 8f);
            labelText.rectTransform.offsetMax = new Vector2(-10f, 0f);
            var sh = labelText.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.35f);
            sh.effectDistance = new Vector2(0f, -3f);
            return b;
        }

        static Sprite Icon(string name)
        {
            return SortexArt.Get("UI/" + name, 100f);
        }

        // ================================================================== menu
        public void ShowMenu(List<LevelCardInfo> cards)
        {
            Hide();
            menu = Node("Menu", root).gameObject;
            var rt = menu.GetComponent<RectTransform>();
            Stretch(rt);
            var dim = Img(rt, "Dim", null, new Color(0.04f, 0.06f, 0.12f, 0.55f), true);
            Stretch(dim.rectTransform);

            // scrollable column: the three case levels, then every level saved with the Level Editor
            var scrollRt = Node("Scroll", rt);
            Stretch(scrollRt);
            var scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 40f;
            var viewport = Node("Viewport", scrollRt);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            Stretch(Img(viewport, "Hit", null, new Color(0f, 0f, 0f, 0f), true).rectTransform);   // lets the list be dragged
            var col = Node("Content", viewport);
            col.anchorMin = col.anchorMax = new Vector2(0.5f, 1f);
            col.pivot = new Vector2(0.5f, 1f);
            col.anchoredPosition = Vector2.zero;
            scroll.viewport = viewport;
            scroll.content = col;

            var title = Txt(col, "Title", "SORT EXPRESS", 92, Color.white, TextAnchor.MiddleCenter);
            Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1000f, 120f));
            title.gameObject.AddComponent<Outline>().effectColor = new Color(0.1f, 0.15f, 0.35f, 1f);
            var sub = Txt(col, "Subtitle", "Level Design Case  ·  Easy / Medium / Hard", 40, SortexArt.Orange, TextAnchor.MiddleCenter);
            Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(1000f, 60f));

            float y = -400f;
            bool ownHeader = false;
            foreach (var c in cards)
            {
                if (c.Compact && !ownHeader)
                {
                    ownHeader = true;
                    var h = Txt(col, "MyLevels", "MY LEVELS", 50, Color.white, TextAnchor.MiddleLeft);
                    Place(h.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y + 150f), new Vector2(960f, 70f));
                    h.gameObject.AddComponent<Outline>().effectColor = new Color(0.1f, 0.15f, 0.35f, 1f);
                    y -= 90f;
                }
                float height = c.Compact ? 170f : 360f;
                var card = Img(col, "Card " + c.Title, SortexArt.Panel, new Color(0.10f, 0.14f, 0.26f, 0.96f), true);
                Place(card.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y - height * 0.5f + 180f), new Vector2(980f, height));
                var stripe = Img(card.transform, "Stripe", SortexArt.Panel, c.Color);
                stripe.rectTransform.anchorMin = new Vector2(0f, 0f);
                stripe.rectTransform.anchorMax = new Vector2(0f, 1f);
                stripe.rectTransform.pivot = new Vector2(0f, 0.5f);
                stripe.rectTransform.sizeDelta = new Vector2(26f, -24f);
                stripe.rectTransform.anchoredPosition = new Vector2(12f, 0f);

                Text lbl;
                if (c.Compact)
                {
                    var name = Txt(card.transform, "Name", c.Title, 52, Color.white, TextAnchor.UpperLeft);
                    Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(370f, -50f), new Vector2(640f, 70f));
                    var info = Txt(card.transform, "Info", c.Tier + "  ·  " + c.Board, 28, new Color(0.85f, 0.9f, 1f), TextAnchor.UpperLeft);
                    Place(info.rectTransform, new Vector2(0f, 1f), new Vector2(370f, -112f), new Vector2(640f, 44f));
                    var playSmall = Btn(card.transform, "PLAY", c.Color, new Vector2(200f, 110f), 40, c.OnPlay, out lbl);
                    Place(playSmall.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(-130f, 0f), new Vector2(200f, 110f));
                }
                else
                {
                    var name = Txt(card.transform, "Name", c.Title.ToUpperInvariant(), 64, Color.white, TextAnchor.UpperLeft);
                    Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(330f, -64f), new Vector2(560f, 80f));
                    var tier = Txt(card.transform, "Tier", c.Tier, 34, Color.Lerp(c.Color, Color.white, 0.45f), TextAnchor.UpperLeft);
                    Place(tier.rectTransform, new Vector2(0f, 1f), new Vector2(330f, -128f), new Vector2(560f, 50f));
                    var info = Txt(card.transform, "Info", c.Board + "\n" + c.Mechanics + "\n<color=#9FB6E8>" + c.Sim + "</color>", 30,
                        new Color(0.85f, 0.9f, 1f), TextAnchor.UpperLeft);
                    info.lineSpacing = 1.05f;
                    Place(info.rectTransform, new Vector2(0f, 1f), new Vector2(380f, -238f), new Vector2(660f, 160f));
                    var play = Btn(card.transform, "PLAY", c.Color, new Vector2(230f, 130f), 46, c.OnPlay, out lbl);
                    Place(play.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-150f, -95f), new Vector2(230f, 130f));
                }
                var cardButton = card.gameObject.AddComponent<Button>();
                cardButton.transition = Selectable.Transition.None;
                var onPlay = c.OnPlay;
                cardButton.onClick.AddListener(() => onPlay());
                y -= height + 35f;
            }

            var foot = Txt(col, "Footer",
                (ownHeader ? "" : "Levels you save with Sortex > Level Editor show up here.\n") +
                "Built in Unity from the Sort Express level data (2150 levels analysed).\nEvery case level is checked by a solver and 300 simulated players.",
                28, new Color(0.7f, 0.78f, 0.95f, 0.9f), TextAnchor.MiddleCenter);
            Place(foot.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y + 120f), new Vector2(980f, 150f));
            col.sizeDelta = new Vector2(1000f, -y + 80f);
        }

        // ================================================================== in-game HUD
        public void ShowGame(string title, string tier, Color tierColor, string hint, Action menuAction, Action retryAction)
        {
            Hide();
            onMenu = menuAction;
            onRetry = retryAction;
            game = Node("Game", root).gameObject;
            var rt = game.GetComponent<RectTransform>();
            Stretch(rt);

            // top bar
            var bar = Img(rt, "TopBar", SortexArt.Panel, new Color(0.07f, 0.10f, 0.20f, 0.92f));
            bar.rectTransform.anchorMin = new Vector2(0f, 1f);
            bar.rectTransform.anchorMax = new Vector2(1f, 1f);
            bar.rectTransform.pivot = new Vector2(0.5f, 1f);
            bar.rectTransform.sizeDelta = new Vector2(-24f, TopHeight - 24f);
            bar.rectTransform.anchoredPosition = new Vector2(0f, -12f);

            Text l;
            var menuBtn = Btn(bar.transform, "Menu", SortexArt.Blue, new Vector2(190f, 104f), 38, () => { if (onMenu != null) onMenu(); }, out l);
            Place(menuBtn.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(120f, -76f), new Vector2(190f, 104f));
            var retryBtn = Btn(bar.transform, "Retry", SortexArt.OrangeDark, new Vector2(190f, 104f), 38, () => { if (onRetry != null) onRetry(); }, out l);
            Place(retryBtn.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(-120f, -76f), new Vector2(190f, 104f));

            levelTitle = Txt(bar.transform, "LevelTitle", title.ToUpperInvariant(), 56, Color.white, TextAnchor.MiddleCenter);
            Place(levelTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -54f), new Vector2(520f, 70f));
            tierBadge = Img(bar.transform, "TierBadge", SortexArt.Panel, tierColor);
            Place(tierBadge.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -116f), new Vector2(300f, 50f));
            levelTier = Txt(tierBadge.transform, "Tier", tier, 30, Color.white, TextAnchor.MiddleCenter);
            Stretch(levelTier.rectTransform);
            var skull = Icon("HardLevelTaskIcon");
            if (skull != null && tier != "Normal")
            {
                var sk = Img(tierBadge.transform, "Skull", skull, Color.white);
                Place(sk.rectTransform, new Vector2(0f, 0.5f), new Vector2(-6f, 0f), new Vector2(70f, 70f));
            }

            // timer pill
            timerPill = Img(bar.transform, "Timer", SortexArt.Panel, new Color(0.03f, 0.05f, 0.12f, 1f));
            Place(timerPill.rectTransform, new Vector2(0.5f, 0f), new Vector2(-170f, 62f), new Vector2(300f, 84f));
            var hg = Icon("HourglassIcon128");
            if (hg != null)
            {
                var hgi = Img(timerPill.transform, "Hourglass", hg, Color.white);
                Place(hgi.rectTransform, new Vector2(0f, 0.5f), new Vector2(40f, 0f), new Vector2(84f, 84f));
            }
            timerText = Txt(timerPill.transform, "Time", "0:00", 50, Color.white, TextAnchor.MiddleCenter);
            Stretch(timerText.rectTransform);
            timerText.rectTransform.offsetMin = new Vector2(60f, 0f);

            // progress bar
            var track = Img(bar.transform, "Progress", SortexArt.Panel, new Color(0.03f, 0.05f, 0.12f, 1f));
            Place(track.rectTransform, new Vector2(0.5f, 0f), new Vector2(170f, 62f), new Vector2(340f, 84f));
            progressFill = Img(track.transform, "Fill", SortexArt.Panel, SortexArt.Green);
            progressFill.rectTransform.anchorMin = new Vector2(0f, 0f);
            progressFill.rectTransform.anchorMax = new Vector2(0f, 1f);
            progressFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            progressFill.rectTransform.anchoredPosition = new Vector2(6f, 0f);
            progressFill.rectTransform.sizeDelta = new Vector2(0f, -12f);
            progressText = Txt(track.transform, "Count", "0 / 0", 36, Color.white, TextAnchor.MiddleCenter);
            Stretch(progressText.rectTransform);

            // bottom hint + toast
            hintText = Txt(rt, "Hint", hint, 30, new Color(0.88f, 0.92f, 1f, 0.92f), TextAnchor.MiddleCenter);
            hintText.rectTransform.anchorMin = new Vector2(0f, 0f);
            hintText.rectTransform.anchorMax = new Vector2(1f, 0f);
            hintText.rectTransform.pivot = new Vector2(0.5f, 0f);
            hintText.rectTransform.sizeDelta = new Vector2(-60f, BottomHeight - 30f);
            hintText.rectTransform.anchoredPosition = new Vector2(0f, 18f);
            hintText.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.6f);

            toastText = Txt(rt, "Toast", "", 44, SortexArt.Orange, TextAnchor.MiddleCenter);
            Place(toastText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, BottomHeight + 40f), new Vector2(1000f, 80f));
            toastText.gameObject.AddComponent<Outline>().effectColor = new Color(0.05f, 0.05f, 0.15f, 1f);
        }

        public void SetTimer(float secondsLeft, bool running)
        {
            if (timerText == null) return;
            int s = Mathf.Max(0, Mathf.CeilToInt(secondsLeft));
            timerText.text = (s / 60) + ":" + (s % 60).ToString("00");
            bool low = secondsLeft <= 10f && running;
            float pulse = low ? 0.5f + 0.5f * Mathf.Sin(Time.time * 10f) : 0f;
            timerText.color = Color.Lerp(Color.white, SortexArt.Red, low ? 0.6f + 0.4f * pulse : 0f);
            timerPill.rectTransform.localScale = Vector3.one * (1f + pulse * 0.05f);
        }

        public void SetProgress(int cleared, int total)
        {
            if (progressText == null) return;
            progressText.text = cleared + " / " + total;
            float w = progressFill.transform.parent.GetComponent<RectTransform>().rect.width - 12f;
            float t = total > 0 ? (float)cleared / total : 0f;
            var sd = progressFill.rectTransform.sizeDelta;
            sd.x = Mathf.Lerp(sd.x, Mathf.Max(0f, w * t), 0.2f);
            progressFill.rectTransform.sizeDelta = sd;
        }

        public void Toast(string text, Color color)
        {
            if (toastText == null) return;
            toastText.text = text;
            toastText.color = color;
            toastTime = 1.6f;
        }

        public void Tick(float dt)
        {
            if (toastText == null) return;
            if (toastTime > 0f) toastTime -= dt;
            var c = toastText.color;
            c.a = Mathf.Clamp01(toastTime / 0.4f);
            toastText.color = c;
            toastText.rectTransform.anchoredPosition = new Vector2(0f, BottomHeight + 40f + (1.6f - Mathf.Max(0f, toastTime)) * 30f);
        }

        // ================================================================== result card
        public void ShowResult(bool win, string title, string body, string primary, Action primaryAction,
            Action retryAction, Action menuAction)
        {
            if (result != null) UnityEngine.Object.Destroy(result);
            onNext = primaryAction;
            result = Node("Result", root).gameObject;
            var rt = result.GetComponent<RectTransform>();
            Stretch(rt);
            var dim = Img(rt, "Dim", null, new Color(0.02f, 0.03f, 0.08f, 0.72f), true);
            Stretch(dim.rectTransform);

            var card = Img(rt, "Card", SortexArt.Panel, new Color(0.10f, 0.14f, 0.26f, 0.98f), true);
            Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(900f, 980f));

            if (win)
            {
                var glow = Img(card.transform, "Glow", SortexArt.Glow, new Color(1f, 0.8f, 0.3f, 0.7f));
                Place(glow.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(420f, 420f));
                resultIcon = Img(card.transform, "Star", SortexArt.Get("Art/fx_sparkle", 100f), SortexArt.Orange);
                Place(resultIcon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(260f, 260f));
            }
            else
            {
                resultIcon = Img(card.transform, "Heart", Icon("BrokenHeartLevelEnd"), Color.white);
                Place(resultIcon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(300f, 250f));
                if (resultIcon.sprite == null) resultIcon.enabled = false;
            }

            resultTitle = Txt(card.transform, "Title", title, 76, win ? SortexArt.Orange : Color.white, TextAnchor.MiddleCenter);
            Place(resultTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -390f), new Vector2(860f, 100f));
            resultTitle.gameObject.AddComponent<Outline>().effectColor = new Color(0.05f, 0.05f, 0.15f, 1f);
            resultBody = Txt(card.transform, "Body", body, 36, new Color(0.85f, 0.9f, 1f), TextAnchor.UpperCenter);
            Place(resultBody.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -560f), new Vector2(800f, 220f));

            Text l;
            resultPrimary = Btn(card.transform, primary, win ? SortexArt.Green : SortexArt.OrangeDark, new Vector2(560f, 130f), 50,
                () => { if (onNext != null) onNext(); }, out resultPrimaryLabel);
            Place(resultPrimary.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0f, 230f), new Vector2(560f, 130f));
            if (win)
            {
                var retry = Btn(card.transform, "Retry", SortexArt.OrangeDark, new Vector2(270f, 110f), 40, retryAction, out l);
                Place(retry.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(-145f, 90f), new Vector2(270f, 110f));
                var menuB = Btn(card.transform, "Menu", SortexArt.Blue, new Vector2(270f, 110f), 40, menuAction, out l);
                Place(menuB.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(145f, 90f), new Vector2(270f, 110f));
            }
            else
            {
                var menuB = Btn(card.transform, "Menu", SortexArt.Blue, new Vector2(560f, 110f), 40, menuAction, out l);
                Place(menuB.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0f, 90f), new Vector2(560f, 110f));
            }
        }

        public void Hide()
        {
            if (menu != null) UnityEngine.Object.Destroy(menu);
            if (game != null) UnityEngine.Object.Destroy(game);
            if (result != null) UnityEngine.Object.Destroy(result);
            menu = game = result = null;
            timerText = progressText = hintText = toastText = null;
        }

        public void HideResult()
        {
            if (result != null) UnityEngine.Object.Destroy(result);
            result = null;
        }
    }
}
