// Loads every sprite, font and material of the prototype from Resources and keeps the colour palette.
// Sprites are created from plain textures at runtime (Sprite.Create), so the game does not depend on
// texture import settings: any PNG dropped into Resources/Sortex works.
using System.Collections.Generic;
using UnityEngine;

namespace Sortex.Game
{
    public static class SortexArt
    {
        public const string Root = "Sortex/";
        public const float ItemSize = 0.92f;        // world units an item icon occupies (slot = 0.52 on screen; icons fill ~94% of it, like the game)

        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        static Material spriteMaterial;
        static bool spriteMaterialLoaded;
        static Font font;

        /// <summary>
        /// "Enter Play Mode Options" (Unity 6 default) skips the domain reload, so static fields survive from one
        /// Play to the next while Unity destroys the sprites we created in the previous Play. Start every Play clean.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            cache.Clear();
            spriteMaterial = null;
            spriteMaterialLoaded = false;
            font = null;
            stonePx = questionPx = null;
        }

        // ------------------------------------------------------------------ palette (from the APK art)
        public static readonly Color Navy = Hex("#1B2540");
        public static readonly Color NavyDeep = Hex("#121A2E");
        public static readonly Color Orange = Hex("#ECAF24");
        public static readonly Color OrangeDark = Hex("#DC810F");
        public static readonly Color Blue = Hex("#3D63CD");
        public static readonly Color LightBlue = Hex("#87AFEE");
        public static readonly Color Green = Hex("#4CC46A");
        public static readonly Color Red = Hex("#E2474B");
        public static readonly Color Purple = Hex("#7A45CF");
        public static readonly Color BackLayerTint = new Color(0.33f, 0.38f, 0.52f, 1f);
        public static readonly Color Silhouette = new Color(0.10f, 0.12f, 0.20f, 1f);       // mystery item
        public static readonly Color BackSilhouette = new Color(0.07f, 0.08f, 0.14f, 1f);
        public static readonly Color Cabinet = Hex("#7F9FD3");

