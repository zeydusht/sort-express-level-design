// Sort Express is a portrait phone game: keep Unity's Game view at a phone resolution while testing.
//
//   - Every Play of the SortexGame scene (Unity's Play button or Level Editor > Play level) switches the
//     Game view to "Sortex Phone" (1080 x 1920) if a landscape / Free Aspect size is selected.
//     A portrait size you picked yourself (or the Simulator tab) is left alone.
//   - Sortex > Portrait Game View (9:16) does it on demand.
//   - A fixed resolution renders the full 1080 x 1920 and scales it down to the tab, so it stays sharp.
//     (An aspect-ratio size is drawn at low resolution and blown up on high-DPI screens: blurry.)
//
// Unity has no public API for Game view sizes, so this goes through the editor's internal classes by
// reflection. If a Unity update renames them, it only logs how to do the same thing by hand.
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sortex.EditorTools
{
    [InitializeOnLoad]
    public static class SortexGameView
    {
        const string SizeName = "Sortex Phone";
        const string OldSizeName = "Sortex Portrait";     // first version: a blurry 9:16 aspect-ratio size
        const int PhoneW = 1080, PhoneH = 1920;
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public const string ManualHelp =
            "To set it by hand: at the top of the Game tab open the resolution menu (\"Free Aspect\"), press +, " +
            "choose Type: Fixed Resolution, Width 1080, Height 1920, OK, and select it.";

        static SortexGameView()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode) return;
            if (SceneManager.GetActiveScene().path != SortexSetup.ScenePath) return;
            EnsurePortrait(false);
        }

        [MenuItem("Sortex/Portrait Game View (9:16)", false, 21)]
        static void PortraitMenu()
        {
            if (!EnsurePortrait(true))
                EditorUtility.DisplayDialog("Sortex", "Could not find a Game tab in Game mode (is it set to Simulator?).\n\n" + ManualHelp, "OK");
        }

        /// <summary>
        /// Selects the 9:16 size in the Game view (adds it to the size list once).
        /// force = false keeps a portrait size that is already selected.
        /// </summary>
        public static bool EnsurePortrait(bool force)
        {
            try
            {
                var asm = typeof(Editor).Assembly;
                var sizesType = asm.GetType("UnityEditor.GameViewSizes");
                var sizeType = asm.GetType("UnityEditor.GameViewSize");
                var sizeKind = asm.GetType("UnityEditor.GameViewSizeType");
                var gameViewType = asm.GetType("UnityEditor.GameView");
                if (sizesType == null || sizeType == null || sizeKind == null || gameViewType == null)
                    throw new Exception("Game view classes not found");

                // the open Game tab (none when the tab shows the Device Simulator: leave that alone)
                var open = Resources.FindObjectsOfTypeAll(gameViewType);
                if (open.Length == 0) return false;
                var gameView = (EditorWindow)open[0];

                var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
                object sizes = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
                object group = sizesType.GetProperty("currentGroup", Flags).GetValue(sizes, null);
                var groupType = group.GetType();
                var getSize = groupType.GetMethod("GetGameViewSize", Flags);
                var totalCount = groupType.GetMethod("GetTotalCount", Flags);
                int count = (int)totalCount.Invoke(group, null);

                var selectedProp = gameViewType.GetProperty("selectedSizeIndex", Flags);
                int selected = (int)selectedProp.GetValue(gameView, null);
                var baseText = sizeType.GetProperty("baseText", Flags);
                if (!force && selected >= 0 && selected < count)
                {
                    var current = getSize.Invoke(group, new object[] { selected });
                    if (IsPortrait(current, sizeType) && (string)baseText.GetValue(current, null) != OldSizeName) return true;
                }

                int index = -1;
                for (int i = 0; i < count && index < 0; i++)
                    if ((string)baseText.GetValue(getSize.Invoke(group, new object[] { i }), null) == SizeName) index = i;

                if (index < 0)
                {
                    var ctor = sizeType.GetConstructor(Flags, null, new[] { sizeKind, typeof(int), typeof(int), typeof(string) }, null);
                    var size = ctor.Invoke(new object[] { Enum.Parse(sizeKind, "FixedResolution"), PhoneW, PhoneH, SizeName });
                    groupType.GetMethod("AddCustomSize", Flags).Invoke(group, new[] { size });
                    var save = sizesType.GetMethod("SaveToHDD", Flags);
                    if (save != null) save.Invoke(sizes, null);
                    index = (int)totalCount.Invoke(group, null) - 1;
                }

                var select = gameViewType.GetMethod("SizeSelectionCallback", Flags);
                if (select != null) select.Invoke(gameView, new object[] { index, null });
                else selectedProp.SetValue(gameView, index, null);
                FitZoom(gameViewType, gameView);
                gameView.Repaint();
                return true;
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                Debug.LogWarning("Sortex: could not switch the Game view to portrait (" + inner.Message + "). " + ManualHelp);
                return false;
            }
        }

        /// <summary>Zooms the Game tab so the whole phone screen is visible (the smallest scale of the slider).</summary>
        static void FitZoom(Type gameViewType, EditorWindow gameView)
        {
            try
            {
                var minScale = gameViewType.GetProperty("minScale", Flags);
                var snap = gameViewType.GetMethod("SnapZoom", Flags, null, new[] { typeof(float) }, null);
                if (minScale == null || snap == null) return;
                snap.Invoke(gameView, new[] { minScale.GetValue(gameView, null) });
            }
            catch (Exception)
            {
                // zoom is only a convenience
            }
        }

        static bool IsPortrait(object size, Type sizeType)
        {
            int w = (int)sizeType.GetProperty("width", Flags).GetValue(size, null);
            int h = (int)sizeType.GetProperty("height", Flags).GetValue(size, null);
            return w > 0 && h > w;    // Free Aspect is 0 x 0
        }
    }
}
