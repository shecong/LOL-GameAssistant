using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using LOL_GameAssistant.Domain.GameData;
using Tesseract;

// Offline comparison only. Client rectangles are annotated from the supplied screenshots.
// Narrow title ROI is a hypothesis evaluated on this small sample, not a production detector.
string root = Path.GetFullPath(args.ElementAtOrDefault(0) ?? ".");
string output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "tools/OcrBenchmark/results");
Directory.CreateDirectory(output);
var samples = JsonSerializer.Deserialize<Sample[]>(File.ReadAllText(Path.Combine(root, "tools/OcrBenchmark/samples.json")))!;
using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "LOL-GameAssistant/Resources/augment-names.json")));
var entries = catalog.RootElement.EnumerateObject().Where(p => int.Parse(p.Name) >= 1000).ToArray();
var names = entries.SelectMany(p => new[] { (int.Parse(p.Name), p.Value.GetProperty("name").GetString()!), (int.Parse(p.Name), p.Value.TryGetProperty("englishName", out var en) ? en.GetString() ?? "" : "") }).ToArray();
var labels = entries.ToDictionary(p => int.Parse(p.Name), p => p.Value.GetProperty("name").GetString()!);
using var engine = new TesseractEngine(Path.Combine(root, "tools/OcrBenchmark/tessdata"), "chi_sim+eng", EngineMode.LstmOnly);
var rows = new List<Result>();


foreach (var sample in samples)
{
    using var source = new Bitmap(sample.Path);
    using var client = source.Clone(new Rectangle(sample.X, sample.Y, sample.Width, sample.Height), PixelFormat.Format24bppRgb);
    var seen = new List<int>();
    for (int card = 0; card < 3; card++)
    {
        int center = new[] {31, 50, 69}[card];
        var watch = Stopwatch.StartNew();
        var readings = new List<Reading>();
        var candidates = new Dictionary<int, AugmentNameMatch>();
        AugmentNameMatch? best = null;
        foreach (var band in new[] {(38,8), (32,10), (44,10), (28,10), (50,10)})
        {
            // Reproduce scanner's integer region/crop arithmetic and preprocessing order.
            int regionLeft = client.Width / 6, regionWidth = client.Width * 2 / 3;
            int width = client.Width * 19 / 100;
            int left = regionLeft + Math.Clamp(client.Width * center / 100 - regionLeft - width / 2, 0, regionWidth - width);
            int regionTop = client.Height * 28 / 100;
            int top = client.Height * band.Item1 / 100 - regionTop;
            int height = Math.Min(client.Height * band.Item2 / 100, client.Height * 32 / 100 - top);
            using var scaled = Scale(client, new Rectangle(left, regionTop + top, width, height));
            foreach (var pass in new[] {("raw",PageSegMode.SparseText), ("contrast",PageSegMode.SingleBlock), ("fixed150",PageSegMode.SparseText)})
            {
                using var prepared = Prepare(scaled, pass.Item1);
                var reading = Read(prepared, pass.Item2); readings.Add(reading);
                var match = AugmentNameMatcher.MatchCard(reading.Text, names);
                if (match != null && !seen.Contains(match.Id))
                {
                    if (!candidates.TryGetValue(match.Id, out var old) || match.Score > old.Score) candidates[match.Id] = match;
                    if (best == null || match.Score > best.Score) best = match;
                }
                if (best?.Score == 1) break;
            }
            if (best?.Score == 1) break;
        }
        var ranked = candidates.Values.OrderByDescending(p => p.Score).Take(2).ToArray();
        if (ranked.Length > 1 && ranked[0].Score - ranked[1].Score < .08) best = null;
        if (best != null) seen.Add(best.Id);
        rows.Add(Make("baseline", sample, card, best, readings, watch.Elapsed.TotalMilliseconds));
        foreach (int widthPercent in new[] {15, 10})
        foreach (string preparation in new[] {"raw", "contrast", "fixed150", "adaptive"})
        foreach (var mode in new[] {PageSegMode.SparseText, PageSegMode.SingleLine, PageSegMode.SingleBlock})
        {
            watch.Restart();
            int width = client.Width * widthPercent / 100;
            var crop = new Rectangle(client.Width * center / 100 - width / 2, client.Height * 40 / 100, width, client.Height * 3 / 100);
            using var scaled = Scale(client, crop);
            using var prepared = Prepare(scaled, preparation);
            var reading = Read(prepared, mode);
            var match = AugmentNameMatcher.MatchCard(reading.Text, names);
            rows.Add(Make($"{(widthPercent == 15 ? "narrow" : "tight")}-{preparation}-{mode}", sample, card, match, [reading], watch.Elapsed.TotalMilliseconds));
        }
    }
    Console.WriteLine($"Completed {sample.Name}: {source.Width}x{source.Height}");
}
// Exploratory combination selected after inspecting the initial sample results.
foreach (var group in rows.Where(r => r.Method is "narrow-raw-SparseText" or "narrow-fixed150-SingleLine").GroupBy(r => (r.Sample,r.Card)))
{
    var raw = group.First(r => r.Method == "narrow-raw-SparseText");
    var fallback = group.First(r => r.Method == "narrow-fixed150-SingleLine");
    rows.Add(raw.Actual != null ? raw with {Method="narrow-raw-then-fixed"} : fallback with {Method="narrow-raw-then-fixed",Milliseconds=raw.Milliseconds+fallback.Milliseconds});
}
File.WriteAllText(Path.Combine(output, "details.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions {WriteIndented=true}));


var summary = rows.GroupBy(r => r.Method).Select(g => new {Method=g.Key, Correct=g.Count(r=>r.Actual==r.Expected), Wrong=g.Count(r=>r.Actual!=null && r.Actual!=r.Expected), Missed=g.Count(r=>r.Actual==null), ExactText=g.Count(r=>r.Readings.Any(t=>Normalize(t.Text)==Normalize(r.Expected))), MeanMs=Math.Round(g.Average(r=>r.Milliseconds),1)}).ToArray();
File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions {WriteIndented=true}));
foreach(var s in summary) Console.WriteLine($"{s.Method}: correct {s.Correct}/12, wrong {s.Wrong}, missed {s.Missed}, exact text {s.ExactText}, mean {s.MeanMs}ms/card");

