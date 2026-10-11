// Minimal UnityEngine / UnityEditor API surface, only used to type-check the prototype's C# offline
// (mcs) without a Unity installation. Signatures mirror the real Unity 6 API for the members used.
#pragma warning disable 0067, 0649, 0414, 0169
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    [Flags] public enum HideFlags { None = 0, DontSave = 52, HideAndDontSave = 61 }
    public class Object
    {
        public string name;
        public HideFlags hideFlags;
        public static void Destroy(Object o) { }
        public static void DestroyImmediate(Object o) { }
        public static T FindAnyObjectByType<T>() where T : Object { return null; }
        public static implicit operator bool(Object o) { return o != null; }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return new Vector2(); } }
        public static Vector2 one { get { return new Vector2(1, 1); } }
        public float sqrMagnitude { get { return x * x + y * y; } }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return a; }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return a; }
        public static Vector2 operator *(Vector2 a, float b) { return a; }
        public static Vector2 operator /(Vector2 a, float b) { return a; }
        public static Vector2 operator -(Vector2 a) { return a; }
        public static bool operator ==(Vector2 a, Vector2 b) { return a.x == b.x && a.y == b.y; }
        public static bool operator !=(Vector2 a, Vector2 b) { return !(a == b); }
        public override bool Equals(object o) { return o is Vector2 && (Vector2)o == this; }
        public override int GetHashCode() { return 0; }
        public Vector2 normalized { get { return this; } }
        public static float Dot(Vector2 a, Vector2 b) { return 0; }
        public static implicit operator Vector3(Vector2 v) { return new Vector3(v.x, v.y, 0); }
        public static implicit operator Vector2(Vector3 v) { return new Vector2(v.x, v.y); }
    }
    public struct Vector2Int
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public static Vector2Int zero { get { return new Vector2Int(); } }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3(float x, float y) { this.x = x; this.y = y; z = 0; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 one { get { return new Vector3(1, 1, 1); } }
        public float sqrMagnitude { get { return 0; } }
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { return a; }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator *(Vector3 a, float b) { return a; }
        public static Vector3 operator *(float b, Vector3 a) { return a; }
        public static Vector3 operator /(Vector3 a, float b) { return a; }
    }
    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 zero { get { return new Vector4(); } }
        public static bool operator ==(Vector4 a, Vector4 b) { return true; }
        public static bool operator !=(Vector4 a, Vector4 b) { return false; }
        public override bool Equals(object o) { return true; }
        public override int GetHashCode() { return 0; }
    }
    public enum ColorSpace { Uninitialized = -1, Gamma = 0, Linear = 1 }
    public static class QualitySettings { public static ColorSpace activeColorSpace { get { return ColorSpace.Linear; } } }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1; }
        public Color linear { get { return this; } }
        public static Color operator *(Color a, Color b) { return new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a); }
        public static Color white { get { return new Color(); } }
        public static Color magenta { get { return new Color(); } }
        public static Color clear { get { return new Color(); } }
        public static Color Lerp(Color a, Color b, float t) { return a; }
        public static Color operator *(Color a, float b) { return a; }
    }
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color(Color32 c) { return new Color(); }
        public static implicit operator Color32(Color c) { return new Color32(); }
    }
    public struct Rect
    {
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        public float x, y, width, height;
        public float xMax { get { return x + width; } }
        public float yMax { get { return y + height; } }
        public float xMin { get { return x; } }
        public float yMin { get { return y; } }
        public Vector2 center { get { return new Vector2(x + width / 2, y + height / 2); } }
        public Vector2 position { get { return new Vector2(x, y); } }
        public bool Contains(Vector2 p) { return true; }
        public static Rect MinMaxRect(float a, float b, float c, float d) { return new Rect(); }
    }
    public struct Bounds { public Vector3 size, center; public void Encapsulate(Bounds b) { } public bool Contains(Vector3 p) { return false; } }
    public static class PlayerPrefs { public static int GetInt(string k, int d) { return d; } public static void SetInt(string k, int v) { } public static void Save() { } }
    public struct Quaternion { public static Quaternion Euler(float x, float y, float z) { return new Quaternion(); } public static Quaternion identity { get { return new Quaternion(); } } }
    public struct CharacterInfo { public int advance; }
    public static class Mathf
    {
        public const float PI = 3.14159f;
        public static float Max(float a, float b) { return a; }
        public static int Max(int a, int b) { return a; }
        public static float Min(float a, float b) { return a; }
        public static int Min(int a, int b) { return a; }
        public static float Abs(float a) { return a; }
        public static float Round(float a) { return a; }
        public static float Clamp(float v, float a, float b) { return v; }
        public static int Clamp(int v, int a, int b) { return v; }
        public static float Clamp01(float v) { return v; }
        public static float Lerp(float a, float b, float t) { return a; }
        public static float Sin(float a) { return a; }
        public static float Cos(float a) { return a; }
        public static float Exp(float a) { return a; }
        public static float Pow(float a, float b) { return a; }
        public static float MoveTowards(float a, float b, float d) { return a; }
        public static int RoundToInt(float a) { return 0; }
        public static int CeilToInt(float a) { return 0; }
        public static int FloorToInt(float a) { return 0; }
    }
    public static class Random
    {
        public static float Range(float a, float b) { return a; }
        public static int Range(int a, int b) { return a; }
        public static float value { get { return 0; } }
    }
    public static class Time { public static float time; public static float deltaTime; public static int frameCount; }
    public static class Screen { public static int width; public static int height; }
    public static class Debug
    {
        public static void Log(object o) { }
        public static void LogWarning(object o) { }
        public static void LogError(object o) { }
        public static void LogException(Exception e) { }
    }
    public static class Application { public static int targetFrameRate; public static string dataPath; public static bool isEditor, isPlaying; public static void OpenURL(string u) { } }
    public static class ColorUtility { public static bool TryParseHtmlString(string s, out Color c) { c = new Color(); return true; } }
    public static class Resources
    {
        public static T Load<T>(string path) where T : Object { return null; }
        public static T GetBuiltinResource<T>(string path) where T : Object { return null; }
        public static Object[] FindObjectsOfTypeAll(Type t) { return new Object[0]; }
        public static T[] LoadAll<T>(string path) where T : Object { return new T[0]; }
    }
    public enum RenderTextureFormat { ARGB32 }
    public enum RenderTextureReadWrite { Default, Linear, sRGB }
    public class RenderTexture : Texture
    {
        public static RenderTexture active;
        public RenderTexture(int w, int h, int d, RenderTextureFormat f) { }
        public static RenderTexture GetTemporary(int w, int h, int d, RenderTextureFormat f, RenderTextureReadWrite rw) { return null; }
        public static void ReleaseTemporary(RenderTexture rt) { }
    }
    public static class Graphics { public static void Blit(Texture src, RenderTexture dst) { } }
    public enum TextureFormat { RGBA32, RGB24 }
    public enum FullScreenMode { ExclusiveFullScreen, FullScreenWindow, MaximizedWindow, Windowed }
    public enum TextureWrapMode { Repeat, Clamp }
    public class Texture : Object { public int width, height; public TextureWrapMode wrapMode; }
    public class Texture2D : Texture
    {
        public Texture2D(int w, int h) { }
        public Texture2D(int w, int h, TextureFormat f, bool mips) { }
        public void SetPixels(Color[] c) { }
        public void SetPixels32(Color32[] c) { }
        public byte[] EncodeToPNG() { return new byte[0]; }
        public Color32[] GetPixels32() { return null; }
        public void ReadPixels(Rect r, int x, int y) { }
        public void Apply() { }
        public void Apply(bool mips) { }
    }
    public enum SpriteMeshType { FullRect, Tight }
    public class Sprite : Object
    {
        public Vector4 border;
        public Bounds bounds;
        public static Sprite Create(Texture2D t, Rect r, Vector2 pivot, float ppu) { return null; }
        public static Sprite Create(Texture2D t, Rect r, Vector2 pivot, float ppu, uint extrude, SpriteMeshType mt) { return null; }
        public static Sprite Create(Texture2D t, Rect r, Vector2 pivot, float ppu, uint extrude, SpriteMeshType mt, Vector4 border) { return null; }
    }
    public class Shader : Object { public static Shader Find(string n) { return null; } }
    public class TextAsset : Object { public string text; }
    public class Material : Object { public Material(Shader s) { } }
    public class Font : Object { public Material material; public void RequestCharactersInTexture(string s, int size) { } public bool GetCharacterInfo(char c, out CharacterInfo ci, int size) { ci = new CharacterInfo(); return false; } }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform;
        public string tag;
        public T GetComponent<T>() { return default(T); }
        public T[] GetComponentsInChildren<T>() { return new T[0]; }
    }
    public class Behaviour : Component { public bool enabled; }
    public class MonoBehaviour : Behaviour { }
    public class Transform : Component
    {
        public Vector3 position, localPosition, localScale, lossyScale;
        public Quaternion localRotation, rotation;
        public Transform parent;
        public void SetParent(Transform p, bool w) { }
        public void Rotate(float x, float y, float z) { }
    }
    public class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta, offsetMin, offsetMax;
        public Rect rect;
    }
    public class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string n) { }
        public GameObject(string n, params Type[] t) { }
        public Transform transform;
        public string tag;
        public bool activeSelf, activeInHierarchy;
        public void SetActive(bool b) { }
        public T AddComponent<T>() where T : Component { return null; }
        public T GetComponent<T>() { return default(T); }
    }
    public class Renderer : Component
    {
        public int sortingOrder;
        public bool enabled;
        public Material sharedMaterial;
    }
    public class MeshRenderer : Renderer { }
    public enum SpriteDrawMode { Simple, Sliced, Tiled }
    public class SpriteRenderer : Renderer
    {
        public Sprite sprite;
        public Color color;
        public bool flipX, flipY;
        public Bounds bounds;
        public SpriteDrawMode drawMode;
        public Vector2 size;
    }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum TextAlignment { Left, Center, Right }
    public class TextMesh : Component
    {
        public string text;
        public Font font;
        public int fontSize;
        public float characterSize;
        public TextAnchor anchor;
        public TextAlignment alignment;
        public Color color;
    }
    public enum CameraClearFlags { Skybox, SolidColor }
    public class Camera : Behaviour
    {
        public static Camera main;
        public bool orthographic;
        public float orthographicSize, nearClipPlane, farClipPlane;
        public RenderTexture targetTexture;
        public void Render() { }
        public CameraClearFlags clearFlags;
        public Color backgroundColor;
        public Vector3 ScreenToWorldPoint(Vector3 p) { return p; }
    }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
    public class Canvas : Behaviour
    {
        public RenderMode renderMode;
        public int sortingOrder;
        public float scaleFactor;
        public static void ForceUpdateCanvases() { }
    }
    public enum ScaleMode { StretchToFill, ScaleAndCrop, ScaleToFit }
    public enum EventType { MouseDown, MouseUp, MouseMove, MouseDrag, KeyDown, KeyUp, ScrollWheel, Repaint, Layout }
    public enum KeyCode { None, Delete, Backspace, Z, Y, S, F, Q, B, E, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9 }
    public class Event
    {
        public static Event current;
        public EventType type;
        public KeyCode keyCode;
        public bool control, command, shift, alt;
        public int button;
        public Vector2 delta, mousePosition;
        public void Use() { }
    }
    public class GUIStyleState { public Color textColor; }
    public class GUIStyle
    {
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
        public TextAnchor alignment;
        public int fontSize;
        public GUIStyleState normal = new GUIStyleState();
    }
    public class GUIContent
    {
        public GUIContent(string text) { }
        public GUIContent(string text, string tooltip) { }
        public GUIContent(Texture image) { }
        public GUIContent(Texture image, string tooltip) { }
    }
    public sealed class GUILayoutOption { }
    public static class GUILayout
    {
        public static GUILayoutOption Width(float w) { return null; }
        public static GUILayoutOption Height(float h) { return null; }
        public static GUILayoutOption ExpandWidth(bool b) { return null; }
        public static GUILayoutOption ExpandHeight(bool b) { return null; }
        public static bool Button(string t, params GUILayoutOption[] o) { return false; }
        public static bool Button(string t, GUIStyle s, params GUILayoutOption[] o) { return false; }
        public static bool Button(GUIContent t, params GUILayoutOption[] o) { return false; }
        public static bool Button(GUIContent t, GUIStyle s, params GUILayoutOption[] o) { return false; }
        public static void Label(string t, params GUILayoutOption[] o) { }
        public static void Label(GUIContent c, params GUILayoutOption[] o) { }
        public static void Label(string t, GUIStyle s, params GUILayoutOption[] o) { }
        public static void Box(GUIContent c, params GUILayoutOption[] o) { }
        public static bool Toggle(bool v, string t, GUIStyle s, params GUILayoutOption[] o) { return v; }
        public static int Toolbar(int sel, string[] texts, GUIStyle s, params GUILayoutOption[] o) { return sel; }
        public static void Space(float px) { }
        public static void FlexibleSpace() { }
    }
    public static class GUILayoutUtility
    {
        public static Rect GetRect(float w, float h, params GUILayoutOption[] o) { return new Rect(); }
    }
    public static class GUIUtility { public static int keyboardControl; public static void ExitGUI() { } }
    public static class GUI
    {
        public static Color color, backgroundColor;
        public static bool enabled;
        public static void DrawTexture(Rect r, Texture t, ScaleMode m, bool alpha) { }
        public static void Label(Rect r, string t, GUIStyle s) { }
        public static void Label(Rect r, GUIContent c) { }
        public static void BeginClip(Rect r) { }
        public static void EndClip() { }
        public static Vector2 BeginScrollView(Rect pos, Vector2 scroll, Rect view) { return scroll; }
        public static void EndScrollView() { }
    }
    public class ScriptableObject : Object { }
    public class SerializeField : Attribute { }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, SubsystemRegistration, AfterAssembliesLoaded, BeforeSplashScreen }
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { } }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string name, path;
        public bool isDirty, isLoaded;
        public bool IsValid() { return true; }
        public static bool operator ==(Scene a, Scene b) { return true; }
        public static bool operator !=(Scene a, Scene b) { return false; }
        public override bool Equals(object o) { return true; }
        public override int GetHashCode() { return 0; }
    }
    public static class SceneManager
    {
        public static Scene GetActiveScene() { return new Scene(); }
        public static bool SetActiveScene(Scene s) { return true; }
    }
}

