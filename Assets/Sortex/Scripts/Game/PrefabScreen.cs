// A screen of Sort Express rebuilt from one of the game's prefabs (Tools/make_main_menu.py):
//   Menu/menu.json    the home screen (MainScene.prefab)          -> MainMenuView
//   Level/level.json  the level HUD (LevelSceneUI.prefab)         -> LevelHud
//   Level/pause.json  the pause dialog (LevelPauseDialog.prefab)  -> PauseDialog
//   Level/win.json, nomore.json, lose.json   the level end dialogs -> LevelEndDialogs.cs
//
// The json keeps every GameObject's local position / scale / rotation, its SpriteRenderer (sprite, 9-slice size,
// flip, colour, sorting order) and SortingGroup, and the TextMeshPro boxes with the face / outline colours of their
// materials. The sprites are cut from the game's atlases. This class rebuilds that hierarchy 1:1 with
// SpriteRenderers + SortingGroups, draws the texts with the game's font (Bango Pro), and handles the buttons
// (press-down animation, action on release).
//
// Screen: the game's UI is laid out on a 4.6166 x 10 reference screen (its menu pages sit 4.6166 world units
// apart, i.e. the camera always shows that width). Parts keep their distance to the screen edge they are anchored
// to (top / bottom / center).
using System;
using System.Collections.Generic;
using Sortex.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sortex.Game
{
    public class PrefabScreen
    {
        sealed class Part
        {
            public string Anchor;
            public float Offset;
            public Transform Root;
            public float RootX;
        }

        sealed class TextBox
        {
            public Transform Node;
            public Vector2 Size;
            public string Style;
            public bool Left;              // left-aligned in its box (the game's texts are centred; the level list's are not)
            public int Order;
            public TextMesh Face;
            public readonly List<TextMesh> Layers = new List<TextMesh>();   // outline / underlay copies
            public string Value;
        }

        // One thing Unity sorts inside a sorting group: a SpriteRenderer, a text box (its outline / underlay / face
        // layers) or a nested SortingGroup. The original relies on the hierarchy order for equal sorting orders
        // (one atlas, one material); here every sprite has its own texture, so Unity would break those ties by
        // material instead. Build() therefore ranks the members of each group by (layer, order, hierarchy order).
        sealed class SortEntry
        {
            public Transform Scope;        // the sorting group this entry is sorted in (null = the menu root)
            public int Layer, Order, Seq;
            public SortingGroup Group;
            public SpriteRenderer Sprite;
            public TextBox Text;
        }

        sealed class MenuButton
        {
            public string Name;
            public Transform Root;
            public Transform Press;
            public Action Action;
        }

        protected readonly Transform root;
        readonly string folder;                // Resources/Sortex/<folder>: the json and its sprites
        readonly int baseOrder;                // first sorting order of this screen (screens drawn over each other)
        readonly HashSet<Transform> containers = new HashSet<Transform>();
        readonly Dictionary<string, float> textScale = new Dictionary<string, float>();
        protected Color textTint = Color.white; // vertex colour of the game's TextMeshPro texts (multiplies the face)
        readonly Dictionary<string, Part> parts = new Dictionary<string, Part>();
        readonly Dictionary<string, Transform> nodes = new Dictionary<string, Transform>();
        readonly Dictionary<string, TextBox> texts = new Dictionary<string, TextBox>();
        readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        readonly Dictionary<string, Dictionary<string, object>> styles = new Dictionary<string, Dictionary<string, object>>();
        readonly List<MenuButton> buttons = new List<MenuButton>();
        readonly List<SortEntry> sorting = new List<SortEntry>();
        protected SpriteRenderer background;
        protected float refWidth = 4.616601f, refHeight = 10f;
        Font font;
        Dictionary<string, object> spriteTable;
        MenuButton pressed;
        float pressAnim;

        public bool Ready { get; private set; }

        /// <param name="resource">json under Resources/Sortex, e.g. "Menu/menu"</param>
        public PrefabScreen(Transform parent, string name, string resource, int baseOrder)
        {
            this.baseOrder = baseOrder;
            folder = resource.Substring(0, resource.LastIndexOf('/') + 1);
            root = new GameObject(name).transform;
            root.SetParent(parent, false);
            var asset = Resources.Load<TextAsset>(SortexArt.Root + resource);
            if (asset == null)
            {
                Debug.LogWarning("Sortex: Resources/Sortex/" + resource + ".json is missing (Tools/make_main_menu.py).");
                return;
            }
            font = Resources.Load<Font>(SortexArt.Root + "Fonts/BangoPro");
            if (font == null) font = SortexArt.Font;
            Build(MiniJson.Parse(asset.text) as Dictionary<string, object>);
            Ready = true;
        }

        // ================================================================== build
        void Build(Dictionary<string, object> data)
        {
            refWidth = (float)MiniJson.Num(data, "refWidth", refWidth);
            refHeight = (float)MiniJson.Num(data, "refHeight", refHeight);
            spriteTable = MiniJson.Obj(data, "sprites");
            var styleTable = MiniJson.Obj(data, "styles");
            if (styleTable != null)
                foreach (var kv in styleTable)
                    styles[kv.Key] = kv.Value as Dictionary<string, object>;

            var partTable = MiniJson.Obj(data, "parts");
            var containers = new Dictionary<string, Transform>();
            foreach (var kv in partTable)
            {
                var pd = kv.Value as Dictionary<string, object>;
                // the HomePage parts share one sorting group (MainMenuContentContainer); the navigation bar draws above it
                Transform partParent = root;
                var container = MiniJson.Obj(pd, "container");
                if (container != null)
                {
                    string cname = MiniJson.Str(container, "name", "Container");
                    if (!containers.TryGetValue(cname, out partParent))
                    {
                        partParent = new GameObject(cname).transform;
                        partParent.SetParent(root, false);
                        this.containers.Add(partParent);
                        var cg = MiniJson.Obj(container, "group");
                        if (cg != null) AddEntry(partParent, cg, partParent.gameObject.AddComponent<SortingGroup>(), null, null);
                        containers[cname] = partParent;
                    }
                }
                var list = MiniJson.Arr(pd, "nodes");
                var made = new List<Transform>();
                var part = new Part { Anchor = MiniJson.Str(pd, "anchor", "center"), Offset = (float)MiniJson.Num(pd, "offset") };
                for (int i = 0; i < list.Count; i++)
                {
                    var n = list[i] as Dictionary<string, object>;
                    int parentIndex = MiniJson.Int(n, "parent", -1);
                    var go = new GameObject(MiniJson.Str(n, "name", "Node"));
                    var t = go.transform;
                    t.SetParent(parentIndex >= 0 ? made[parentIndex] : partParent, false);
                    var pos = MiniJson.Arr(n, "pos");
                    var scale = MiniJson.Arr(n, "scale");
                    // z only orders things inside a sorting group in the original; keep it tiny so the camera sees everything
                    t.localPosition = new Vector3(F(pos, 0), F(pos, 1), F(pos, 2) * 0.01f);
                    t.localScale = new Vector3(F(scale, 0, 1f), F(scale, 1, 1f), 1f);
                    t.localRotation = Quaternion.Euler(0f, 0f, (float)MiniJson.Num(n, "rot"));
                    go.SetActive(MiniJson.Bool(n, "active", true));
                    made.Add(t);
                    string path = parentIndex >= 0 ? PathOf(made[parentIndex]) + "/" + go.name : go.name;
                    nodes[path] = t;

                    var group = MiniJson.Obj(n, "group");
                    if (group != null) AddEntry(t, group, go.AddComponent<SortingGroup>(), null, null);
                    var sprite = MiniJson.Obj(n, "sprite");
                    if (sprite != null)
                    {
                        var sr = AddSprite(go, sprite, spriteTable);
                        if (sr != null) AddEntry(t, sprite, null, sr, null);
                    }
                    var text = MiniJson.Obj(n, "text");
                    if (text != null)
                    {
                        var box = new TextBox
                        {
                            Node = t,
                            Size = new Vector2(F(MiniJson.Arr(text, "size"), 0), F(MiniJson.Arr(text, "size"), 1)),
                            Style = MiniJson.Str(text, "style", "Default"),
                            Left = MiniJson.Str(text, "align", "center") == "left"
                        };
                        texts[path] = box;
                        AddEntry(t, text, null, null, box);
                        var anchored = MiniJson.Arr(text, "anchored");
                        t.localPosition = new Vector3(F(anchored, 0), F(anchored, 1), t.localPosition.z);
                    }
                }
                part.Root = made[0];
                part.RootX = made[0].localPosition.x;
                parts[kv.Key] = part;
            }
            ApplySorting();
            // names in the prefab start at the part roots, e.g. "LoadLevelButton/Root/Button/..."
            DefineButtons();
        }

        /// <summary>The screen's buttons: AddButton(name, root path, press-down path).</summary>
        protected virtual void DefineButtons() { }

        string PathOf(Transform t)
        {
            string p = t.name;
            for (var x = t.parent; x != null && x != root && !containers.Contains(x); x = x.parent) p = x.name + "/" + p;
            return p;
        }

        static float F(List<object> list, int i, float fallback = 0f)
        {
            if (list == null || i >= list.Count) return fallback;
            return Convert.ToSingle(list[i], System.Globalization.CultureInfo.InvariantCulture);
        }

        void AddEntry(Transform node, Dictionary<string, object> d, SortingGroup group, SpriteRenderer sprite, TextBox text)
        {
            // a renderer belongs to the sorting group on its own GameObject; a group is sorted in the one above it
            Transform scope = group != null ? node.parent : node;
            while (scope != null && scope != root && scope.GetComponent<SortingGroup>() == null) scope = scope.parent;
            if (scope == root) scope = null;
            sorting.Add(new SortEntry
            {
                Scope = scope,
                Layer = MiniJson.Int(d, "layer"),
                Order = MiniJson.Int(d, "order"),
                Seq = sorting.Count,
                Group = group,
                Sprite = sprite,
                Text = text
            });
        }

        // Puts everything in the order the original draws it: inside each sorting group by (sorting layer, order in
        // layer, hierarchy order), a nested group as one block at its own place. The result is flattened into one
        // global order (4 steps each: sprites and groups use the first, texts the next three for underlay, outline
        // and face), so the picture is the same whether Unity applies the nested SortingGroups yet or not (a group
        // created in the same frame is not always registered before the first render).
        void ApplySorting()
        {
            var groupOf = new Dictionary<Transform, SortEntry>();
            foreach (var e in sorting)
                if (e.Group != null) groupOf[e.Group.transform] = e;
            var chains = new Dictionary<SortEntry, List<SortEntry>>();
            foreach (var e in sorting)
            {
                var chain = new List<SortEntry> { e };
                SortEntry g;
                for (var s = e.Scope; s != null && groupOf.TryGetValue(s, out g) && chain.Count < 64; s = g.Scope)
                    chain.Insert(0, g);
                chains[e] = chain;
            }
            var flat = new List<SortEntry>(sorting);
            flat.Sort((a, b) =>
            {
                var ca = chains[a];
                var cb = chains[b];
                for (int i = 0; i < ca.Count && i < cb.Count; i++)
                {
                    var x = ca[i];
                    var y = cb[i];
                    if (x == y) continue;
                    if (x.Layer != y.Layer) return x.Layer.CompareTo(y.Layer);
                    if (x.Order != y.Order) return x.Order.CompareTo(y.Order);
                    return x.Seq.CompareTo(y.Seq);
                }
                return ca.Count.CompareTo(cb.Count);   // a group comes before what is inside it
            });
            for (int i = 0; i < flat.Count; i++)
            {
                var e = flat[i];
                if (e.Group != null) e.Group.sortingOrder = baseOrder + i * 4;
                if (e.Sprite != null) e.Sprite.sortingOrder = baseOrder + i * 4;
                if (e.Text != null) e.Text.Order = baseOrder + i * 4;
            }
        }

        SpriteRenderer AddSprite(GameObject go, Dictionary<string, object> s, Dictionary<string, object> table)
        {
            string key = MiniJson.Str(s, "key");
            var sprite = LoadSprite(key, table);
            if (sprite == null) return null;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            if (SortexArt.SpriteMaterial != null) sr.sharedMaterial = SortexArt.SpriteMaterial;
            var c = MiniJson.Arr(s, "color");
            sr.color = new Color(F(c, 0, 1), F(c, 1, 1), F(c, 2, 1), F(c, 3, 1));
            sr.flipX = MiniJson.Bool(s, "flipX");
            sr.flipY = MiniJson.Bool(s, "flipY");
            int mode = MiniJson.Int(s, "drawMode");
            if (mode == 1 || mode == 2)
            {
                sr.drawMode = mode == 1 ? SpriteDrawMode.Sliced : SpriteDrawMode.Tiled;
                var size = MiniJson.Arr(s, "size");
                sr.size = new Vector2(F(size, 0), F(size, 1));
            }
            sr.enabled = MiniJson.Bool(s, "enabled", true);
            if (key == "MainSceneBackground") background = sr;
            return sr;
        }

        Sprite LoadSprite(string key, Dictionary<string, object> table)
        {
            if (string.IsNullOrEmpty(key)) return null;
            Sprite s;
            if (sprites.TryGetValue(key, out s)) return s;
            var meta = MiniJson.Obj(table, key);
            var tex = Resources.Load<Texture2D>(SortexArt.Root + folder + key);
            if (meta == null || tex == null)
            {
                Debug.LogWarning("Sortex: sprite " + folder + key + " is missing");
                sprites[key] = null;
                return null;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            var pivot = MiniJson.Arr(meta, "pivot");
            var border = MiniJson.Arr(meta, "border");
            // the PNG is cut at the sprite's pixel size; Unity may store it resized, so map the original pixels
            float sx = tex.width / (float)MiniJson.Num(meta, "width", tex.width);
            float sy = tex.height / (float)MiniJson.Num(meta, "height", tex.height);
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(F(pivot, 0, 0.5f), F(pivot, 1, 0.5f)),
                (float)MiniJson.Num(meta, "ppu", 100) * sx, 0, SpriteMeshType.FullRect,
                new Vector4(F(border, 0) * sx, F(border, 1) * sy, F(border, 2) * sx, F(border, 3) * sy));
            s.name = key;
            sprites[key] = s;
            return s;
        }

        // ================================================================== texts
        /// <summary>Sets a text of the menu by its path in the prefab (from the part root), e.g. "TopUI/CoinsButton/Button/PressDownTransform/Text".</summary>
        public void SetText(string path, string value)
        {
            TextBox box;
            if (!texts.TryGetValue(path, out box)) return;
            if (box.Value == value && (box.Face != null || string.IsNullOrEmpty(value))) return;
            box.Value = value;
            foreach (var l in box.Layers) Kill(l.gameObject);
            box.Layers.Clear();
            if (box.Face != null) Kill(box.Face.gameObject);
            box.Face = null;
            if (string.IsNullOrEmpty(value)) return;

            var style = StyleOf(box.Style);
            Color face = ColorOf(style, "face", Color.white) * textTint;
            Color outline = ColorOf(style, "outline", Color.clear);
            Color underlay = ColorOf(style, "underlay", Color.clear);
            float outlineWidth = (float)MiniJson.Num(style, "outlineWidth");
            if (box.Node.name == "ShadowText")
            {
                // ShadowText sits under BaseText, a little lower: the dark copy that gives the button text its depth.
                // It takes the outline colour of its BaseText (green, red for Hard, purple for Super Hard), darkened.
                TextBox sibling;
                string basePath = path.Substring(0, path.LastIndexOf('/')) + "/BaseText";
                var baseStyle = texts.TryGetValue(basePath, out sibling) ? StyleOf(sibling.Style) : style;
                Color dark = ColorOf(baseStyle, "outline", new Color(0.08f, 0.55f, 0.06f)) * 0.7f;
                dark.a = 1f;
                face = outline = dark;
                outlineWidth = (float)MiniJson.Num(baseStyle, "outlineWidth", 0.2);
                underlay = Color.clear;
            }
            float em = Fit(box, value);
            float layerRing = 0f;
            int layer = TitleLayer(path);
            if (layer > 0)
            {
                // The dialogs' big titles stack four copies of the text (Bottom2, Bottom1, Center, Main): the three
                // under Main use a fat (dilated) white material and are tinted from code. Here: a white rim (Center),
                // the header's outline colour (Bottom1) and its darker underlay colour as the drop (Bottom2).
                TextBox main;
                var mainStyle = texts.TryGetValue(path.Substring(0, path.LastIndexOf('/')) + "/Main", out main) ? StyleOf(main.Style) : style;
                Color o = ColorOf(mainStyle, "outline", new Color(0.06f, 0.24f, 0.67f));
                Color u = ColorOf(mainStyle, "underlay", o * 0.8f);
                face = layer == 3 ? Color.white : layer == 2 ? o : new Color(u.r * 0.8f, u.g * 0.8f, u.b * 0.8f, 1f);
                outline = face;
                underlay = Color.clear;
                layerRing = em * 0.085f;
                outlineWidth = 0f;
            }

            if (layer > 0)
            {
                box.Face = MakeText(box, "Face", value, face, 3, Vector2.zero, em);
                AddRing(box, value, face, Vector2.zero, layerRing, 2, em);
                return;
            }
            box.Face = MakeText(box, "Face", value, face, 3, Vector2.zero, em);
            float stroke = outline.a > 0f ? em * outlineWidth * 0.35f : 0f;
            if (underlay.a > 0f)
            {
                var uo = MiniJson.Arr(style, "underlayOffset");
                AddRing(box, value, underlay, new Vector2(F(uo, 0), F(uo, 1)) * em * 0.08f, stroke, 1, em);
            }
            if (stroke > 0f) AddRing(box, value, outline, Vector2.zero, stroke, 2, em);
        }

        /// <summary>3 = Center, 2 = Bottom1, 1 = Bottom2 of a layered dialog title (a sibling "Main" exists); else 0.</summary>
        int TitleLayer(string path)
        {
            int cut = path.LastIndexOf('/');
            if (cut < 0) return 0;
            string name = path.Substring(cut + 1);
            int layer = name == "Center" ? 3 : name == "Bottom1" ? 2 : name == "Bottom2" ? 1 : 0;
            return layer > 0 && texts.ContainsKey(path.Substring(0, cut) + "/Main") ? layer : 0;
        }

        static void Kill(GameObject go)
        {
            // the editor's snapshot tool builds screens outside Play mode, where only DestroyImmediate works
            if (Application.isPlaying) UnityEngine.Object.Destroy(go);
            else UnityEngine.Object.DestroyImmediate(go);
        }

        Dictionary<string, object> StyleOf(string name)
        {
            Dictionary<string, object> style;
            if (name != null && styles.TryGetValue(name, out style) && style != null) return style;
            styles.TryGetValue("Default", out style);
            return style;
        }

        const int FontPixels = 64;

        /// <summary>
        /// Em size (local units) that puts the text in its box like TextMeshPro's auto size: 95% of the box height,
        /// shrunk until the line fits 98% of the width. TextMesh: one em = fontSize * characterSize / 10.
        /// </summary>
        float Fit(TextBox box, string value)
        {
            float k;
            // several lines (a long level name) share the box height
            var lines = value.Split('\n');
            float em = box.Size.y * 0.95f * (textScale.TryGetValue(PathOf(box.Node), out k) ? k : 1f);
            if (lines.Length > 1) em /= lines.Length * 1.1f;
            float widest = 0f;
            foreach (var line in lines) widest = Mathf.Max(widest, EmWidth(line));
            float width = widest * em;
            if (width > box.Size.x * 0.98f && width > 0f) em *= box.Size.x * 0.98f / width;
            return em;
        }

        /// <summary>Width of one line of text in the game's font, in ems.</summary>
        protected float EmWidth(string line)
        {
            if (font == null || string.IsNullOrEmpty(line)) return 0f;
            font.RequestCharactersInTexture(line, FontPixels);
            float advance = 0f;
            foreach (char ch in line)
            {
                CharacterInfo ci;
                if (font.GetCharacterInfo(ch, out ci, FontPixels)) advance += ci.advance;
            }
            return advance / FontPixels;
        }

        TextMesh MakeText(TextBox box, string name, string value, Color color, int orderOffset, Vector2 offset, float em)
        {
            var go = new GameObject(name);
            go.transform.SetParent(box.Node, false);
            go.transform.localPosition = new Vector3(offset.x - (box.Left ? box.Size.x * 0.5f : 0f), offset.y, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.fontSize = FontPixels;
            tm.characterSize = em * 10f / FontPixels;
            tm.anchor = box.Left ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            tm.alignment = box.Left ? TextAlignment.Left : TextAlignment.Center;
            // TextMesh passes its colour to the shader as is: in a linear-colour project it has to be linearised first,
            // or every text comes out lighter than the TextMeshPro colours of the game (navy "Full" turns light blue)
            tm.color = QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
            tm.text = value;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = font.material;
            mr.sortingOrder = box.Order + orderOffset;
            return tm;
        }

        void AddRing(TextBox box, string value, Color color, Vector2 center, float radius, int orderOffset, float em)
        {
            const int steps = 12;
            for (int i = 0; i < steps; i++)
            {
                float a = i * Mathf.PI * 2f / steps;
                box.Layers.Add(MakeText(box, "Outline", value, color, orderOffset,
                    center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, em));
            }
            if (center != Vector2.zero) box.Layers.Add(MakeText(box, "Underlay", value, color, orderOffset, center, em));
        }

        static Color ColorOf(Dictionary<string, object> style, string key, Color fallback)
        {
            var c = MiniJson.Arr(style, key);
            if (c == null || c.Count < 3) return fallback;
            return new Color(F(c, 0), F(c, 1), F(c, 2), F(c, 3, 1f));
        }

        /// <summary>Gives a text another of the screen's styles (e.g. the Play button's green / red / purple outline).</summary>
        protected void SetStyle(string path, string style)
        {
            TextBox box;
            if (!texts.TryGetValue(path, out box) || box.Style == style) return;
            box.Style = style;
            string v = box.Value;
            box.Value = null;
            if (v != null) SetText(path, v);
        }

        public void SetSprite(string path, string key)
        {
            Transform t;
            if (!nodes.TryGetValue(path, out t)) return;
            var sr = t.GetComponent<SpriteRenderer>();
            var sprite = LoadSprite(key, spriteTable);
            if (sr != null && sprite != null) sr.sprite = sprite;
        }

        public void ShowNode(string path, bool visible)
        {
            Transform t;
            if (nodes.TryGetValue(path, out t)) t.gameObject.SetActive(visible);
        }

        // ================================================================== buttons
        protected void AddButton(string name, string rootPath, string pressPath)
        {
            Transform r, p = null;
            if (!nodes.TryGetValue(rootPath, out r)) return;
            if (pressPath != null) nodes.TryGetValue(pressPath, out p);
            buttons.Add(new MenuButton { Name = name, Root = r, Press = p ?? r });
        }

        public void OnClick(string button, Action action)
        {
            foreach (var b in buttons)
                if (b.Name == button) b.Action = action;
        }

        MenuButton HitTest(Vector3 world)
        {
            for (int i = buttons.Count - 1; i >= 0; i--)
            {
                var b = buttons[i];
                if (!b.Root.gameObject.activeInHierarchy) continue;
                bool any = false;
                var bounds = new Bounds();
                foreach (var sr in b.Root.GetComponentsInChildren<SpriteRenderer>())
                {
                    if (!sr.enabled) continue;
                    if (!any) { bounds = sr.bounds; any = true; }
                    else bounds.Encapsulate(sr.bounds);
                }
                if (!any) continue;
                // the edge tabs of the navigation bar reach past the screen: clamp to what is visible
                if (bounds.Contains(new Vector3(world.x, world.y, bounds.center.z))) return b;
            }
            return null;
        }

        /// <summary>Pointer handling: press-down animation like the game, action on release over the same button.
        /// True while a press that started on one of the buttons is going on (the caller ignores it then).</summary>
        public bool Pointer(bool down, bool held, bool up, Vector3 world, float dt)
        {
            if (down) pressed = HitTest(world);
            if (pressed == null) return false;
            bool over = HitTest(world) == pressed;
            pressAnim = Mathf.MoveTowards(pressAnim, held && over ? 1f : 0f, dt * 12f);
            pressed.Press.localScale = Vector3.one * (1f - 0.06f * pressAnim);
            if (up)
            {
                pressed.Press.localScale = Vector3.one;
                var action = over ? pressed.Action : null;
                pressed = null;
                pressAnim = 0f;
                if (action != null) action();
            }
            return true;
        }

        // ================================================================== layout
        public bool Visible { get { return root.gameObject.activeSelf; } }

        public void SetVisible(bool visible)
        {
            root.gameObject.SetActive(visible);
            if (!visible) pressed = null;
        }

        /// <summary>Lays the parts out on the reference screen for a screen of the given aspect (width / height):
        /// each part keeps its distance to its edge; returns half the reference screen height.</summary>
        protected float PlaceParts(float aspect)
        {
            aspect = Mathf.Max(0.3f, aspect);
            float halfH = refWidth / aspect * 0.5f;
            foreach (var p in parts.Values)
            {
                float y = p.Anchor == "top" ? halfH + p.Offset : p.Anchor == "bottom" ? -halfH + p.Offset : p.Offset;
                var lp = p.Root.localPosition;
                p.Root.localPosition = new Vector3(p.RootX, y, lp.z);
            }
            if (background != null && background.sprite != null)
            {
                // the game swaps the placeholder for MainSceneBackground and scales it to cover the screen
                var size = background.sprite.bounds.size;
                float k = Mathf.Max(refWidth / size.x, halfH * 2f / size.y);
                background.transform.localScale = new Vector3(k, k, 1f);
            }
            return halfH;
        }

        /// <summary>Keeps the screen in front of a camera that the game moves and zooms (the level HUD over the board):
        /// the reference screen is scaled to the camera's view.</summary>
        public void Follow(Camera cam, float aspect)
        {
            aspect = Mathf.Max(0.3f, aspect);
            float k = cam.orthographicSize * 2f * aspect / refWidth;
            var c = cam.transform.position;
            root.position = new Vector3(c.x, c.y, 0f);
            root.localScale = new Vector3(k, k, 1f);
            PlaceParts(aspect);
        }

        /// <summary>Height of the reference screen for an aspect (the 4.6166 wide screen the UI is laid out on).</summary>
        public float ReferenceHeight(float aspect) { return refWidth / Mathf.Max(0.3f, aspect); }

        public Transform Node(string path)
        {
            Transform t;
            return nodes.TryGetValue(path, out t) ? t : null;
        }

        /// <summary>Text size relative to the box height (the game's TextMeshPro auto size stops below the box).</summary>
        protected void SetTextScale(string path, float k)
        {
            textScale[path] = k;
        }

        /// <summary>Moves / resizes a text box (for texts the game lays out in code, e.g. the timer's digits).</summary>
        protected void SetTextBox(string path, Vector2 center, Vector2 size)
        {
            TextBox box;
            if (!texts.TryGetValue(path, out box)) return;
            box.Size = size;
            box.Node.localPosition = new Vector3(center.x, center.y, box.Node.localPosition.z);
            string v = box.Value;
            box.Value = null;
            if (v != null) SetText(path, v);
        }

        public SpriteRenderer SpriteAt(string path)
        {
            Transform t;
            return nodes.TryGetValue(path, out t) ? t.GetComponent<SpriteRenderer>() : null;
        }
    }
}
