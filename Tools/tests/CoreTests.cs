// Small regression tests for the shared rules code:  mono coretests.exe <levels dir>
using System;
using System.IO;
using System.Linq;
using Sortex.Core;

static class CoreTests
{
    static int fails;
    static void Check(bool ok, string what) { Console.WriteLine((ok ? "ok   " : "FAIL ") + what); if (!ok) fails++; }

    static string Signature(Board b)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var s in b.Shelves)
        {
            foreach (var layer in s.Layers)
            {
                foreach (var it in layer) sb.Append(it == null ? "_" : it.Type).Append(',');
                sb.Append('|');
            }
            sb.Append('/');
        }
        return sb.ToString();
    }

    static int Main(string[] args)
    {
        // 1. JSON round trip keeps the level identical (same simulation result)
        foreach (var n in new[] { "Easy", "Medium", "Hard" })
        {
            string json = File.ReadAllText(Path.Combine(args[0], n + ".json"));
            var a = LevelData.FromJson(json);
            var b = LevelData.FromJson(a.ToJson());
            var s = new SimSettings { Runs = 60, Seed = 3 };
            string ra = new Simulator().Run(a, s).ToJson(), rb = new Simulator().Run(b, s).ToJson();
            Check(ra == rb, n + " round trip");
            Check(LevelData.FromJson(b.ToJson(false)).ToJson() == b.ToJson(), n + " writer is stable");
        }

        // 2. shutter: a shelf that runs out of items closes for good
        var lv = new LevelData { name = "shutter", timeLimit = 60 };
        var sh = ShelfData.Create(0, 0, 3, 1); sh.shutter = true;
        sh.layers[0][0] = new SlotData { it = 1, iid = "a" };
        var other = ShelfData.Create(3.13f, 0, 3, 1);
        other.layers[0][0] = new SlotData { it = 1, iid = "a" };
        other.layers[0][1] = new SlotData { it = 1, iid = "a" };
        var third = ShelfData.Create(-3.13f, 0, 3, 1);
        third.layers[0][0] = new SlotData { it = 1, iid = "b" };
        lv.shelves.Add(sh); lv.shelves.Add(other); lv.shelves.Add(third);
        var board = Board.FromLevel(lv);
        var r = board.Move(2, 0, 0, 1);       // park b on the shutter shelf
        Check(r.Ok && !board.Shelves[0].Closed, "shutter stays open while it holds items");
        r = board.Move(0, 0, 1, 2);           // a -> other: triple, shutter still has b
        Check(r.Ok && r.Matched && !board.Shelves[0].Closed, "match elsewhere");
        r = board.Move(0, 1, 2, 0);           // last item leaves the shutter shelf
        Check(r.Ok && board.Shelves[0].Closed && r.Closed.Contains(0), "shutter closes when empty");
        Check(board.Check(2, 0, 0, 0) == MoveFail.TargetClosed, "closed shelf rejects items");

        // 3. frozen items cannot move, locks open after N matches
        var lv2 = new LevelData { name = "lock", timeLimit = 60 };
        var f = ShelfData.Create(0, 0, 3, 1);
        f.layers[0][0] = new SlotData { it = 1, iid = "x", frozen = true };
        var g = ShelfData.Create(3.13f, 0, 3, 1);
        g.layers[0][0] = new SlotData { it = 1, iid = "x" };
        g.layers[0][1] = new SlotData { it = 1, iid = "x" };
        var lk = ShelfData.Create(-3.13f, 0, 3, 1); lk.lockCount = 1;
        lk.layers[0][0] = new SlotData { it = 1, iid = "y" };
        lv2.shelves.Add(f); lv2.shelves.Add(g); lv2.shelves.Add(lk);
        var b2 = Board.FromLevel(lv2);
        Check(b2.Check(0, 0, 1, 2) == MoveFail.ItemFrozen, "frozen item cannot move");
        b2.Move(1, 0, 0, 1);
        r = b2.Move(1, 1, 0, 2);
        Check(r.Matched && r.Unlocked.Contains(2) && !b2.Shelves[2].Locked, "match next to frozen item clears it and opens the lock");

        // 4. an item can slide to an empty slot of its own shelf
        var lv3 = new LevelData { name = "slide", timeLimit = 60 };
        var sl = ShelfData.Create(0, 0, 3, 1);
        sl.layers[0][0] = new SlotData { it = 1, iid = "a" };
        sl.layers[0][1] = new SlotData { it = 1, iid = "b" };
        lv3.shelves.Add(sl);
        var b3 = Board.FromLevel(lv3);
        r = b3.Move(0, 0, 0, 2);
        Check(r.Ok && b3.Shelves[0].Front[0] == null && b3.Shelves[0].Front[2].Type == "a", "same-shelf slide");
        Check(b3.Check(0, 1, 0, 2) == MoveFail.TargetOccupied, "same-shelf slide needs an empty slot");

        // 5. moving shelves: read the real game's keys, write them back, loop through the tunnels
        string moving = "{\"ld\":0,\"tl\":120,\"s\":[" +
            "{\"pos\":{\"x\":-6.26,\"y\":-2.217},\"im\":true,\"mv\":{\"x\":0.3,\"y\":0},\"sicw\":3,\"si\":[{\"rd\":[{\"it\":0},{\"it\":0},{\"it\":0}]}]}," +
            "{\"pos\":{\"x\":0,\"y\":-4.5},\"im\":true,\"mv\":{\"x\":-1,\"y\":0},\"ms\":0.25,\"sicw\":3,\"si\":[{\"rd\":[{\"it\":0},{\"it\":0},{\"it\":0}]}]}," +
            "{\"pos\":{\"x\":0,\"y\":1},\"im\":false,\"sicw\":3,\"si\":[{\"rd\":[{\"it\":0},{\"it\":0},{\"it\":0}]}]}]}";
        var mlv = LevelData.FromJson(moving);
        Check(mlv.shelves[0].moving && mlv.shelves[0].moveDir == 1 && Math.Abs(mlv.shelves[0].moveSpeed - 0.3f) < 1e-4, "early moving shelf: speed from mv");
        Check(mlv.shelves[1].moveDir == -1 && Math.Abs(mlv.shelves[1].moveSpeed - 0.25f) < 1e-4, "moving shelf: direction from mv, speed from ms");
        Check(!mlv.shelves[2].moving, "static shelf stays static");
        var mlv2 = LevelData.FromJson(mlv.ToJson());
        Check(mlv2.shelves[1].moving && mlv2.shelves[1].moveDir == -1 && Math.Abs(mlv2.shelves[1].moveSpeed - 0.25f) < 1e-4, "moving shelf round trip");
        var xs = new System.Collections.Generic.List<float> { -6.26f, -3.13f, 0f, 3.13f, 6.26f, 0f };
        var ys = new System.Collections.Generic.List<float> { -2f, -2f, -2f, -2f, -2f, 1f };
        var ws = new System.Collections.Generic.List<int> { 3, 3, 3, 3, 3, 3 };
        var mv = new System.Collections.Generic.List<bool> { true, true, true, true, true, false };
        var vs = new System.Collections.Generic.List<float> { 1f, 1f, 1f, 1f, 1f, 0f };
        float half = Conveyor.VisibleHalfWidth(xs, ws, mv);
        var rows = Conveyor.Rows(xs, ys, ws, mv, vs, half);
        Check(Math.Abs(half - Conveyor.ScreenHalfWidth) < 1e-4 && rows.Count == 1 && rows[0].Shelves.Count == 5, "one moving row of five");
        Check(Math.Abs(rows[0].Loop - 15.65f) < 1e-3, "loop length = 5 shelves");
        Check(Math.Abs(Conveyor.Wrap(6.26f + 3.13f, rows[0].Loop) - (-6.26f)) < 1e-3, "a shelf leaving right comes back left");
        Check(Conveyor.Hidden(6.26f, 3, half) && !Conveyor.Hidden(3.13f, 3, half), "outer shelves start inside the pipes");
        // the simulator's clock: where a moving shelf is after t seconds (the row slides from the start, 1 unit/s)
        var cl = new LevelData { name = "conveyor clock", timeLimit = 60 };
        for (int i = -2; i <= 2; i++)
        {
            var cs = ShelfData.Create(i * 3.13f, -2f, 3, 1);
            cs.moving = true;
            cs.moveDir = 1;
            cs.moveSpeed = 1f;
            cs.layers[0][0] = new SlotData { it = SlotData.Normal, iid = "c" + (i + 2) };
            cl.shelves.Add(cs);
        }
        var cstat = ShelfData.Create(0f, 1f, 3, 1);
        cl.shelves.Add(cstat);
        var bc = Board.FromLevel(cl);
        Check(bc.HasConveyors && Math.Abs(bc.Shelves[0].Loop - 15.65f) < 1e-3, "board keeps the conveyor loop");
        Check(!bc.OnScreen(4, 0) && bc.OnScreen(3, 0) && bc.OnScreen(5, 0), "at the start the outer shelves are in the pipes, static ones on screen");
        Check(bc.OnScreen(4, 6.26) && !bc.OnScreen(3, 6.26), "after 6.26 s the right shelf has left and the next one came out");
        Check(bc.OnScreen(0, 3.13) && bc.OnScreen(0, 3.13 + 15.65 * 3), "the row loops: same picture every loop");

        // 6. pinatas: three of a colour burst, what was inside drops into their slots and can match again
        var pl = new LevelData { name = "pinata", timeLimit = 60 };
        var pa = ShelfData.Create(0, 0, 3, 1);
        pa.layers[0][0] = SlotData.Pinata(2, "a");
        pa.layers[0][1] = SlotData.Pinata(2, "a");
        var pb = ShelfData.Create(3.13f, 0, 3, 1);
        pb.layers[0][0] = SlotData.Pinata(2, "a");
        pl.shelves.Add(pa); pl.shelves.Add(pb);
        var bp = Board.FromLevel(pl);
        Check(bp.TotalItems == 6, "a pinata with an item inside counts twice");
        r = bp.Move(1, 0, 0, 2);
        Check(r.Matched && r.Released.Count == 3 && bp.Matches == 2 && r.Won, "pinatas burst, their contents match, level won");
        var pl2 = new LevelData { name = "empty pinata", timeLimit = 60 };
        var pc = ShelfData.Create(0, 0, 3, 1);
        pc.layers[0][0] = SlotData.Pinata(1, null);
        pc.layers[0][1] = SlotData.Pinata(1, null);
        var pd = ShelfData.Create(3.13f, 0, 3, 1);
        pd.layers[0][0] = SlotData.Pinata(1, null);
        pd.layers[0][1] = SlotData.Pinata(3, null);
        pl2.shelves.Add(pc); pl2.shelves.Add(pd);
        var bp2 = Board.FromLevel(pl2);
        Check(bp2.Check(1, 0, 0, 2) == MoveFail.None, "pinatas can be moved");
        r = bp2.Move(1, 0, 0, 2);
        Check(r.Matched && r.Released.Count == 0 && bp2.Shelves[0].FrontEmpty, "empty pinatas just burst");
        var bp3 = Board.FromLevel(pl2);
        r = bp3.Move(1, 1, 0, 2);
        Check(r.Ok && !r.Matched, "pinatas of different colours do not match");

        // 7. fuse box: closed until each listed item has been matched once
        var fl = new LevelData { name = "fuse", timeLimit = 60 };
        var f1 = ShelfData.Create(-3.13f, 2, 3, 1);
        f1.layers[0][0] = new SlotData { it = 1, iid = "a" }; f1.layers[0][1] = new SlotData { it = 1, iid = "a" };
        var f2 = ShelfData.Create(0, 2, 3, 1);
        f2.layers[0][0] = new SlotData { it = 1, iid = "a" };
        var f3 = ShelfData.Create(3.13f, 2, 3, 1);
        f3.layers[0][0] = new SlotData { it = 1, iid = "b" }; f3.layers[0][1] = new SlotData { it = 1, iid = "b" };
        var f4 = ShelfData.Create(-3.13f, 0, 3, 1);
        f4.layers[0][0] = new SlotData { it = 1, iid = "b" };
        var fb = ShelfData.Create(3.13f, 0, 3, 1);
        fb.fuseItems.Add("a"); fb.fuseItems.Add("b");
        fb.layers[0][0] = new SlotData { it = 1, iid = "c" };
        fl.shelves.Add(f1); fl.shelves.Add(f2); fl.shelves.Add(f3); fl.shelves.Add(f4); fl.shelves.Add(fb);
        var bf = Board.FromLevel(fl);
        Check(bf.Check(4, 0, 1, 1) == MoveFail.SourceLocked && bf.Check(3, 0, 4, 1) == MoveFail.TargetLocked, "closed fuse box: nothing in, nothing out");
        r = bf.Move(1, 0, 0, 2);
        Check(r.Matched && r.FuseProgress.Contains(4) && r.FuseOpened.Count == 0 && bf.Shelves[4].FuseClosed, "first objective lights one window");
        r = bf.Move(3, 0, 2, 2);
        Check(r.Matched && r.FuseOpened.Contains(4) && bf.Shelves[4].CanTake, "second objective opens the fuse box");
        var flr = LevelData.FromJson(fl.ToJson());
        Check(flr.shelves[4].fuseItems.Count == 2 && flr.shelves[4].fuseItems[1] == "b", "fuse box round trip");

        // 8. iris: opens and closes after every move
        var il = new LevelData { name = "iris", timeLimit = 60 };
        var i1 = ShelfData.Create(0, 0, 3, 1);
        i1.layers[0][0] = new SlotData { it = 1, iid = "a" };
        var i2 = ShelfData.Create(3.13f, 0, 3, 1);
        i2.layers[0][0] = new SlotData { it = 1, iid = "b" };
        var iris = ShelfData.Create(-3.13f, 0, 3, 1); iris.iris = true; iris.irisClosed = true;
        iris.layers[0][0] = new SlotData { it = 1, iid = "c" };
        il.shelves.Add(i1); il.shelves.Add(i2); il.shelves.Add(iris);
        var bi = Board.FromLevel(il);
        Check(bi.Check(0, 0, 2, 1) == MoveFail.TargetLocked && bi.Check(2, 0, 0, 1) == MoveFail.SourceLocked, "closed iris blocks both ways");
        r = bi.Move(0, 0, 1, 1);
        Check(r.IrisToggled && !bi.Shelves[2].IrisClosed && bi.Check(1, 1, 2, 1) == MoveFail.None, "iris opens after a move");
        bi.Move(1, 1, 2, 1);
        Check(bi.Shelves[2].IrisClosed, "and closes after the next one");
        var ilr = LevelData.FromJson(il.ToJson());
        Check(ilr.shelves[2].iris && ilr.shelves[2].irisClosed && !ilr.shelves[0].iris, "iris round trip");

        // 9. rotating door: every cleared row turns the door to the next stage
        var rl = new LevelData { name = "ferris", timeLimit = 60 };
        var rf = ShelfData.Create(0, 0, 3, 2); rf.ferris = true;
        rf.layers[0][0] = new SlotData { it = 1, iid = "a" }; rf.layers[0][1] = new SlotData { it = 1, iid = "a" };
        rf.layers[1][0] = new SlotData { it = 1, iid = "c" };
        var ro = ShelfData.Create(3.13f, 0, 3, 1);
        ro.layers[0][0] = new SlotData { it = 1, iid = "a" };
        rl.shelves.Add(rf); rl.shelves.Add(ro);
        var br = Board.FromLevel(rl);
        Check(br.Shelves[0].FerrisStages == 2, "one stage per row");
        r = br.Move(1, 0, 0, 2);
        Check(r.Matched && r.FerrisTurned.Contains(0) && br.Shelves[0].Front[0].Type == "c", "door turns to the next row");
        Check(LevelData.FromJson(rl.ToJson()).shelves[0].ferris, "rotating door round trip");

        // 10. dragon eggs sort in threes like items
        var el = new LevelData { name = "eggs", timeLimit = 60 };
        var e1 = ShelfData.Create(0, 0, 3, 1);
        e1.layers[0][0] = SlotData.Egg(); e1.layers[0][1] = SlotData.Egg();
        var e2 = ShelfData.Create(3.13f, 0, 3, 1);
        e2.layers[0][0] = SlotData.Egg();
        el.shelves.Add(e1); el.shelves.Add(e2);
        var be = Board.FromLevel(el);
        r = be.Move(1, 0, 0, 2);
        Check(r.Matched && r.MatchedItems[0].Kind == ItemKind.Egg && r.Won, "three eggs hatch");
        var elr = LevelData.FromJson(el.ToJson());
        Check(elr.shelves[0].layers[0][0].IsEgg && elr.TypeCounts().Count == 1, "egg round trip");

        // 11. wide shelves: three neighbours match, not just any three
        var wl = new LevelData { name = "wide", timeLimit = 60 };
        var wd = ShelfData.Create(0, 0, 6, 1);
        wd.layers[0][0] = new SlotData { it = 1, iid = "a" };
        wd.layers[0][2] = new SlotData { it = 1, iid = "b" };
        wd.layers[0][3] = new SlotData { it = 1, iid = "a" };
        var wo = ShelfData.Create(0, 2, 3, 1);
        wo.layers[0][0] = new SlotData { it = 1, iid = "a" };
        wo.layers[0][1] = new SlotData { it = 1, iid = "b" };
        wo.layers[0][2] = new SlotData { it = 1, iid = "b" };
        wl.shelves.Add(wd); wl.shelves.Add(wo);
        var bw = Board.FromLevel(wl);
        r = bw.Move(1, 0, 0, 4);
        Check(r.Ok && !r.Matched, "three a's apart do not match");
        var legal = new System.Collections.Generic.List<int[]>();
        bw.LegalMoves(legal);
        Check(legal.Exists(m => m[0] == 0 && m[2] == 0 && m[1] == 0 && m[3] == 5), "a wide shelf can slide within itself");
        r = bw.Move(0, 0, 0, 5);
        Check(r.Matched && bw.Shelves[0].Front[3] == null && bw.Shelves[0].Front[2].Type == "b", "three neighbours match");

        // 12. item generator: waves fill the generated slots, then rows behind, never a ready three
        string gen = "{\"ld\":0,\"tl\":120,\"s\":[" +
            "{\"gid\":\"1\",\"pos\":{\"x\":-3.13,\"y\":0},\"sicw\":3,\"si\":[{\"rd\":[{\"it\":-1},{\"it\":-1},{\"it\":-1}]}]}," +
            "{\"gid\":\"1\",\"pos\":{\"x\":0,\"y\":0},\"sicw\":3,\"si\":[{\"rd\":[{\"it\":-1},{\"it\":0},{\"it\":-1}]}]}," +
            "{\"gid\":\"1\",\"pos\":{\"x\":3.13,\"y\":0},\"sicw\":3,\"fbi\":[\"t0\"],\"si\":[{\"rd\":[{\"it\":-1},{\"it\":-1},{\"it\":0}]}]}]," +
            "\"ig\":[{\"id\":\"1\",\"isg\":[{\"ssp\":0.8,\"slw\":15,\"blw\":5,\"rw\":1,\"hid\":0,\"icd\":[";
        for (int t = 0; t < 6; t++) gen += (t > 0 ? "," : "") + "{\"id2\":{\"it\":1,\"iid\":\"t" + t + "\"},\"ic\":3}";
        gen += ",{\"id2\":{\"it\":0},\"ic\":2}]}]}]}";
        var gl = LevelData.FromJson(gen);
        Check(gl.generators.Count == 1 && gl.GeneratorItemCount == 18, "generator waves parsed");
        bool noThree = true, fuseClean = true, same = true;
        for (int seed = 1; seed <= 20; seed++)
        {
            var gb = Board.FromLevel(gl, seed);
            if (gb.GeneratedItems != 18 || gb.TotalItems != 18) noThree = false;
            foreach (var s in gb.Shelves)
                foreach (var layer in s.Layers)
                    for (int k = 0; k + 2 < layer.Length; k++)
                        if (layer[k] != null && layer[k + 1] != null && layer[k + 2] != null &&
                            layer[k].Type == layer[k + 1].Type && layer[k + 1].Type == layer[k + 2].Type) noThree = false;
            foreach (var layer in gb.Shelves[2].Layers)
                foreach (var it in layer) if (it != null && it.Type == "t0") fuseClean = false;
            if (gb.Shelves[2].Layers.Count != 1) fuseClean = false;
            if (Signature(gb) != Signature(Board.FromLevel(gl, seed))) same = false;
        }
        Check(noThree, "all 18 generated, no row starts as a ready three");
        Check(fuseClean, "a fuse box never gets its own objectives, nor extra rows");
        Check(same, "same seed, same level");
        var glr = LevelData.FromJson(gl.ToJson());
        Check(glr.GeneratorItemCount == 18 && glr.shelves[1].gid == "1" && glr.shelves[0].layers[0][0].IsGenerated, "generator round trip");

        // 13. pinata JSON: colour and contents survive a round trip in the game's format
        string pj = "{\"ld\":0,\"tl\":120,\"s\":[{\"pos\":{\"x\":0,\"y\":0},\"sicw\":3,\"si\":[{\"rd\":[" +
            "{\"it\":5,\"iid\":\"pinata\",\"pr\":{\"pci\":4,\"pcii\":{\"it\":1,\"iid\":\"item_lego_blue\"}}}," +
            "{\"it\":5,\"iid\":\"pinata\",\"pr\":{\"pci\":2,\"pcii\":{\"it\":0}}},{\"it\":7,\"iid\":\"dragon_egg_0\"}]}]}]}";
        var pjl = LevelData.FromJson(LevelData.FromJson(pj).ToJson());
        var ps = pjl.shelves[0].layers[0];
        Check(ps[0].IsPinata && ps[0].pinataColor == 4 && ps[0].pinataContent == "item_lego_blue" && ps[1].pinataContent == null && ps[2].IsEgg,
            "pinata and egg round trip");

        // 14. auto-fill keeps a fuse box solvable: its objectives are re-picked among items outside the box
        var al = new LevelData { name = "autofill", timeLimit = 120 };
        for (int k = 0; k < 6; k++) al.shelves.Add(ShelfData.Create((k % 3 - 1) * 3.13f, k / 3, 3, 3));
        al.shelves[4].fuseItems.Add("x"); al.shelves[4].fuseItems.Add("y");
        al.generators.Add(new GeneratorData { id = "1" });
        var cat = new System.Collections.Generic.List<string>();
        for (int t = 0; t < 20; t++) cat.Add("item_m" + t + "_red");
        bool fuseOk = true;
        for (int seed = 1; seed <= 10; seed++)
        {
            var lvf = al.Clone();
            var rf2 = new FillRecipe { Seed = seed, FrontFill = 0.7, BackFill = 0.8 };
            rf2.Types = LevelGenerator.Capacity(lvf, rf2) / 3;
            var picked = LevelGenerator.PickTypes(cat, rf2.Types, 0, new Random(seed));
            if (LevelGenerator.Fill(lvf, rf2, picked) != null) { fuseOk = false; continue; }
            var box = lvf.shelves[4];
            if (box.fuseItems.Count != 2 || lvf.generators.Count != 0) fuseOk = false;
            foreach (var goal in box.fuseItems)
            {
                if (!picked.Contains(goal)) fuseOk = false;
                foreach (var l in box.layers) foreach (var c in l) if (c.IsItem && c.iid == goal) fuseOk = false;
            }
        }
        Check(fuseOk, "auto-fill: fuse objectives are items outside the box, generator dropped");

        // 15. auto-fill behind a front row filled by hand: the row stays, its types are completed to threes
        bool keepOk = true;
        for (int seed = 1; seed <= 20; seed++)
        {
            var kl = new LevelData { name = "keep", timeLimit = 120 };
            for (int k = 0; k < 9; k++) kl.shelves.Add(ShelfData.Create((k % 3 - 1) * 3.13f, 2 - k / 3, 3, 3));
            string[] hand = { "item_m0_red", "item_m0_red", "item_m1_red", "item_m2_red", null, "item_m2_red", "item_m3_red", "item_m3_red", "item_m3_red" };
            for (int k = 0; k < hand.Length; k++)
                if (hand[k] != null) { var c = kl.shelves[k / 3].layers[0][k % 3]; c.it = 1; c.iid = hand[k]; }
            string front = string.Join("|", kl.shelves.Select(q => string.Join(",", q.layers[0].Select(c => c.iid))).ToArray());
            var rk = new FillRecipe { Seed = seed, KeepRows = 1, BackFill = 0.8, Frozen = 1, Mystery = 2 };
            rk.Types = LevelGenerator.MaxTypes(kl, rk);
            if (LevelGenerator.Fill(kl, rk, LevelGenerator.PickNewTypes(cat, kl, rk, new Random(seed))) != null) { keepOk = false; continue; }
            if (front != string.Join("|", kl.shelves.Select(q => string.Join(",", q.layers[0].Select(c => c.iid))).ToArray())) keepOk = false;
            if (kl.shelves.Any(q => q.layers[0].Any(c => c.frozen || c.mystery))) keepOk = false;
            var byType = kl.shelves.SelectMany(q => q.layers).SelectMany(l => l).Where(c => c.IsItem).GroupBy(c => c.iid).ToList();
            if (byType.Any(grp => grp.Count() % 3 != 0) || byType.Count != rk.Types) keepOk = false;
        }
        Check(keepOk, "auto-fill keeps a hand-made front row and completes its types to threes");

        // 16. boosters on a case level after some moves: vacuum takes one set, the magic hat keeps every item and leaves
        //     ready matches, the painter turns whole sets into aliens
        var bl = LevelData.FromJson(File.ReadAllText(Path.Combine(args[0], "Medium.json")));
        bool vacOk = true, hatOk = true, paintOk = true;
        var brng = new Random(5);
        var bmoves = new System.Collections.Generic.List<int[]>();
        for (int game = 0; game < 30; game++)
        {
            var bb = Board.FromLevel(bl, game + 1);
            for (int i = 0; i < 25; i++)
            {
                bb.LegalMoves(bmoves);
                if (bmoves.Count == 0) break;
                var bmv = bmoves[brng.Next(bmoves.Count)];
                if (bb.Move(bmv[0], bmv[1], bmv[2], bmv[3]).Won) break;
            }
            var v = bb.Clone(); var vr = new MoveResult();
            if (v.Vacuum(new Random(game), vr))
                vacOk &= vr.MatchedItems.Count == 3 && vr.MatchedItems.Select(x => x.Type).Distinct().Count() == 1 && v.RemainingItems == bb.RemainingItems - 3;
            var h = bb.Clone(); var hr = new MoveResult();
            if (h.Shuffle(new Random(game), hr))
            {
                var before = bb.Shelves.SelectMany(q => q.Layers).SelectMany(l => l).Where(x => x != null).Select(x => x.Uid).OrderBy(x => x).ToList();
                var after = h.Shelves.SelectMany(q => q.Layers).SelectMany(l => l).Where(x => x != null).Select(x => x.Uid).OrderBy(x => x).ToList();
                hatOk &= hr.Matched || before.SequenceEqual(after);
                hatOk &= h.HasAnyMove() || h.IsWon;
            }
            var pnt = bb.Clone(); var pr = new MoveResult();
            if (pnt.Paint(new Random(game), pr))
            {
                paintOk &= pr.Painted.Count > 0 && pr.Painted.Count <= 9 && pr.Painted.Count % 3 == 0;
                var counts = pnt.Shelves.SelectMany(q => q.Layers).SelectMany(l => l).Where(x => x != null).GroupBy(x => x.Type);
                paintOk &= counts.All(grp => grp.Count() % 3 == 0 || grp.Key.StartsWith("pinata"));
            }
        }
        Check(vacOk, "vacuum takes the three copies of one type");
        Check(hatOk, "magic hat keeps every item and leaves a move");
        Check(paintOk, "painter turns whole sets into aliens");

        // 17. no match left: one free slot, eight different items on the front rows, nothing can be dug out
        var nm = new LevelData { name = "nomatch", timeLimit = 60 };
        for (int k = 0; k < 3; k++) nm.shelves.Add(ShelfData.Create((k - 1) * 3.13f, 0, 3, 2));
        string[] fronts = { "a", "b", "c", "d", "e", "f", "g", "h", null };
        string[] backs = { "a", "a", "b", "b", "c", "c", "d", "d", "e" };
        for (int k = 0; k < 9; k++)
        {
            if (fronts[k] != null) { var c = nm.shelves[k / 3].layers[0][k % 3]; c.it = 1; c.iid = "item_" + fronts[k] + "_red"; }
            var cb = nm.shelves[k / 3].layers[1][k % 3]; cb.it = 1; cb.iid = "item_" + backs[k] + "_red";
        }
        var nb = Board.FromLevel(nm);
        bool noMatch = nb.HasAnyMove() && !nb.MatchPossible();
        // two more free slots: the first shelf's front fits elsewhere, so its back row (a, a, b) can be dug out
        var nm2 = nm.Clone(); nm2.shelves[1].layers[0][1].Clear(); nm2.shelves[1].layers[0][2].Clear();
        Check(noMatch && Board.FromLevel(nm2).MatchPossible(), "no match left is detected (and digging counts as a way out)");

        // 15. Problems(): what keeps a level from being published
        var okLevel = LevelData.FromJson(File.ReadAllText(Path.Combine(args[0], "Easy.json")));
        Check(okLevel.Problems().Count == 0, "case level has no problems");
        okLevel.shelves[0].layers[0][0] = new SlotData { it = 1, iid = "item_lonely" };
        okLevel.shelves[1].fuseItems.Add("item_missing");
        var probs = okLevel.Problems();
        Check(probs.Exists(x => x.StartsWith("item_lonely")) && probs.Exists(x => x.Contains("item_missing")), "lonely item and missing fuse goal are reported");
        Check(gl.Problems().Count == 0, "generator items count towards multiples of 3");

        Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
        return fails == 0 ? 0 : 1;
    }
}
