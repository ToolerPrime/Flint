/* The .flint project format.
 *
 * A zip holding a JSON manifest plus one PNG per raster object. Zip and PNG
 * were chosen so the format stays inspectable with ordinary tools and so no
 * image codec has to be written here — and so a future reader can skip object
 * kinds it does not understand instead of failing.
 *
 *   project.json          manifest, versioned
 *   images/<n>.png        raster payloads, lossless BGRA
 *   audio/<id>.wav        audio sources, 48 kHz stereo 32-bit float
 *
 * Saving is atomic: the zip is built beside the target and moved into place
 * only once it is complete, so a crash mid-save cannot destroy the previous
 * version of the file.
 */
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Flint;

public static class FlintFormat
{
    /// <summary>
    /// 1: layers, frames, tags, raster objects.
    /// 2: adds audio (sources, tracks, clips).
    /// 3: adds per-frame pivot and hitboxes.
    /// 4: adds the library and lossless transformed objects (original + matrix).
    /// 5: adds traced vector objects (vector art + matrix).
    /// 6: gradient fills, live effects.
    /// 7: layer masks (per cel image, per layer enabled/linked flags).
    /// 8: ruler guides (a "guides" list of {vertical, pos}).
    /// 9: editable primitive and text source properties on rendered objects.
    /// 10: nested logical group paths on objects.
    /// Older versions load unchanged; their missing fields take defaults.
    /// </summary>
    public const int CurrentVersion = 10;   // 8: guides; 7: layer masks; 6: gradient fills, live effects; 5: traced vector objects; 4: library, lossless transformed objects
    public const string Extension = ".flint";

    public sealed class LoadResult
    {
        public Document? Document;
        public string? Error;
        public readonly List<string> Warnings = new();
        public bool Ok => Document != null;
    }

    // ---------- save ----------