namespace UnityEngine.Events
{
    public class UnityEvent { public void AddListener(Action a) { } }
}

namespace UnityEngine.EventSystems
{
    public class UIBehaviour : MonoBehaviour { }
    public class EventSystem : UIBehaviour { }
    public class BaseInputModule : UIBehaviour { }
}

namespace UnityEngine.UI
{
    public class Graphic : UnityEngine.EventSystems.UIBehaviour
    {
        public Color color;
        public bool raycastTarget;
        public RectTransform rectTransform;
    }
    public class MaskableGraphic : Graphic { }
    public class Image : MaskableGraphic
    {
        public enum Type { Simple, Sliced, Tiled, Filled }
        public Sprite sprite;
        public Type type;
    }
    public enum HorizontalWrapMode { Wrap, Overflow }
    public enum VerticalWrapMode { Truncate, Overflow }
    public class Text : MaskableGraphic
    {
        public string text;
        public Font font;
        public int fontSize;
        public TextAnchor alignment;
        public HorizontalWrapMode horizontalOverflow;
        public VerticalWrapMode verticalOverflow;
        public bool supportRichText;
        public float lineSpacing;
    }
    public struct ColorBlock { public Color highlightedColor, pressedColor; }
    public class Selectable : UnityEngine.EventSystems.UIBehaviour
    {
        public enum Transition { None, ColorTint }
        public Transition transition;
        public ColorBlock colors;
    }
    public class Button : Selectable
    {
        public class ButtonClickedEvent : UnityEngine.Events.UnityEvent { }
        public ButtonClickedEvent onClick;
    }
    public class BaseMeshEffect : UnityEngine.EventSystems.UIBehaviour { }
    public class Shadow : BaseMeshEffect { public Color effectColor; public Vector2 effectDistance; }
    public class Outline : Shadow { }
    public class CanvasScaler : UnityEngine.EventSystems.UIBehaviour
    {
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize }
        public enum ScreenMatchMode { MatchWidthOrHeight, Expand, Shrink }
        public ScaleMode uiScaleMode;
        public Vector2 referenceResolution;
        public ScreenMatchMode screenMatchMode;
        public float matchWidthOrHeight;
    }
    public class GraphicRaycaster : UnityEngine.EventSystems.UIBehaviour { }
    public class RectMask2D : UnityEngine.EventSystems.UIBehaviour { }
    public class ScrollRect : UnityEngine.EventSystems.UIBehaviour
    {
        public enum MovementType { Unrestricted, Elastic, Clamped }
        public bool horizontal, vertical;
        public MovementType movementType;
        public float scrollSensitivity;
        public RectTransform viewport, content;
    }
}