Reading Read(Bitmap bitmap, PageSegMode mode)
{
    using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
    using var pix = Pix.LoadFromMemory(stream.ToArray()); using var page = engine.Process(pix,mode);
    return new Reading(page.GetText(), page.GetMeanConfidence());
}
Result Make(string method, Sample sample, int card, AugmentNameMatch? match, List<Reading> readings, double ms) =>
    new(method,sample.Name,sample.Tier,card+1,sample.Expected[card],match==null?null:labels[match.Id],match?.Score,readings,ms);
static string Normalize(string text) => new(text.Where(char.IsLetterOrDigit).ToArray());
static Bitmap Scale(Bitmap image, Rectangle crop)
{
    int scale = image.Height < 900 ? 3 : 2;
    var result = new Bitmap(crop.Width*scale,crop.Height*scale,PixelFormat.Format24bppRgb);
    using var g=Graphics.FromImage(result); g.InterpolationMode=InterpolationMode.HighQualityBicubic;
    g.DrawImage(image,new Rectangle(0,0,result.Width,result.Height),crop,GraphicsUnit.Pixel); return result;
}
static Bitmap Prepare(Bitmap image,string method)
{
    var result=(Bitmap)image.Clone(); if(method=="raw")return result;
    int w=image.Width,h=image.Height; var lum=new int[w*h]; var integral=new long[(w+1)*(h+1)];
    for(int y=0;y<h;y++)for(int x=0;x<w;x++)
    {
        var c=image.GetPixel(x,y); int v=(c.R*299+c.G*587+c.B*114)/1000; lum[y*w+x]=v;
        integral[(y+1)*(w+1)+x+1]=v+integral[y*(w+1)+x+1]+integral[(y+1)*(w+1)+x]-integral[y*(w+1)+x];
    }
    for(int y=0;y<h;y++)for(int x=0;x<w;x++)
    {
        int l=lum[y*w+x]; int value;
        if(method=="contrast")value=255-Math.Clamp((l-65)*255/150,0,255);
        else if(method=="fixed150")value=l>=150?0:255;
        else
        {
            int x0=Math.Max(0,x-15),x1=Math.Min(w,x+16),y0=Math.Max(0,y-15),y1=Math.Min(h,y+16);
            double mean=(double)(integral[y1*(w+1)+x1]-integral[y0*(w+1)+x1]-integral[y1*(w+1)+x0]+integral[y0*(w+1)+x0])/((x1-x0)*(y1-y0));
            value=l>mean+20?0:255;
        }
        result.SetPixel(x,y,Color.FromArgb(value,value,value));
    }
    return result;
}
record Sample(string Name,string Tier,string Path,int X,int Y,int Width,int Height,string[] Expected);
record Reading(string Text,float Confidence);
record Result(string Method,string Sample,string Tier,int Card,string Expected,string? Actual,double? MatchScore,List<Reading> Readings,double Milliseconds);
