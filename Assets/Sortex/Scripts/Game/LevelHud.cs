// The game's in-level HUD, rebuilt from Sort Express' LevelSceneUI.prefab (see PrefabScreen):
//   top:    LevelNumber ("Level N" on the navy / red / purple plate), the Timer (dark track, green ring that runs
//           down with the time and turns red in the last 30 seconds, cream digits) and the PauseButton
//   bottom: the four boosters (vacuum, magic hat, painter, freeze) with their count badges, on a wooden plank
// The boosters are 3D models in the game; their icons stand in for them here. Tapping one uses it (SortexGame:
// UseBooster; the board side is in Core/BoardBoosters.cs); the freezer shows the game's frozen timer and frost.
using System.Collections.Generic;
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
        static readonly Color Ice = new Color(0.5f, 0.86f, 1f, 1f);
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
            BuildFreeze();
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

        public const float RedSeconds = 30f, WarningSeconds = 10f;

        /// <summary>m:ss in the timer. The green ring around it runs down with the time left (clockwise from the top,
        /// like a clock hand), turns red in the last 30 seconds; the warning glow pulses in the last 10.</summary>
        public void SetTime(float secondsLeft, float totalSeconds, bool running, bool frozen = false)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(secondsLeft));
            if (s != shownSeconds)
            {
                shownSeconds = s;
                SetText(Digits, (s / 60) + ":" + (s % 60).ToString("00"));
            }
            bool red = secondsLeft <= RedSeconds;
            bool low = running && !frozen && secondsLeft <= WarningSeconds;
            float pulse = low ? 0.5f + 0.5f * Mathf.Sin(Time.time * 8f) : 0f;
            var ring = SpriteAt(Timer + "NewTimerSetup/TimerFill");
            if (ring != null)
            {
                float total = Mathf.Max(totalSeconds, secondsLeft, 1f);
                SetRingFill(ring, Mathf.Clamp01(secondsLeft / total), frozen ? Ice : red ? Color.Lerp(Red, Green, 0.12f * pulse) : Green);
            }
            foreach (var side in new[] { "BaseLeft", "BaseRight" })
            {
                var glow = SpriteAt(Timer + "NewTimerSetup/Warning/" + side);
                if (glow != null) glow.color = new Color(1f, 0.1f, 0.05f, low ? 0.35f + 0.55f * pulse : 0f);
            }
        }

        float ringShown = -1f;
        Color ringColor;
        MeshFilter ringMesh;
        readonly List<Vector2> ringPoints = new List<Vector2>();

        /// <summary>
        /// Draws the part of the ring that is still full: the sector from the centre over the outline positions
        /// [1 - fill, 1] of the way round, starting at the top centre and going clockwise. The game fills TimerFill with
        /// a progress shader; here a mesh with the ring's texture takes the place of the sprite (same sorting order).
        /// </summary>
        void SetRingFill(SpriteRenderer ring, float fill, Color color)
        {
            fill = Mathf.Round(fill * 2000f) / 2000f;
            if (ring.sprite == null || Mathf.Approximately(fill, ringShown) && color == ringColor && ringMesh != null) return;
            ringShown = fill;
            ringColor = color;
            if (ringMesh == null)
            {
                var go = new GameObject("RingFill");
                go.transform.SetParent(ring.transform, false);
                ringMesh = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                var baseMat = SortexArt.SpriteMaterial;
                var mat = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Sprites/Default"));
                mat.mainTexture = ring.sprite.texture;
                mr.sharedMaterial = mat;
                mr.sortingLayerID = ring.sortingLayerID;
                mr.sortingOrder = ring.sortingOrder;
                ringMesh.sharedMesh = new Mesh { name = "TimerRingFill" };
            }
            ring.enabled = false;
            ringMesh.gameObject.SetActive(fill > 0f);
            if (fill <= 0f) return;

            var r = ring.sprite.rect;
            float w = r.width, h = r.height;
            float perimeter = 2f * (w + h);
            float a = (1f - fill) * perimeter, b = perimeter;
            float[] corners = { w * 0.5f, w * 0.5f + h, w * 1.5f + h, w * 1.5f + 2f * h };
            ringPoints.Clear();
            ringPoints.Add(new Vector2(w * 0.5f, h * 0.5f));
            ringPoints.Add(OnOutline(a, w, h));
            foreach (float c in corners)
                if (c > a && c < b) ringPoints.Add(OnOutline(c, w, h));
            ringPoints.Add(OnOutline(b, w, h));

            // sprite pixels -> local units around the sprite's pivot, texture coordinates over the whole texture
            float ppu = ring.sprite.pixelsPerUnit;
            var pivot = ring.sprite.pivot;
            var tex = ring.sprite.texture;
            // a SpriteRenderer's colour reaches the shader in linear space in a linear project; a mesh colour does not
            Color c0 = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
            int n = ringPoints.Count;
            var verts = new Vector3[n];
            var uv = new Vector2[n];
            var colors = new Color[n];
            for (int i = 0; i < n; i++)
            {
                var p = ringPoints[i];
                verts[i] = new Vector3((p.x - pivot.x) / ppu, (p.y - pivot.y) / ppu, 0f);
                uv[i] = new Vector2((r.x + p.x) / tex.width, (r.y + p.y) / tex.height);
                colors[i] = c0;
            }
            var tris = new int[(n - 2) * 3];
            for (int i = 0; i < n - 2; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = i + 2;
            }
            var mesh = ringMesh.sharedMesh;
            mesh.Clear();
            mesh.vertices = verts;
            mesh.uv = uv;
            mesh.colors = colors;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        /// <summary>The point at distance d along the sprite's outline, clockwise from the top centre (pixels).</summary>
        static Vector2 OnOutline(float d, float w, float h)
        {
            if (d <= w * 0.5f) return new Vector2(w * 0.5f + d, h);                       // top, right half
            d -= w * 0.5f;
            if (d <= h) return new Vector2(w, h - d);                                     // right side, down
            d -= h;
            if (d <= w) return new Vector2(w - d, 0f);                                    // bottom, to the left
            d -= w;
            if (d <= h) return new Vector2(0f, d);                                        // left side, up
            d -= h;
            return new Vector2(Mathf.Min(w * 0.5f, d), h);                                // top, left half
        }

        /// <summary>The count badges of the four boosters (an empty booster is greyed out).</summary>
        public void SetBoosters(params int[] counts)
        {
            for (int i = 0; i < BoosterButtons.Length && i < counts.Length; i++)
            {
                SetText("BoosterContainer/" + BoosterButtons[i] + "/Badge/Counter/Text", counts[i].ToString());
                var icon = SpriteAt("BoosterContainer/" + BoosterButtons[i] + "/Icon");
                if (icon != null) icon.color = counts[i] > 0 ? Color.white : new Color(0.55f, 0.55f, 0.6f, 0.8f);
            }
        }

        /// <summary>Where a booster stands on screen (the vacuum pulls the items there).</summary>
        public Vector3 BoosterPosition(int index)
        {
            var t = Node("BoosterContainer/" + BoosterButtons[Mathf.Clamp(index, 0, BoosterButtons.Length - 1)]);
            return t != null ? t.position : Vector3.zero;
        }

        // ================================================================== freezer
        // The game's frozen timer (FreezeTimerBackground behind it, the icy FreezeTimerFrame over it) and the frost on the
        // screen edges (FreezeEffectOverlay, the left half; the right half is the same picture mirrored).
        SpriteRenderer freezeBack, freezeFrame, frostLeft, frostRight;

        void BuildFreeze()
        {
            var setup = Node(Timer + "NewTimerSetup");
            var ring = SpriteAt(Timer + "NewTimerSetup/TimerFill");
            if (setup == null || ring == null) return;
            int behind = ring.sortingOrder;
            foreach (var n in new[] { "NewTimerSetup/TimerBase/TimerBaseLeft", "NewTimerSetup/TimerBase/TimerBaseRight", "NewTimerSetup/TimerInside" })
            {
                var sr = SpriteAt(Timer + n);
                if (sr != null) behind = Mathf.Min(behind, sr.sortingOrder);
            }
            freezeBack = MakeSprite("FreezeTimerBackground", setup, 285, 278f, new Vector2(0.5f, 0.5f), Vector4.zero, behind - 1);
            if (freezeBack != null) freezeBack.transform.localScale = new Vector3(1.03f, 1.2f, 1f);
            freezeFrame = MakeSprite("FreezeTimerFrame", setup, 370, 278f, new Vector2(0.5f, 0.5f), Vector4.zero, ring.sortingOrder + 1);
            if (freezeFrame != null)
            {
                freezeFrame.transform.localPosition = new Vector3(0f, -0.006f, 0f);
                freezeFrame.transform.localScale = new Vector3(1.06f, 1.15f, 1f);
            }
            var border = new Vector4(456, 280, 0, 280);
            frostLeft = MakeSprite("FreezeEffectOverlay", root, 472, 300f, new Vector2(1f, 0.5f), border, 990);
            frostRight = MakeSprite("FreezeEffectOverlay", root, 472, 300f, new Vector2(1f, 0.5f), border, 990);
            foreach (var f in new[] { frostLeft, frostRight })
                if (f != null) f.drawMode = SpriteDrawMode.Sliced;
            if (frostRight != null) frostRight.flipX = true;
            SetFreeze(0f, 0.5625f);
        }

        SpriteRenderer MakeSprite(string key, Transform parent, int sourceWidth, float ppu, Vector2 pivot, Vector4 border, int order)
        {
            var tex = Resources.Load<Texture2D>(SortexArt.Root + "Level/" + key);
            if (tex == null) return null;
            tex.wrapMode = TextureWrapMode.Clamp;
            float k = tex.width / (float)sourceWidth;     // Unity may store the PNG resized
            var go = new GameObject(key);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, ppu * k, 0, SpriteMeshType.FullRect, border * k);
            if (SortexArt.SpriteMaterial != null) sr.sharedMaterial = SortexArt.SpriteMaterial;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>How frozen the timer looks (0 = normal, 1 = frozen; in between while it thaws).</summary>
        public void SetFreeze(float amount, float aspect)
        {
            amount = Mathf.Clamp01(amount);
            var c = new Color(1f, 1f, 1f, amount);
            foreach (var sr in new[] { freezeBack, freezeFrame, frostLeft, frostRight })
            {
                if (sr == null) continue;
                sr.enabled = amount > 0.001f;
                sr.color = c;
            }
            float refH = ReferenceHeight(aspect);
            foreach (var f in new[] { frostLeft, frostRight })
            {
                if (f == null) continue;
                f.size = new Vector2(refWidth * 0.5f + 0.02f, refH + 0.1f);
                f.transform.localPosition = Vector3.zero;
            }
        }
    }
}