    public static void Save(Document doc, string path)
    {
        string temp = path + ".saving-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                int imageSeq = 0;
                var manifest = new JsonObject
                {
                    ["format"] = CurrentVersion,
                    ["app"] = "Flint",
                    ["width"] = doc.Width,
                    ["height"] = doc.Height,
                };

                var frames = new JsonArray();
                foreach (var f in doc.Frames)
                {
                    var fn = new JsonObject { ["delay"] = f.Delay, ["name"] = f.Name };
                    if (f.Pivot is { } pv) fn["pivot"] = new JsonObject { ["x"] = pv.X, ["y"] = pv.Y };
                    if (f.Hitboxes.Count > 0)
                        fn["hitboxes"] = new JsonArray(f.Hitboxes.Select(h => (JsonNode)new JsonObject
                        {
                            ["name"] = h.Name, ["x"] = h.Rect.X, ["y"] = h.Rect.Y,
                            ["w"] = h.Rect.Width, ["h"] = h.Rect.Height,
                        }).ToArray());
                    frames.Add(fn);
                }
                manifest["frames"] = frames;

                var tags = new JsonArray();
                foreach (var t in doc.Tags)
                    tags.Add(new JsonObject
                    {
                        ["name"] = t.Name, ["start"] = t.Start, ["end"] = t.End,
                        ["colour"] = $"#{t.Colour.R:X2}{t.Colour.G:X2}{t.Colour.B:X2}",
                    });
                manifest["tags"] = tags;
                if (doc.Guides.Count > 0) manifest["guides"] = GuideSet.ToJson(doc.Guides);

                var layers = new JsonArray();
                foreach (var l in doc.Layers)
                {
                    var cels = new JsonArray();
                    foreach (var (frameKey, cel) in l.Cels.OrderBy(k => k.Key))
                    {
                        var objects = new JsonArray();
                        foreach (var o in cel.Objects)
                        {
                            var node = new JsonObject
                            {
                                ["kind"] = o.Kind.ToString(),
                                ["name"] = o.Name,
                                ["visible"] = o.Visible,
                                ["opacity"] = o.Opacity,
                                ["blend"] = o.Blend.ToString(),
                                ["x"] = o.X,
                                ["y"] = o.Y,
                            };
                            if (o is RasterObject r)
                            {
                                string entry = $"images/{imageSeq++:D5}.png";
                                node["image"] = entry;
                                node["w"] = r.Width;
                                node["h"] = r.Height;
                                WritePng(zip, entry, r.Pixels);
                                if (r.GroupPath.Count > 0) node["groups"] = new JsonArray(r.GroupPath.Select(id => (JsonNode)id).ToArray());
                                if (r.Primitive != null) node["primitive"] = r.Primitive.ToJson();
                                if (r.TextSource != null) node["textSource"] = r.TextSource.ToJson();
                                if (r.Original != null || r.Vector != null)
                                {
                                    if (r.Original != null)
                                    {
                                        string oe = $"images/{imageSeq++:D5}.png";
                                        WritePng(zip, oe, r.Original);
                                        node["original"] = oe;
                                    }
                                    if (r.Vector != null) node["vector"] = VectorToJson(r.Vector);
                                    node["matrix"] = new JsonArray(r.Matrix.ToArray().Select(v => (JsonNode)v).ToArray());
                                    node["resample"] = r.Mode.ToString();
                                    if (r.Effects is { Count: > 0 }) node["effects"] = EffectsToJson(r.Effects);
                                }
                            }
                            objects.Add(node);
                        }
                        var celNode = new JsonObject { ["frame"] = frameKey, ["objects"] = objects };
                        if (cel.Mask != null)
                        {
                            string me2 = $"images/{imageSeq++:D5}.png";
                            WritePng(zip, me2, cel.Mask.Pixels);
                            celNode["mask"] = me2;
                        }
                        cels.Add(celNode);
                    }

                    var layerNode = new JsonObject
                    {
                        ["name"] = l.Name,
                        ["visible"] = l.Visible,
                        ["locked"] = l.Locked,
                        ["shared"] = l.Shared,
                        ["opacity"] = l.Opacity,
                        ["blend"] = l.Blend.ToString(),
                        ["cels"] = cels,
                    };
                    if (!l.MaskEnabled) layerNode["maskEnabled"] = false;
                    if (!l.MaskLinked) layerNode["maskLinked"] = false;
                    layers.Add(layerNode);
                }
                manifest["layers"] = layers;
                manifest["audio"] = SaveAudio(doc.Audio, zip);
                var lib = new JsonArray();
                foreach (var item in doc.Library)
                {
                    string le = $"library/{lib.Count:D4}.png";
                    WritePng(zip, le, item.Pixels);
                    lib.Add(new JsonObject { ["name"] = item.Name, ["image"] = le, ["source"] = item.Source });
                }
                manifest["library"] = lib;

                var me = zip.CreateEntry("project.json", CompressionLevel.Optimal);
                using var ws = new StreamWriter(me.Open());
                ws.Write(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }

            // Only now replace the real file.
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            throw;
        }
    }

    // ---------- load ----------

    public static LoadResult Load(string path)
    {
        var result = new LoadResult();
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var manifestEntry = zip.GetEntry("project.json");
            if (manifestEntry == null)
            {
                result.Error = "not a Flint project: project.json is missing";
                return result;
            }

            JsonNode? root;
            using (var rs = new StreamReader(manifestEntry.Open()))
                root = JsonNode.Parse(rs.ReadToEnd());
            if (root == null) { result.Error = "project.json is not valid JSON"; return result; }

            int version = root["format"]?.GetValue<int>() ?? 0;
            if (version <= 0) { result.Error = "project.json has no format version"; return result; }
            if (version > CurrentVersion)
            {
                result.Error = $"this project was written by a newer Flint (format {version}, " +
                               $"this build understands {CurrentVersion})";
                return result;
            }

            var doc = new Document
            {
                Width = root["width"]?.GetValue<int>() ?? 0,
                Height = root["height"]?.GetValue<int>() ?? 0,
            };
            if (doc.Width <= 0 || doc.Height <= 0) { result.Error = "canvas size is missing"; return result; }

            foreach (var f in root["frames"]?.AsArray() ?? new JsonArray())
            {
                var spec = new FrameSpec
                {
                    Delay = f?["delay"]?.GetValue<int>() ?? 100,
                    Name = f?["name"]?.GetValue<string>() ?? "",
                };
                if (f?["pivot"] is JsonNode pv)
                    spec.Pivot = new Point(pv["x"]?.GetValue<int>() ?? 0, pv["y"]?.GetValue<int>() ?? 0);
                foreach (var hn in f?["hitboxes"]?.AsArray() ?? new JsonArray())
                    if (hn != null)
                        spec.Hitboxes.Add(new Hitbox
                        {
                            Name = hn["name"]?.GetValue<string>() ?? "hit",
                            Rect = new Rectangle(hn["x"]?.GetValue<int>() ?? 0, hn["y"]?.GetValue<int>() ?? 0,
                                                 hn["w"]?.GetValue<int>() ?? 0, hn["h"]?.GetValue<int>() ?? 0),
                        });
                doc.Frames.Add(spec);
            }
            if (doc.Frames.Count == 0) doc.Frames.Add(new FrameSpec());

            foreach (var t in root["tags"]?.AsArray() ?? new JsonArray())
                doc.Tags.Add(new AnimTag
                {
                    Name = t?["name"]?.GetValue<string>() ?? "Tag",
                    Start = t?["start"]?.GetValue<int>() ?? 0,
                    End = t?["end"]?.GetValue<int>() ?? 0,
                    Colour = ParseColour(t?["colour"]?.GetValue<string>()),
                });

            doc.Guides = GuideSet.FromJson(root["guides"] as JsonArray);

            foreach (var ln in root["layers"]?.AsArray() ?? new JsonArray())
            {
                if (ln == null) continue;
                var layer = new Layer
                {
                    Name = ln["name"]?.GetValue<string>() ?? "Layer",
                    Visible = ln["visible"]?.GetValue<bool>() ?? true,
                    Locked = ln["locked"]?.GetValue<bool>() ?? false,
                    Shared = ln["shared"]?.GetValue<bool>() ?? false,
                    Opacity = (byte)(ln["opacity"]?.GetValue<int>() ?? 255),
                    Blend = ParseBlend(ln["blend"]?.GetValue<string>(), result),
                    MaskEnabled = ln["maskEnabled"]?.GetValue<bool>() ?? true,
                    MaskLinked = ln["maskLinked"]?.GetValue<bool>() ?? true,
                };

                foreach (var cn in ln["cels"]?.AsArray() ?? new JsonArray())
                {
                    if (cn == null) continue;
                    int frameKey = cn["frame"]?.GetValue<int>() ?? 0;
                    var cel = new Cel();
                    foreach (var on in cn["objects"]?.AsArray() ?? new JsonArray())
                    {
                        if (on == null) continue;
                        string kind = on["kind"]?.GetValue<string>() ?? "Raster";
                        if (kind != "Raster")
                        {
                            // Forward compatibility: keep going rather than
                            // refusing the whole file, but never pretend the
                            // object survived.
                            result.Warnings.Add($"dropped a '{kind}' object: this build only reads raster objects");
                            continue;
                        }

                        string? entryName = on["image"]?.GetValue<string>();
                        var entry = entryName == null ? null : zip.GetEntry(entryName);
                        if (entry == null)
                        {
                            result.Warnings.Add($"missing image data for '{on["name"]?.GetValue<string>()}'");
                            continue;
                        }

                        var bmp = ReadPng(entry);
                        Bitmap? original = null;
                        if (on["original"]?.GetValue<string>() is string on2 && zip.GetEntry(on2) is { } oentry)
                            original = ReadPng(oentry);

                        cel.Objects.Add(new RasterObject(bmp)
                        {
                            Original = original,
                            GroupPath = ObjectGroups.FromJson(on["groups"] as JsonArray),
                            Primitive = PrimitiveSpec.FromJson(on["primitive"] as JsonObject),
                            TextSource = TextSource.FromJson(on["textSource"] as JsonObject),
                            Vector = on["vector"] is JsonObject vn ? VectorFromJson(vn) : null,
                            Effects = on["effects"] is JsonArray fxa ? EffectsFromJson(fxa) : null,
                            Matrix = on["matrix"] is JsonArray ma && ma.Count == 6
                                ? Affine.FromArray(ma.Select(v => v!.GetValue<double>()).ToArray()) : Affine.Identity,
                            Mode = Enum.TryParse<Resample>(on["resample"]?.GetValue<string>(), out var rm) ? rm : Resample.Nearest,
                            Name = on["name"]?.GetValue<string>() ?? "Bitmap",
                            Visible = on["visible"]?.GetValue<bool>() ?? true,
                            Opacity = (byte)(on["opacity"]?.GetValue<int>() ?? 255),
                            Blend = ParseBlend(on["blend"]?.GetValue<string>(), result),
                            X = on["x"]?.GetValue<int>() ?? 0,
                            Y = on["y"]?.GetValue<int>() ?? 0,
                        });
                    }
                    if (cn["mask"]?.GetValue<string>() is string mn && zip.GetEntry(mn) is { } mentry)
                    {
                        var mb = ReadPng(mentry);
                        if (mb.Width == doc.Width && mb.Height == doc.Height)
                        {
                            // normalise to the in-memory form: grey (G times A) in all three channels, A = 255
                            var mpx = ImageOps.GetPixels(mb);
                            for (int i = 0; i < mpx.Length; i += 4) { byte g = (byte)(mpx[i + 1] * mpx[i + 3] / 255); mpx[i] = mpx[i + 1] = mpx[i + 2] = g; mpx[i + 3] = 255; }
                            cel.Mask = new RasterObject(ImageOps.FromPixels(mb.Width, mb.Height, mpx)) { Name = "Mask" };
                        }
                        else result.Warnings.Add($"a layer mask on '{layer.Name}' does not match the canvas size and was dropped");
                    }
                    else if (cn["mask"] != null) result.Warnings.Add($"a layer mask on '{layer.Name}' is missing from the file");
                    layer.Cels[frameKey] = cel;
                }
                doc.Layers.Add(layer);
            }

            if (doc.Layers.Count == 0) doc.Layers.Add(new Layer { Name = "Layer 1" });
            if (root["audio"] is JsonNode audioNode) LoadAudio(audioNode, zip, doc.Audio, result);
            foreach (var ln2 in root["library"]?.AsArray() ?? new JsonArray())
            {
                if (ln2?["image"]?.GetValue<string>() is not string li || zip.GetEntry(li) is not { } le)
                {
                    result.Warnings.Add("a library image is missing from the file");
                    continue;
                }
                doc.Library.Add(new LibraryItem(ln2["name"]?.GetValue<string>() ?? "Image", ReadPng(le), ln2["source"]?.GetValue<string>()));
            }
            result.Document = doc;
            return result;
        }
        catch (InvalidDataException)
        {
            result.Error = "not a readable Flint project (the file is not a valid zip)";
            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    private static JsonObject SaveAudio(AudioProject audio, ZipArchive zip)
    {
        var sources = new JsonArray();
        foreach (var src in audio.Sources.Values.OrderBy(v => v.Id))
        {
            string entry = $"audio/{src.Id:D5}.wav";
            // WAV needs a seekable stream to finish its header; zip entries are not.
            using (var ms = new MemoryStream())
            {
                AudioIO.WriteFloatWav(ms, src.Samples);
                ms.Position = 0;
                using var es = zip.CreateEntry(entry, CompressionLevel.Optimal).Open();
                ms.CopyTo(es);
            }
            sources.Add(new JsonObject
            {
                ["id"] = src.Id, ["name"] = src.Name, ["file"] = entry,
                ["original"] = src.OriginalPath,
            });
        }

        var tracks = new JsonArray();
        foreach (var t in audio.Tracks)
        {
            var clips = new JsonArray();
            foreach (var c in t.Clips)
                clips.Add(new JsonObject
                {
                    ["source"] = c.SourceId, ["name"] = c.Name,
                    ["start"] = c.StartMs, ["in"] = c.InMs, ["length"] = c.LengthMs,
                    ["gain"] = c.Gain, ["fadeIn"] = c.FadeInMs, ["fadeOut"] = c.FadeOutMs,
                    ["loop"] = c.Loop,
                });
            tracks.Add(new JsonObject
            {
                ["name"] = t.Name, ["kind"] = t.Kind.ToString(),
                ["muted"] = t.Muted, ["solo"] = t.Solo, ["volume"] = t.Volume,
                ["clips"] = clips,
            });
        }
        return new JsonObject { ["nextSourceId"] = audio.NextSourceId, ["sources"] = sources, ["tracks"] = tracks };
    }

    private static void LoadAudio(JsonNode node, ZipArchive zip, AudioProject audio, LoadResult result)
    {
        audio.NextSourceId = node["nextSourceId"]?.GetValue<int>() ?? 1;
        foreach (var sn in node["sources"]?.AsArray() ?? new JsonArray())
        {
            if (sn == null) continue;
            int id = sn["id"]?.GetValue<int>() ?? 0;
            string name = sn["name"]?.GetValue<string>() ?? "Audio";
            var entry = sn["file"]?.GetValue<string>() is string f ? zip.GetEntry(f) : null;
            if (entry == null) { result.Warnings.Add($"audio '{name}' is missing its data"); continue; }
            using var es = entry.Open();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            ms.Position = 0;
            audio.Sources[id] = new AudioSource
            {
                Id = id, Name = name, Samples = AudioIO.ReadWav(ms),
                OriginalPath = sn["original"]?.GetValue<string>(),
            };
            audio.NextSourceId = Math.Max(audio.NextSourceId, id + 1);
        }

        foreach (var tn in node["tracks"]?.AsArray() ?? new JsonArray())
        {
            if (tn == null) continue;
            var t = new AudioTrack
            {
                Name = tn["name"]?.GetValue<string>() ?? "Audio",
                Kind = Enum.TryParse<TrackKind>(tn["kind"]?.GetValue<string>(), out var k) ? k : TrackKind.Dialogue,
                Muted = tn["muted"]?.GetValue<bool>() ?? false,
                Solo = tn["solo"]?.GetValue<bool>() ?? false,
                Volume = tn["volume"]?.GetValue<float>() ?? 1f,
            };
            foreach (var cn in tn["clips"]?.AsArray() ?? new JsonArray())
            {
                if (cn == null) continue;
                var c = new AudioClip
                {
                    SourceId = cn["source"]?.GetValue<int>() ?? 0,
                    Name = cn["name"]?.GetValue<string>() ?? "Clip",
                    StartMs = cn["start"]?.GetValue<double>() ?? 0,
                    InMs = cn["in"]?.GetValue<double>() ?? 0,
                    LengthMs = cn["length"]?.GetValue<double>() ?? 0,
                    Gain = cn["gain"]?.GetValue<float>() ?? 1f,
                    FadeInMs = cn["fadeIn"]?.GetValue<double>() ?? 0,
                    FadeOutMs = cn["fadeOut"]?.GetValue<double>() ?? 0,
                    Loop = cn["loop"]?.GetValue<bool>() ?? false,
                };
                if (!audio.Sources.ContainsKey(c.SourceId))
                    result.Warnings.Add($"clip '{c.Name}' refers to missing audio; kept but silent");
                t.Clips.Add(c);
            }
            audio.Tracks.Add(t);
        }
    }

    private static Color ParseColour(string? s)
    {
        if (s != null && s.StartsWith('#') && s.Length == 7
            && int.TryParse(s[1..], System.Globalization.NumberStyles.HexNumber, null, out int v))
            return Color.FromArgb(255, (v >> 16) & 255, (v >> 8) & 255, v & 255);
        return Color.FromArgb(40, 170, 120);
    }

    private static JsonObject VectorToJson(VectorArt a)
    {
        var shapes = new JsonArray();
        foreach (var sh in a.Shapes)
        {
            var contours = new JsonArray();
            foreach (var c in sh.Contours)
                contours.Add(new JsonArray(c.SelectMany(p => new[] { p.X, p.Y }).Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()));
            var shn = new JsonObject { ["c"] = sh.Colour, ["contours"] = contours };
            if (sh.Gradient is { } g)
                shn["grad"] = new JsonObject
                {
                    ["k"] = (int)g.Kind,
                    ["pos"] = new JsonArray(g.Pos.Select(v => (JsonNode)JsonValue.Create(double.IsFinite(v) ? v : 0)!).ToArray()),
                    ["col"] = new JsonArray(g.Col.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray()),
                    ["pts"] = new JsonArray(new[] { g.X0, g.Y0, g.X1, g.Y1, g.X2, g.Y2 }.Select(v => (JsonNode)JsonValue.Create(double.IsFinite(v) ? v : 0)!).ToArray()),
                };
            shapes.Add(shn);
        }
        var o = new JsonObject { ["w"] = a.W, ["h"] = a.H, ["shapes"] = shapes };
        if (a.KeepOrder) o["keepOrder"] = true;   // optional: older files lack it
        return o;
    }

    private static JsonArray EffectsToJson(List<ObjectEffect> fx)
    {
        var a = new JsonArray();
        foreach (var e in fx)
        {
            var p = new JsonObject();
            foreach (var (k, v) in e.P) if (double.IsFinite(v)) p[k] = v;   // JSON cannot hold NaN/Infinity
            a.Add(new JsonObject { ["kind"] = e.Kind.ToString(), ["p"] = p });
        }
        return a;
    }

    private static List<ObjectEffect> EffectsFromJson(JsonArray a)
    {
        var l = new List<ObjectEffect>();
        foreach (var n in a)
        {
            if (n == null || !Enum.TryParse<EffectKind>(n["kind"]?.GetValue<string>(), out var k)) continue;
            var e = new ObjectEffect(k);
            if (n["p"] is JsonObject po) foreach (var (key, v) in po) if (v != null) e.P[key] = v.GetValue<double>();
            l.Add(e);
        }
        return l;
    }

    private static VectorArt VectorFromJson(JsonObject n)
    {
        var a = new VectorArt { W = n["w"]?.GetValue<int>() ?? 0, H = n["h"]?.GetValue<int>() ?? 0, KeepOrder = n["keepOrder"]?.GetValue<bool>() ?? false };
        foreach (var sn in n["shapes"]?.AsArray() ?? new JsonArray())
        {
            var sh = new VShape { Colour = sn!["c"]!.GetValue<uint>() };
            foreach (var cn in sn["contours"]!.AsArray())
            {
                var v = cn!.AsArray().Select(x => x!.GetValue<double>()).ToArray();
                var poly = new List<VPt>();
                for (int i = 0; i + 1 < v.Length; i += 2) poly.Add(new VPt(v[i], v[i + 1]));
                sh.Contours.Add(poly);
            }
            if (sn["grad"] is JsonObject gn)
            {
                var pt = gn["pts"]?.AsArray().Select(x => x!.GetValue<double>()).ToArray() ?? Array.Empty<double>();
                var pos = gn["pos"]?.AsArray().Select(x => x!.GetValue<double>()).ToArray() ?? Array.Empty<double>();
                var col = gn["col"]?.AsArray().Select(x => x!.GetValue<uint>()).ToArray() ?? Array.Empty<uint>();
                int k = gn["k"]?.GetValue<int>() ?? -1;
                // A damaged gradient falls back to the shape's flat colour instead of crashing later.
                if (pt.Length >= 6 && pos.Length > 0 && pos.Length == col.Length && Enum.IsDefined(typeof(GradientKind), k)
                    && pt.Concat(pos).All(double.IsFinite))
                    sh.Gradient = new VGradient
                    {
                        Kind = (GradientKind)k, Pos = pos, Col = col,
                        X0 = pt[0], Y0 = pt[1], X1 = pt[2], Y1 = pt[3], X2 = pt[4], Y2 = pt[5],
                    };
            }
            a.Shapes.Add(sh);
        }
        return a;
    }

    private static void WritePng(ZipArchive zip, string name, Bitmap bmp)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = e.Open();
        using var ms = new MemoryStream();   // PNG encoding needs a seekable stream
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        ms.CopyTo(s);
    }

    private static Bitmap ReadPng(ZipArchiveEntry entry)
    {
        using var es = entry.Open();
        using var ms = new MemoryStream();
        es.CopyTo(ms);
        ms.Position = 0;
        using var loaded = new Bitmap(ms);
        return ImageOps.ReadExact(loaded);   // raw copy: drawing would round partial alpha
    }

    private static BlendMode ParseBlend(string? s, LoadResult result)
    {
        if (s == null) return BlendMode.Normal;
        if (Enum.TryParse<BlendMode>(s, out var m)) return m;
        result.Warnings.Add($"unknown blend mode '{s}', using Normal");
        return BlendMode.Normal;
    }
}