        public static Color Hex(string hex)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(hex, out c) ? c : Color.magenta;
        }

        /// <summary>Theme per Sort Express difficulty tag: 0 normal (blue), 10 hard (purple), 20 super hard (red).</summary>
        public static string ThemeName(int difficultyTag)
        {
            if (difficultyTag >= 20) return "red";
            if (difficultyTag >= 10) return "purple";
            return "blue";
        }

        public static Color ThemeColor(int difficultyTag)
        {
            if (difficultyTag >= 20) return Red;
            if (difficultyTag >= 10) return Purple;
            return Blue;
        }

        // ------------------------------------------------------------------ loading
        /// <summary>Loads Resources/Sortex/{path} as a sprite. border = 9-slice border in pixels (l, b, r, t).</summary>
        public static Sprite Get(string path, float pixelsPerUnit = 128f, Vector4 border = default(Vector4))
        {
            string key = path + "|" + pixelsPerUnit + "|" + border;
            Sprite s;
            // a cached sprite can be a destroyed object (Unity "== null" is true for it): rebuild it then
            if (cache.TryGetValue(key, out s) && s != null) return s;
            var tex = Resources.Load<Texture2D>(Root + path);
            if (tex == null) return null;
            tex.wrapMode = TextureWrapMode.Clamp;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), pixelsPerUnit, 0,
                SpriteMeshType.FullRect, border);
            s.name = path;
            cache[key] = s;
            return s;
        }

        public static Sprite Item(string iid)
        {
            // item icons are 128 px; the visible object fills ~90% of the square
            var s = Get("Items/" + iid, 128f / ItemSize);
            return s != null ? s : Get("Art/ui_circle", 128f / (ItemSize * 0.8f));
        }

        public static Sprite ShelfFrame(int difficultyTag)
        {
            return Get("Art/shelf_frame_" + ThemeName(difficultyTag), 128f, new Vector4(24, 26, 24, 20));
        }

        public static Sprite ShelfBack { get { return Get("Art/shelf_back", 128f, new Vector4(22, 22, 22, 36)); } }

        static readonly int[] ShelfWidths = { 1, 3, 4, 5, 6 };

        /// <summary>Slot count of the closest pre-rendered shelf (Tools/render_shelf.py renders the game's BaseShelf prefab).</summary>
        public static int ShelfArtWidth(int slots)
        {
            int best = 3;
            foreach (int w in ShelfWidths) if (Mathf.Abs(w - slots) < Mathf.Abs(best - slots)) best = w;
            return best;
        }

        /// <summary>Inside of the real shelf model (back wall, floor, inner walls): drawn behind the items.</summary>
        public static Sprite ShelfInside(int slots) { return Get("Art/shelf_w" + ShelfArtWidth(slots) + "_back", 128f); }

        /// <summary>Front rim of the real shelf model: drawn over the items.</summary>
        public static Sprite ShelfRim(int slots) { return Get("Art/shelf_w" + ShelfArtWidth(slots) + "_front", 128f); }
        // ------------------------------------------------------------------ mystery items
        // Sort Express draws a mystery item as a grey stone "sculpture": the item's convex hull mesh
        // (item_*_hull.prefab) with MysteryItemTexture and a "?" in front (MysteryItemQuestionMark.prefab).
        // It breaks into stone chunks (MysteryItemParticle) when the item is revealed.
        // Here the hull is computed from the icon's outline and shaded like the 3D hull.
        static Color32[] stonePx, questionPx;
        static int stoneW, stoneH, questionW, questionH;

        /// <summary>The stone version of an item (same size and pivot as Item(iid)).</summary>
        public static Sprite MysteryItem(string iid)
        {
            string key = "mystery|" + iid;
            Sprite s;
            if (cache.TryGetValue(key, out s) && s != null) return s;
            var src = Resources.Load<Texture2D>(Root + "Items/" + iid);
            var tex = MysteryTexture(src);
            if (tex == null) return Item(iid);
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width / ItemSize, 0,
                SpriteMeshType.FullRect);
            s.name = key;
            cache[key] = s;
            return s;
        }

        public static Sprite StoneChunk(int i) { return Get("Art/stone_chunk_" + (i % 5), 64f / 0.24f); }

        // ------------------------------------------------------------------ newer obstacles (cut from the game's tutorial pictures)
        /// <summary>The sprite for any board item: normal icon, mystery stone, piñata (by colour) or dragon egg.</summary>
        public static Sprite ItemSprite(Sortex.Core.Item it)
        {
            if (it.Kind == Sortex.Core.ItemKind.Pinata) return Pinata(it.PinataColor);
            if (it.Kind == Sortex.Core.ItemKind.Egg) return Egg(it.Type);
            return it.Mystery ? MysteryItem(it.Type) : Item(it.Type);
        }

        public static Sprite Pinata(int color)
        {
            var s = Get("Art/pinata_" + Mathf.Clamp(color, 0, 5), 128f / ItemSize);
            return s != null ? s : Circle;
        }

        public static Sprite Egg(string iid)
        {
            var s = Get("Art/" + (string.IsNullOrEmpty(iid) ? "dragon_egg_0" : iid), 128f / ItemSize);
            if (s == null) s = Get("Art/dragon_egg_0", 128f / ItemSize);
            return s != null ? s : Circle;
        }

        public static Sprite FerrisDoor { get { return Get("Art/ferris_door", 128f); } }
        public static Sprite FerrisLamp(int state) { return Get(state == 0 ? "Art/ferris_lamp_on" : state == 1 ? "Art/ferris_lamp_off" : "Art/ferris_lamp_broken", 128f); }
        public static Sprite IrisClosed { get { return Get("Art/iris_closed", 128f); } }
        public static Sprite IrisFrame { get { return Get("Art/iris_frame", 128f); } }
        public static Sprite FuseFrame { get { return Get("Art/fuse_frame", 128f); } }
        public static Sprite FuseBolt { get { return Get("Art/fuse_bolt", 128f); } }
        public static Sprite FuseWindow(bool done) { return Get(done ? "Art/fuse_window_done" : "Art/fuse_window", 128f); }

        /// <summary>Scales a sprite renderer so the sprite covers w x h world units.</summary>
        public static void Fit(SpriteRenderer sr, float w, float h)
        {
            if (sr == null || sr.sprite == null) return;
            var size = sr.sprite.bounds.size;
            sr.transform.localScale = new Vector3(w / Mathf.Max(0.001f, size.x), h / Mathf.Max(0.001f, size.y), 1f);
        }
        public static Sprite PipeBody { get { return Get("Art/pipe_body", 128f); } }
        public static Sprite PipeMouth { get { return Get("Art/pipe_mouth", 128f); } }

        /// <summary>Copies any texture (compressed, not readable) into a pixel array through the GPU.</summary>
        public static Color32[] ReadPixels(Texture tex, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            copy.Apply(false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            var px = copy.GetPixels32();
            Object.DestroyImmediate(copy);
            return px;
        }

        /// <summary>Builds the stone sculpture texture for an item icon (also used by the Level Editor).</summary>
        public static Texture2D MysteryTexture(Texture2D item)
        {
            if (item == null) return null;
            if (stonePx == null)
            {
                var st = Resources.Load<Texture2D>(Root + "Art/mystery_stone");
                if (st == null) return null;
                stoneW = st.width;
                stoneH = st.height;
                stonePx = ReadPixels(st, stoneW, stoneH);
            }
            if (questionPx == null)
            {
                var q = Resources.Load<Texture2D>(Root + "Art/mystery_question");
                if (q != null)
                {
                    questionW = q.width;
                    questionH = q.height;
                    questionPx = ReadPixels(q, questionW, questionH);
                }
            }
            int W = item.width, H = item.height;
            var src = ReadPixels(item, W, H);

            // outline points (leftmost / rightmost opaque pixel of every line) -> convex hull
            var pts = new List<Vector2>();
            for (int y = 0; y < H; y++)
            {
                int min = -1, max = -1;
                for (int x = 0; x < W; x++)
                {
                    if (src[y * W + x].a < 128) continue;
                    if (min < 0) min = x;
                    max = x;
                }
                if (min < 0) continue;
                pts.Add(new Vector2(min, y)); pts.Add(new Vector2(min, y + 1));
                pts.Add(new Vector2(max + 1, y)); pts.Add(new Vector2(max + 1, y + 1));
            }
            var hull = ConvexHull(pts);
            if (hull.Count < 3) return null;
            var centre = Vector2.zero;
            foreach (var p in hull) centre += p;
            centre /= hull.Count;

            int n = hull.Count;
            var inward = new Vector2[n];
            var offset = new float[n];
            for (int i = 0; i < n; i++)
            {
                var a = hull[i];
                var e = hull[(i + 1) % n] - a;
                var nrm = new Vector2(-e.y, e.x).normalized;
                if (Vector2.Dot(centre - a, nrm) < 0f) nrm = -nrm;
                inward[i] = nrm;
                offset[i] = Vector2.Dot(a, nrm);
            }

            var light = new Vector2(-0.55f, 0.83f);       // from the top left (pixel rows go up)
            float bevelWidth = W * 0.10f;
            var tint = new Vector3(0.60f, 0.61f, 0.68f);
            var outPx = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float dist = float.MaxValue;
                    int edge = 0;
                    for (int i = 0; i < n; i++)
                    {
                        float d = Vector2.Dot(p, inward[i]) - offset[i];
                        if (d < dist) { dist = d; edge = i; }
                    }
                    float alpha = Mathf.Clamp01(dist + 0.5f);
                    if (alpha <= 0f) continue;
                    float bevel = Mathf.Clamp01(dist / bevelWidth);
                    float facing = Vector2.Dot(-inward[edge], light);
                    float lit = 0.82f + 0.18f * bevel + Mathf.Pow(1f - bevel, 1.5f) * (0.42f * facing - 0.12f);
                    var sc = stonePx[(y * 2 % stoneH) * stoneW + (x * 2 % stoneW)];
                    float stone = (sc.r + sc.g + sc.b) / (3f * 255f);
                    float k = (0.62f + 0.62f * stone) * lit;
                    outPx[y * W + x] = new Color32(
                        (byte)Mathf.Clamp(tint.x * k * 255f, 0, 255), (byte)Mathf.Clamp(tint.y * k * 255f, 0, 255),
                        (byte)Mathf.Clamp(tint.z * k * 255f, 0, 255), (byte)(alpha * 255f));
                }

            // the question mark, centred on the hull
            if (questionPx != null)
            {
                int qs = Mathf.RoundToInt(W * 0.44f);
                float qx0 = centre.x - qs * 0.5f, qy0 = centre.y - qs * 0.5f - H * 0.02f;
                for (int y = 0; y < qs; y++)
                    for (int x = 0; x < qs; x++)
                    {
                        int px = Mathf.RoundToInt(qx0) + x, py = Mathf.RoundToInt(qy0) + y;
                        if (px < 0 || py < 0 || px >= W || py >= H) continue;
                        var q = SampleBilinear(questionPx, questionW, questionH, (x + 0.5f) / qs, (y + 0.5f) / qs);
                        if (q.a <= 0f) continue;
                        int idx = py * W + px;
                        Color dst = outPx[idx];
                        var c = Color.Lerp(dst, new Color(q.r, q.g, q.b, 1f), q.a);
                        c.a = dst.a;
                        outPx[idx] = c;
                    }
            }

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(outPx);
            tex.Apply(false);
            tex.name = "mystery " + item.name;
            return tex;
        }

        static Color SampleBilinear(Color32[] px, int w, int h, float u, float v)
        {
            float fx = Mathf.Clamp(u * w - 0.5f, 0f, w - 1f), fy = Mathf.Clamp(v * h - 0.5f, 0f, h - 1f);
            int x0 = (int)fx, y0 = (int)fy, x1 = Mathf.Min(x0 + 1, w - 1), y1 = Mathf.Min(y0 + 1, h - 1);
            float tx = fx - x0, ty = fy - y0;
            Color a = px[y0 * w + x0], b = px[y0 * w + x1], c = px[y1 * w + x0], d = px[y1 * w + x1];
            return Color.Lerp(Color.Lerp(a, b, tx), Color.Lerp(c, d, tx), ty);
        }

        /// <summary>Andrew's monotone chain.</summary>
        static List<Vector2> ConvexHull(List<Vector2> pts)
        {
            pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var hull = new List<Vector2>();
            if (pts.Count < 3) return hull;
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int i = 0; i < pts.Count; i++)
                {
                    var p = pass == 0 ? pts[i] : pts[pts.Count - 1 - i];
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b)
        {
            return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        }

        public static Sprite LockDoor { get { return Get("Art/lock_door", 256f); } }
        public static Sprite Ice { get { return Get("Art/frozen_ice", 128f / ItemSize); } }
        public static Sprite ShutterSprite { get { return Get("Art/shutter", 256f); } }
        public static Sprite CargoDoor { get { return Get("Art/cargo_door", 256f); } }
        public static Sprite BackgroundGradient { get { return Get("Art/bg_gradient", 64f); } }
        public static Sprite Shadow { get { return Get("Art/item_shadow", 110f); } }
        public static Sprite SlotGlow { get { return Get("Art/slot_glow", 96f, new Vector4(32, 32, 32, 32)); } }
        public static Sprite Glow { get { return Get("Art/fx_glow", 128f); } }
        public static Sprite Sparkle { get { return Get("Art/fx_sparkle", 128f); } }
        public static Sprite Circle { get { return Get("Art/ui_circle", 128f); } }
        public static Sprite Panel { get { return Get("Art/ui_panel", 96f, new Vector4(34, 34, 34, 34)); } }
        public static Sprite Button { get { return Get("Art/ui_button", 96f, new Vector4(34, 38, 34, 34)); } }

        /// <summary>Unlit sprite material created by the editor setup (keeps the art independent of 2D lights).</summary>
        public static Material SpriteMaterial
        {
            get
            {
                if (!spriteMaterialLoaded || spriteMaterial == null)
                {
                    spriteMaterial = Resources.Load<Material>(Root + "SpriteUnlit");
                    spriteMaterialLoaded = true;
                }
                return spriteMaterial;
            }
        }

        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.Load<Font>(Root + "Fonts/Poppins-Bold");
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        // ------------------------------------------------------------------ helpers
        public static SpriteRenderer NewSprite(string name, Transform parent, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            if (SpriteMaterial != null) sr.sharedMaterial = SpriteMaterial;
            return sr;
        }

        public static SpriteRenderer NewSliced(string name, Transform parent, Sprite sprite, Vector2 size, int order)
        {
            var sr = NewSprite(name, parent, sprite, order);
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = size;
            return sr;
        }

        public static TextMesh NewText(string name, Transform parent, string text, float height, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var tm = go.AddComponent<TextMesh>();
            tm.font = Font;
            tm.fontSize = 96;
            tm.characterSize = height / 9.6f * 1.35f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            tm.text = text;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = tm.font.material;
            mr.sortingOrder = order;
            return tm;
        }
    }
}