namespace UnityEngine.InputSystem
{
    public class ButtonControl { public bool wasPressedThisFrame, wasReleasedThisFrame, isPressed; }
    public class Vector2Control { public Vector2 ReadValue() { return new Vector2(); } }
    public class Pointer
    {
        public static Pointer current;
        public ButtonControl press;
        public Vector2Control position;
    }
}

namespace UnityEngine.InputSystem.UI
{
    public class InputSystemUIInputModule : UnityEngine.EventSystems.BaseInputModule { public void AssignDefaultActions() { } }
}

namespace UnityEditor
{
    using UnityEngine;
    public class InitializeOnLoadAttribute : Attribute { }
    public class MenuItem : Attribute { public MenuItem(string s, bool v, int p) { } public MenuItem(string s, bool v) { } }
    public static class Selection { public static Object activeObject; }
    public static class EditorApplication
    {
        public delegate void CallbackFunction();
        public static CallbackFunction delayCall, update;
        public static bool isPlayingOrWillChangePlaymode, isPlaying, isCompiling, isUpdating;
        public static double timeSinceStartup;
        public static event Action<PlayModeStateChange> playModeStateChanged;
        static void Touch() { if (playModeStateChanged != null) playModeStateChanged(PlayModeStateChange.EnteredEditMode); }
    }
    public enum PlayModeStateChange { EnteredEditMode, ExitingEditMode, EnteredPlayMode, ExitingPlayMode }
    public class Editor : ScriptableObject { }
    public class ScriptableSingleton<T> : ScriptableObject where T : ScriptableObject { public static T instance { get { return null; } } }
    public enum UIOrientation { Portrait, PortraitUpsideDown, LandscapeRight, LandscapeLeft, AutoRotation }
    public static class SessionState
    {
        public static bool GetBool(string k, bool d) { return d; }
        public static void SetBool(string k, bool v) { }
        public static string GetString(string k, string d) { return d; }
        public static void SetString(string k, string v) { }
        public static void EraseString(string k) { }
    }
    public static class EditorPrefs
    {
        public static string GetString(string k, string d) { return d; }
        public static void SetString(string k, string v) { }
    }
    public class EditorWindow : ScriptableObject
    {
        public Vector2 minSize;
        public Rect position;
        public bool wantsMouseMove;
        public static T GetWindow<T>(string title) where T : EditorWindow { return null; }
        public static EditorWindow GetWindow(Type t) { return null; }
        public void Focus() { }
        public void Repaint() { }
    }
    public enum MessageType { None, Info, Warning, Error }
    public static class EditorStyles
    {
        public static GUIStyle toolbar, toolbarButton, toolbarDropDown, toolbarSearchField, miniBoldLabel, miniLabel, boldLabel,
            wordWrappedMiniLabel, miniButton, miniButtonLeft, miniButtonMid, miniButtonRight, helpBox, centeredGreyMiniLabel;
    }
    public static class EditorGUI
    {
        public static void BeginChangeCheck() { }
        public static bool EndChangeCheck() { return false; }
        public static void DrawRect(Rect r, Color c) { }
        public static void ProgressBar(Rect r, float v, string t) { }
    }
    public static class EditorGUILayout
    {
        public static void BeginHorizontal(params GUILayoutOption[] o) { }
        public static void BeginHorizontal(GUIStyle s, params GUILayoutOption[] o) { }
        public static void EndHorizontal() { }
        public static void BeginVertical(params GUILayoutOption[] o) { }
        public static void EndVertical() { }
        public static Vector2 BeginScrollView(Vector2 v, params GUILayoutOption[] o) { return v; }
        public static void EndScrollView() { }
        public static bool Foldout(bool v, string t, bool toggleOnLabel) { return v; }
        public static void HelpBox(string t, MessageType m) { }
        public static string TextField(string label, string v) { return v; }
        public static string TextField(string v, GUIStyle s, params GUILayoutOption[] o) { return v; }
        public static int Popup(string label, int i, string[] options) { return i; }
        public static int IntField(string label, int v) { return v; }
        public static int IntField(int v, params GUILayoutOption[] o) { return v; }
        public static float FloatField(string label, float v) { return v; }
        public static int IntSlider(string label, int v, int a, int b) { return v; }
        public static int IntSlider(GUIContent label, int v, int a, int b) { return v; }
        public static float Slider(string label, float v, float a, float b) { return v; }
        public static float Slider(GUIContent label, float v, float a, float b) { return v; }
        public static bool Toggle(string label, bool v) { return v; }
        public static bool Toggle(GUIContent label, bool v) { return v; }
        public static Rect GetControlRect(bool hasLabel, float height) { return new Rect(); }
    }
    public class GenericMenu
    {
        public delegate void MenuFunction();
        public void AddItem(GUIContent c, bool on, MenuFunction f) { }
        public void AddSeparator(string path) { }
        public void ShowAsContext() { }
    }
    public static class EditorUtility
    {
        public static bool DisplayDialog(string a, string b, string c) { return true; }
        public static void DisplayProgressBar(string t, string i, float p) { }
        public static bool DisplayCancelableProgressBar(string t, string i, float p) { return false; }
        public static void ClearProgressBar() { }
        public static bool DisplayDialog(string a, string b, string c, string d) { return true; }
        public static string SaveFilePanelInProject(string title, string name, string ext, string message, string path) { return ""; }
        public static string OpenFilePanel(string title, string dir, string ext) { return ""; }
        public static string OpenFolderPanel(string title, string dir, string name) { return ""; }
        public static void RevealInFinder(string p) { }
    }
    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string p) where T : Object { return null; }
        public static string[] FindAssets(string filter) { return new string[0]; }
        public static string GUIDToAssetPath(string guid) { return ""; }
        public static string GetAssetPath(Object o) { return ""; }
        public static void ImportAsset(string p) { }
        public static void CreateAsset(Object o, string p) { }
        public static void SaveAssets() { }
        public static void Refresh() { }
    }
    public enum WebGLCompressionFormat { Brotli, Gzip, Disabled }
    public static class PlayerSettings
    {
        public static class WebGL
        {
            public static WebGLCompressionFormat compressionFormat;
            public static bool decompressionFallback, dataCaching;
        }
        public static int defaultWebScreenWidth, defaultWebScreenHeight;
        public static bool runInBackground, resizableWindow;
        public static string productName;
        public static int defaultScreenWidth, defaultScreenHeight;
        public static UIOrientation defaultInterfaceOrientation;
        public static FullScreenMode fullScreenMode;
    }
    public class EditorBuildSettingsScene
    {
        public EditorBuildSettingsScene(string path, bool enabled) { this.path = path; }
        public string path;
    }
    public static class EditorBuildSettings { public static EditorBuildSettingsScene[] scenes; }
    public enum BuildTarget { WebGL }
    [Flags] public enum BuildOptions { None = 0 }
    public struct BuildPlayerOptions
    {
        public string[] scenes;
        public string locationPathName;
        public BuildTarget target;
        public BuildOptions options;
    }
    public static class BuildPipeline
    {
        public static UnityEditor.Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions o) { return null; }
    }
}

namespace UnityEditor.Build.Reporting
{
    public enum BuildResult { Unknown, Succeeded, Failed, Cancelled }
    public struct BuildSummary { public BuildResult result; public ulong totalSize; }
    public class BuildReport { public BuildSummary summary; }
}

namespace UnityEditor.SceneManagement
{
    using UnityEngine.SceneManagement;
    public enum NewSceneSetup { EmptyScene, DefaultGameObjects }
    public enum NewSceneMode { Single, Additive }
    public static class EditorSceneManager
    {
        public static Scene NewScene(NewSceneSetup s, NewSceneMode m) { return new Scene(); }
        public static bool SaveScene(Scene s, string p) { return true; }
        public static Scene OpenScene(string p) { return new Scene(); }
        public static bool CloseScene(Scene s, bool remove) { return true; }
        public static bool SaveCurrentModifiedScenesIfUserWantsTo() { return true; }
    }
}

namespace UnityEngine.Rendering
{
    public class SortingGroup : UnityEngine.Behaviour { public int sortingOrder; public int sortingLayerID; }
}
