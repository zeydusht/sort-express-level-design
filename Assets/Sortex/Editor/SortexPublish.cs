// Sortex > 3. Publish to GitHub: the one step after designing levels.
//
//   1. Simulates every level saved with the Level Editor (Resources/Sortex/Levels, except Easy / Medium / Hard)
//      with 1000 players, stores the results in report.json (shown on the menu cards) and rewrites the
//      "Kendi levellarım" table of README.md (between the MY-LEVELS markers).
//   2. Builds the WebGL player into docs/ (GitHub Pages serves that folder).
//   3. git add / commit / push to github.com/zeydusht/sort-express-level-design. The first push asks for a
//      GitHub sign-in (Git Credential Manager window); after that it is silent.
//
// The same publish can be requested by creating the file Logs/SortexPublish.request (works while Unity is in the
// background; "no-build" inside skips step 2). Progress is written to Logs/SortexPublish.log.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Sortex.Core;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Sortex.EditorTools
{
    [InitializeOnLoad]
    public static class SortexPublish
    {
        public const string Owner = "zeydusht";
        public const string Repo = "sort-express-level-design";
        public static string RemoteUrl { get { return "https://github.com/" + Owner + "/" + Repo + ".git"; } }
        public static string SiteUrl { get { return "https://" + Owner + ".github.io/" + Repo + "/"; } }

        const string LevelsDir = "Assets/Sortex/Resources/Sortex/Levels";
        const string StartMarker = "<!-- MY-LEVELS:START -->";
        const string EndMarker = "<!-- MY-LEVELS:END -->";
        const int Players = 1000;
        static readonly string[] CaseLevels = { "Easy", "Medium", "Hard" };
        static readonly string[] TierNames = { "Normal", "Hard", "Super Hard" };
        static readonly float[] BandLo = { 0.88f, 0.50f, 0.20f };
        static readonly float[] BandHi = { 1.00f, 0.75f, 0.45f };

        static bool running;
        static bool interactiveRun;
        static double nextPoll;

        static string Root { get { return Directory.GetParent(Application.dataPath).FullName; } }
        static string StatusFile { get { return Path.Combine(Path.Combine(Root, "Logs"), "SortexPublish.log"); } }
        static string RequestFile { get { return Path.Combine(Path.Combine(Root, "Logs"), "SortexPublish.request"); } }

        static SortexPublish()
        {
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 2.0;
            if (running || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!File.Exists(RequestFile)) return;
            string mode;
            try
            {
                mode = File.ReadAllText(RequestFile).Trim();
                File.Delete(RequestFile);
            }
            catch (Exception)
            {
                return;
            }
            // straight from the update loop: delayCall waits for a repaint, which never comes while Unity is in the background
            Run(false, mode != "no-build");
        }

        // ================================================================== entry points
        [MenuItem("Sortex/3. Publish to GitHub", false, 3)]
        public static void PublishMenu()
        {
            if (running)
            {
                EditorUtility.DisplayDialog("Publish", "A publish is already running. See Logs/SortexPublish.log.", "OK");
                return;
            }
            var own = OwnLevelFiles();
            string list = own.Count == 0 ? "(none yet)" : string.Join(", ", own.Select(Path.GetFileNameWithoutExtension).ToArray());
            if (!EditorUtility.DisplayDialog("Publish to GitHub",
                    "1. Simulate your levels with " + Players + " players and update README + menu cards\n   " + list + "\n" +
                    "2. Build WebGL into docs/ (a few minutes)\n" +
                    "3. Commit and push to github.com/" + Owner + "/" + Repo + "\n\n" +
                    "The site updates 1-2 minutes after the push:\n" + SiteUrl, "Publish", "Cancel"))
                return;
            running = true;
            // outside OnGUI / menu callbacks: building the player from there is not allowed
            EditorApplication.delayCall += () => Run(true, true);
        }

        public static void Run(bool interactive, bool build)
        {
            running = true;
            interactiveRun = interactive;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatusFile));
                File.WriteAllText(StatusFile, "");
            }
            catch (Exception)
            {
                // the log file is a convenience only
            }
            Status("started (" + (build ? "simulate + build + push" : "simulate + push") + ")");
            try
            {
                var rows = SimulateOwnLevels();
                if (rows == null)
                {
                    Status("cancelled");
                    return;
                }
                WriteReport(rows);
                WriteReadme(rows);
                AssetDatabase.Refresh();
                Status("README and report.json updated (" + rows.Count + " own levels)");

                if (build)
                {
                    Status("building WebGL into docs/");
                    EditorUtility.DisplayProgressBar("Publish", "Building WebGL (a few minutes)…", 0.5f);
                    bool ok = SortexSetup.BuildDocs(false);
                    EditorUtility.ClearProgressBar();
                    if (!ok)
                    {
                        Fail("WebGL build failed, see the Console.");
                        return;
                    }
                    Status("WebGL build done");
                }

                var names = rows.Where(r => r.Problems.Count == 0).Select(r => r.Name).ToArray();
                string message = "Publish " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                                 (names.Length > 0 ? " - levels: " + string.Join(", ", names) : "");
                if (PushToGitHub(message))
                {
                    Status("done: pushed to " + RemoteUrl + ", site " + SiteUrl);
                    if (interactive &&
                        EditorUtility.DisplayDialog("Published", "Pushed to GitHub. The site updates in 1-2 minutes:\n" + SiteUrl, "Open site", "Close"))
                        Application.OpenURL(SiteUrl);
                }
            }
            catch (Exception e)
            {
                Fail(e.Message);
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                running = false;
            }
        }

        // ================================================================== 1. own levels
        sealed class Row
        {
            public string File, Name, Tier, Obstacles;
            public int TierIndex, Shelves, Items, Types, Time;
            public List<string> Problems = new List<string>();
            public SimReport Report;
        }

        static List<string> OwnLevelFiles()
        {
            if (!Directory.Exists(LevelsDir)) return new List<string>();
            var files = Directory.GetFiles(LevelsDir, "*.json")
                .Where(f =>
                {
                    string n = Path.GetFileNameWithoutExtension(f);
                    return n != "report" && Array.IndexOf(CaseLevels, n) < 0;
                })
                .ToList();
            files.Sort((a, b) => Sortex.Game.SortexGame.NaturalCompare(Path.GetFileNameWithoutExtension(a), Path.GetFileNameWithoutExtension(b)));
            return files;
        }

        /// <summary>Null when the user cancels.</summary>
        static List<Row> SimulateOwnLevels()
        {
            var rows = new List<Row>();
            var files = OwnLevelFiles();
            for (int i = 0; i < files.Count; i++)
            {
                string file = Path.GetFileNameWithoutExtension(files[i]);
                LevelData lv;
                try { lv = LevelData.FromJson(File.ReadAllText(files[i])); }
                catch (Exception e)
                {
                    rows.Add(new Row { File = file, Name = file, Tier = "-", Problems = { "unreadable: " + e.Message } });
                    continue;
                }
                var board = Board.FromLevel(lv);
                int tier = lv.difficultyTag >= 20 ? 2 : lv.difficultyTag >= 10 ? 1 : 0;
                var row = new Row
                {
                    File = file,
                    Name = string.IsNullOrEmpty(lv.name) || lv.name == "Level" || lv.name == "New Level" ? file : lv.name,
                    TierIndex = tier,
                    Tier = TierNames[tier],
                    Shelves = lv.shelves.Count,
                    Items = board.TotalItems,
                    Types = lv.ItemIds().Count,
                    Time = lv.timeLimit,
                    Obstacles = Obstacles(lv),
                    Problems = lv.Problems()
                };
                rows.Add(row);
                if (row.Problems.Count > 0)
                {
                    Status(file + ": not playable (" + row.Problems[0] + ")");
                    continue;
                }
                Status("simulating " + file);
                int index = i;
                float shown = -1f;
                bool cancelled = false;
                row.Report = new Simulator().Run(lv, new SimSettings { Runs = Players, Seed = 7 }, p =>
                {
                    if (p - shown < 0.01f && p < 1f) return !cancelled;
                    shown = p;
                    cancelled = EditorUtility.DisplayCancelableProgressBar("Publish",
                        "Simulating " + file + " with " + Players + " players…", (index + p) / files.Count);
                    return !cancelled;
                });
                if (cancelled || row.Report == null || row.Report.Cancelled) return null;
                Status(file + ": win " + Pct(row.Report.WinRate) + ", stuck " + Pct(row.Report.Stuck) + ", timeout " + Pct(row.Report.Timeout));
            }
            EditorUtility.ClearProgressBar();
            return rows;
        }

        static string Obstacles(LevelData lv)
        {
            int frozen = 0, mystery = 0, pinata = 0, egg = 0;
            foreach (var s in lv.shelves)
                foreach (var l in s.layers)
                    foreach (var c in l)
                    {
                        if (c == null) continue;
                        if (c.IsItem && c.frozen) frozen++;
                        if (c.IsItem && c.mystery) mystery++;
                        if (c.IsPinata) pinata++;
                        if (c.IsEgg) egg++;
                    }
            var parts = new List<string>();
            int locks = lv.shelves.Count(s => s.lockCount > 0);
            if (locks > 0) parts.Add("kilit " + locks + " (" + lv.shelves.Sum(s => s.lockCount) + " eşleşme)");
            Count(parts, "shutter", lv.shelves.Count(s => s.shutter));
            Count(parts, "cargo door", lv.shelves.Count(s => s.cargo));
            Count(parts, "moving", lv.shelves.Count(s => s.moving));
            Count(parts, "döner kapı", lv.shelves.Count(s => s.ferris));
            Count(parts, "iris", lv.shelves.Count(s => s.iris));
            Count(parts, "fuse box", lv.shelves.Count(s => s.fuseItems.Count > 0));
            Count(parts, "geniş raf", lv.shelves.Count(s => s.width > 3));
            Count(parts, "frozen", frozen);
            Count(parts, "mystery", mystery);
            Count(parts, "piñata", pinata);
            Count(parts, "yumurta", egg);
            return parts.Count == 0 ? "yok" : string.Join(", ", parts.ToArray());
        }

        static void Count(List<string> parts, string label, int n)
        {
            if (n > 0) parts.Add(label + " " + n);
        }

        static void WriteReport(List<Row> rows)
        {
            string path = LevelsDir + "/report.json";
            var root = File.Exists(path) ? MiniJson.Parse(File.ReadAllText(path)) as Dictionary<string, object> : null;
            if (root == null) root = new Dictionary<string, object>();
            // keep the case levels, replace everything else with the current own levels
            foreach (var key in root.Keys.ToList())
                if (Array.IndexOf(CaseLevels.Select(c => c.ToLowerInvariant()).ToArray(), key) < 0) root.Remove(key);
            foreach (var r in rows)
            {
                if (r.Report == null) continue;
                var entry = MiniJson.Parse(r.Report.ToJson()) as Dictionary<string, object>;
                if (entry == null) continue;
                entry["players"] = (double)Players;
                root[r.File.ToLowerInvariant()] = entry;
            }
            File.WriteAllText(path, MiniJson.Serialize(root, true));
        }

        static void WriteReadme(List<Row> rows)
        {
            string path = Path.Combine(Root, "README.md");
            if (!File.Exists(path)) return;
            string text = File.ReadAllText(path);
            var sb = new StringBuilder();
            sb.Append(StartMarker).Append('\n');
            sb.Append("_Bu tablo **Sortex → 3. Publish to GitHub** ile otomatik güncelleniyor (son: ")
              .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(")._\n\n");
            if (rows.Count == 0)
            {
                sb.Append("Henüz editörle kaydedilmiş level yok.\n");
            }
            else
            {
                sb.Append("| Level | Tier | Raf | Item | Tür | Süre | Engeller | Simüle kazanma (" + Players + " oyuncu) | Hedef bant | Kayıp: süre / hamle kalmadı | Çözücü |\n");
                sb.Append("|---|---|---|---|---|---|---|---|---|---|---|\n");
                foreach (var r in rows)
                {
                    if (r.Report == null)
                    {
                        sb.Append("| ").Append(r.Name).Append(" | ").Append(r.Tier)
                          .Append(" | | | | | | yayınlanmadı: ").Append(r.Problems.Count > 0 ? r.Problems[0] : "-").Append(" | | | |\n");
                        continue;
                    }
                    var rep = r.Report;
                    float lo = BandLo[r.TierIndex], hi = BandHi[r.TierIndex];
                    string band = Pct(lo) + "–" + Math.Round(hi * 100).ToString(CultureInfo.InvariantCulture) + (rep.WinRate < lo - 1e-6 ? " (daha zor)" : rep.WinRate > hi + 1e-6 ? " (daha kolay)" : " ✔");
                    sb.Append("| ").Append(r.Name)
                      .Append(" | ").Append(r.Tier)
                      .Append(" | ").Append(r.Shelves)
                      .Append(" | ").Append(r.Items)
                      .Append(" | ").Append(r.Types)
                      .Append(" | ").Append(r.Time).Append(" sn (").Append((r.Items > 0 ? (float)r.Time / r.Items : 0f).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',')).Append(" sn/item)")
                      .Append(" | ").Append(r.Obstacles)
                      .Append(" | **").Append(Pct(rep.WinRate)).Append("**")
                      .Append(" | ").Append(band)
                      .Append(" | ").Append(Pct(rep.Timeout)).Append(" / ").Append(Pct(rep.Stuck))
                      .Append(" | ").Append(rep.Solvable ? "✔ " + rep.SolutionMoves + " hamle" : "kanıt yok")
                      .Append(" |\n");
                }
            }
            sb.Append(EndMarker);

            int a = text.IndexOf(StartMarker, StringComparison.Ordinal);
            int b = text.IndexOf(EndMarker, StringComparison.Ordinal);
            if (a >= 0 && b > a) text = text.Substring(0, a) + sb + text.Substring(b + EndMarker.Length);
            else text = text.TrimEnd() + "\n\n## Kendi levellarım\n\n" + sb + "\n";
            File.WriteAllText(path, text);
        }

        static string Pct(double v)
        {
            return "%" + Math.Round(v * 100).ToString(CultureInfo.InvariantCulture);
        }

        // ================================================================== 3. git
        /// <summary>
        /// Runs on the main thread on purpose: building WebGL can switch the active platform, and the domain reload
        /// that follows would kill a background thread in the middle of a push.
        /// </summary>
        static bool PushToGitHub(string message)
        {
            string git = FindGit();
            if (git == null)
            {
                Fail("Git was not found. Install it from https://git-scm.com and publish again.");
                return false;
            }
            string root = Root;
            var log = new StringBuilder();
            try
            {
                if (!Directory.Exists(Path.Combine(root, ".git")))
                    Must(Git(git, root, "init", log, 60), "git init");
                if (Capture(git, root, "config user.email").Trim().Length == 0)
                {
                    Git(git, root, "config user.name \"Zeyd Tekin\"", log, 30);
                    Git(git, root, "config user.email \"" + Owner + "@users.noreply.github.com\"", log, 30);
                }
                if (Git(git, root, "remote get-url origin", log, 30) != 0)
                    Must(Git(git, root, "remote add origin " + RemoteUrl, log, 30), "git remote add");
                else
                    Git(git, root, "remote set-url origin " + RemoteUrl, log, 30);
                Must(Git(git, root, "add -A", log, 600), "git add");
                if (Git(git, root, "commit -m \"" + message.Replace("\"", "'") + "\"", log, 120) != 0)
                    log.Append("(nothing new to commit)\n");
                Git(git, root, "branch -M main", log, 30);
                Status("git: pushing to " + RemoteUrl + " (the first push opens a GitHub sign-in window)");
                Must(Git(git, root, "push -u origin main", log, 1800), "git push");
                return true;
            }
            catch (Exception e)
            {
                Fail(e.Message + " (details in Logs/SortexPublish.log)");
                return false;
            }
            finally
            {
                AppendStatus(log.ToString());
            }
        }

        static void Must(int code, string what)
        {
            if (code != 0) throw new Exception(what + " failed (exit code " + code + ")");
        }

        static string FindGit()
        {
            foreach (var candidate in new[]
                     {
                         "git",
                         @"C:\Program Files\Git\cmd\git.exe",
                         @"C:\Program Files (x86)\Git\cmd\git.exe",
                         "/usr/bin/git",
                         "/opt/homebrew/bin/git",
                         "/usr/local/bin/git"
                     })
            {
                try
                {
                    if (Exec(candidate, Directory.GetCurrentDirectory(), "--version", null, 20) == 0) return candidate;
                }
                catch (Exception)
                {
                    // not there, try the next one
                }
            }
            return null;
        }

        static int Git(string git, string root, string args, StringBuilder log, int timeoutSeconds)
        {
            log.Append("$ git ").Append(args).Append('\n');
            return Exec(git, root, args, log, timeoutSeconds);
        }

        static string Capture(string git, string root, string args)
        {
            var sb = new StringBuilder();
            Exec(git, root, args, sb, 30);
            return sb.ToString();
        }

        static int Exec(string exe, string dir, string args, StringBuilder output, int timeoutSeconds)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";   // never wait for a hidden console prompt
            using (var p = Process.Start(psi))
            {
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                var watch = Stopwatch.StartNew();
                while (!p.WaitForExit(250))
                {
                    if (watch.Elapsed.TotalSeconds > timeoutSeconds)
                    {
                        try { p.Kill(); }
                        catch (Exception) { }
                        if (output != null) output.Append("(timed out after " + timeoutSeconds + " s)\n");
                        return -1;
                    }
                    if (timeoutSeconds > 60)
                        EditorUtility.DisplayProgressBar("Publish", "git " + args.Split(' ')[0] + "… (" + (int)watch.Elapsed.TotalSeconds +
                                                         " s; on the first push, sign in to GitHub in the window that opened)", 0.9f);
                }
                p.WaitForExit();
                if (output != null) output.Append(stdout.Result).Append(stderr.Result);
                return p.ExitCode;
            }
        }

        // ================================================================== status
        static void Status(string message)
        {
            Debug.Log("Sortex publish: " + message);
            AppendStatus(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message + "\n");
        }

        static void AppendStatus(string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatusFile));
                File.AppendAllText(StatusFile, text);
            }
            catch (Exception)
            {
                // the log file is a convenience only
            }
        }

        static void Fail(string message)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError("Sortex publish: FAILED " + message);
            AppendStatus(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  FAILED " + message + "\n");
            if (interactiveRun) EditorUtility.DisplayDialog("Publish failed", message, "OK");
        }
    }
}
