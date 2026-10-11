// Indexes an AssetRipper export (or any Unity asset folder) by GUID, so tools outside Unity can resolve the
// sprite / texture / material references inside exported prefabs.
//
//   Sortex > Tools > Index Asset Export…   pick the export's Assets folder -> Logs/SortexIndex.tsv
//   or create Logs/SortexIndex.request containing the folder path (works while Unity is in the background).
//
// Output: one line per asset, "guid <TAB> path relative to the folder <TAB> size in bytes".
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Sortex.EditorTools
{
    [InitializeOnLoad]
    public static class SortexExportIndex
    {
        static double nextPoll;

        static string Logs { get { return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs"); } }

        static SortexExportIndex()
        {
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 2.0;
            string request = Path.Combine(Logs, "SortexIndex.request");
            if (!File.Exists(request)) return;
            string folder;
            try
            {
                folder = File.ReadAllText(request).Trim();
                File.Delete(request);
            }
            catch (Exception)
            {
                return;
            }
            Write(folder);
        }

        [MenuItem("Sortex/Tools/Index Asset Export…", false, 40)]
        static void IndexMenu()
        {
            string folder = EditorUtility.OpenFolderPanel("Asset folder to index (e.g. an AssetRipper ExportedProject/Assets)", "", "");
            if (string.IsNullOrEmpty(folder)) return;
            int n = Write(folder);
            EditorUtility.DisplayDialog("Sortex", n + " assets indexed into Logs/SortexIndex.tsv", "OK");
        }

        static int Write(string folder)
        {
            var sb = new StringBuilder();
            int count = 0;
            try
            {
                foreach (var meta in Directory.GetFiles(folder, "*.meta", SearchOption.AllDirectories))
                {
                    string guid = null;
                    using (var reader = new StreamReader(meta))
                    {
                        for (int i = 0; i < 6 && guid == null; i++)
                        {
                            string line = reader.ReadLine();
                            if (line == null) break;
                            line = line.Trim();
                            if (line.StartsWith("guid:", StringComparison.Ordinal)) guid = line.Substring(5).Trim();
                        }
                    }
                    if (guid == null) continue;
                    string asset = meta.Substring(0, meta.Length - 5);
                    long size = File.Exists(asset) ? new FileInfo(asset).Length : -1;
                    string rel = asset.Substring(folder.Length).TrimStart('/', '\\').Replace('\\', '/');
                    sb.Append(guid).Append('\t').Append(rel).Append('\t').Append(size).Append('\n');
                    count++;
                }
                Directory.CreateDirectory(Logs);
                File.WriteAllText(Path.Combine(Logs, "SortexIndex.tsv"), sb.ToString());
                Debug.Log("Sortex: indexed " + count + " assets of " + folder + " into Logs/SortexIndex.tsv");
            }
            catch (Exception e)
            {
                Debug.LogError("Sortex: could not index " + folder + ": " + e.Message);
            }
            return count;
        }
    }
}
