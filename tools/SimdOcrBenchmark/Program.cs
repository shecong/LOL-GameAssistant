using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using LOL_GameAssistant.Domain.GameData;
using LOL_GameAssistant.Infrastructure.GameData;
using Sdcb.SimdPaddleOCR;
using Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny;
using Sdcb.SimdPaddleOCR.Models.ChineseV6Small;

string root = Path.GetFullPath(args.ElementAtOrDefault(0) ?? ".");
string output = Path.Combine(root, "tools/SimdOcrBenchmark/results");
Directory.CreateDirectory(output);
using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"LOL-GameAssistant/Resources/augment-names.json")));
var entries = catalog.RootElement.EnumerateObject().Where(p=>int.Parse(p.Name)>=1000).ToArray();
var names = entries.SelectMany(p=>new[] {(int.Parse(p.Name),p.Value.GetProperty("name").GetString()!),
    (int.Parse(p.Name),p.Value.TryGetProperty("englishName",out var en)?en.GetString()??"":"")}).ToArray();
var labels=entries.ToDictionary(p=>int.Parse(p.Name),p=>p.Value.GetProperty("name").GetString()!);
if (args.Contains("--self-test"))
{
    using var engine = AugmentOcrEngine.Create();
    var gameSize = new Size(2544, 1353);
    using var region = new Bitmap(gameSize.Width * 2 / 3, gameSize.Height * 32 / 100);
    using (var graphics = Graphics.FromImage(region)) graphics.Clear(Color.Black);
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    bool cancelled = false;
    try { AugmentImageRecognizer.Recognize(region, gameSize, engine, names, cancellation.Token); }
    catch (OperationCanceledException) { cancelled = true; }
    bool empty = AugmentImageRecognizer.Recognize(region, gameSize, engine, names).AugmentIds.Count == 0;
    string[] expected = ["火上浇油", "急急小子", "关键暴击"];
    using (var graphics = Graphics.FromImage(region))
    using (var font = new Font("Microsoft YaHei UI", 26, GraphicsUnit.Pixel))
    {
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        for (int index = 0; index < 3; index++)
        {
            float center = gameSize.Width * new[] {31,50,69}[index] / 100f - gameSize.Width / 6f;
            var width = graphics.MeasureString(expected[index], font).Width;
            graphics.DrawString(expected[index], font, Brushes.White, center - width / 2, gameSize.Height * 12 / 100 + 2);
        }
    }
    region.Save(Path.Combine(output, "synthetic-titles.png"));
    var scan = AugmentImageRecognizer.Recognize(region, gameSize, engine, names);
    var actual = scan.AugmentIds.Select(id => labels[id]).ToArray();
    bool passed = cancelled && empty && actual.SequenceEqual(expected);
    File.WriteAllText(Path.Combine(output, "self-test.json"), JsonSerializer.Serialize(new
    {
        EngineVersion = typeof(PaddleOcrAll).Assembly.GetName().Version?.ToString(),
        ModelVersion = typeof(ChineseV6SmallModels).Assembly.GetName().Version?.ToString(),
        Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
        Expected = expected, Actual = actual, Cancelled = cancelled, Empty = empty, Passed = passed,
        Scope = "Synthetic images; does not validate historical game screenshots"
    }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"OCR self-test: {passed}; cancellation={cancelled}, empty={empty}, titles={string.Join(',',actual)}");
    if (!passed) Environment.ExitCode = 1;
    return;
}
int samplesIndex = Array.IndexOf(args, "--samples");
string samplesFile = samplesIndex >= 0 && samplesIndex + 1 < args.Length
    ? Path.GetFullPath(args[samplesIndex + 1]) : Path.Combine(root, "tools/OcrBenchmark/samples.json");
var samples = JsonSerializer.Deserialize<Sample[]>(File.ReadAllText(samplesFile))!;
foreach (var sample in samples)
{
    string samplePath = Path.IsPathRooted(sample.Path) ? sample.Path : Path.Combine(Path.GetDirectoryName(samplesFile)!, sample.Path);
    if (!File.Exists(samplePath)) throw new FileNotFoundException("OCR sample missing. Supply a portable sample manifest using --samples, or run --self-test.", samplePath);
}
var rows = new List<object>();
foreach (string model in args.Contains("--production-only") ? Array.Empty<string>() : new[] {"tiny","small"})
{
    var loading=Stopwatch.StartNew();
    using var ocr = await PaddleOcrAll.LoadAsync(model=="tiny"?ChineseV6TinyModels.Default:ChineseV6SmallModels.Default,
        new PaddleOcrOptions {UseDirectionClassification=false,LineWorkerCount=1,DetIntraOpThreads=2,
            Detector=new PaddleOcrDetectorOptions {MaxPooledSessions=1},
            Recognizer=new PaddleOcrRecognizerOptions {MaxPooledSessions=1}});
    Console.WriteLine($"Loaded {model}: {loading.ElapsedMilliseconds}ms");
    foreach(var sample in samples)
    {
        using var source=new Bitmap(Path.IsPathRooted(sample.Path) ? sample.Path : Path.Combine(Path.GetDirectoryName(samplesFile)!, sample.Path));
        using var client=source.Clone(new Rectangle(sample.X,sample.Y,sample.Width,sample.Height),PixelFormat.Format24bppRgb);
        for(int card=0;card<3;card++)
        {
            int width=client.Width*15/100,center=new[]{31,50,69}[card];
            using var crop=client.Clone(new Rectangle(client.Width*center/100-width/2,client.Height*40/100,width,client.Height*3/100),PixelFormat.Format24bppRgb);
            var timer=Stopwatch.StartNew(); var data=crop.LockBits(new Rectangle(Point.Empty,crop.Size),ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
            PaddleOcrResult result;
            try
            {
                byte[] bytes=new byte[data.Stride*crop.Height];Marshal.Copy(data.Scan0,bytes,0,bytes.Length);
                result=ocr.Run(bytes,crop.Width,crop.Height,data.Stride,ImagePixelFormat.Bgr24);
            }
            finally {crop.UnlockBits(data);}
            var match=AugmentNameMatcher.MatchCard(result.Text,names);
            string? actual=match==null?null:labels[match.Id];
            rows.Add(new {Model=model,Sample=sample.Name,Card=card+1,Expected=sample.Expected[card],Actual=actual,Text=result.Text,Milliseconds=timer.Elapsed.TotalMilliseconds});
            Console.WriteLine($"{model} {sample.Name}/{card+1}: {actual==sample.Expected[card]}, {timer.ElapsedMilliseconds}ms, {result.Text.Replace('\n',' ')}");
        }
    }
}
if(rows.Count > 0)
    File.WriteAllText(Path.Combine(output,"comparison.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions {WriteIndented=true}));
using (var production = AugmentOcrEngine.Create())
{
    var productionRows = new List<object>();
    bool passed=true;
    foreach(var sample in samples)
    {
        using var source=new Bitmap(Path.IsPathRooted(sample.Path) ? sample.Path : Path.Combine(Path.GetDirectoryName(samplesFile)!, sample.Path));
        using var client=source.Clone(new Rectangle(sample.X,sample.Y,sample.Width,sample.Height),PixelFormat.Format24bppRgb);
        using var region=client.Clone(new Rectangle(client.Width/6,client.Height*28/100,client.Width*2/3,client.Height*32/100),PixelFormat.Format24bppRgb);
        var timer=Stopwatch.StartNew();
        var scan=AugmentImageRecognizer.Recognize(region,client.Size,production,names);
        var actual=scan.AugmentIds.Select(id=>labels[id]).ToArray();
        bool correct=actual.SequenceEqual(sample.Expected); passed &= correct;
        productionRows.Add(new {Sample=sample.Name,Expected=sample.Expected,Actual=actual,Passed=correct,Milliseconds=timer.Elapsed.TotalMilliseconds,Text=scan.RecognizedText});
        Console.WriteLine($"Production {sample.Name}: {correct}, {timer.ElapsedMilliseconds}ms, {string.Join(",",actual)}");
    }
    // Cancellation must be observed before invoking the model pipeline.
    using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
    using var blank=new Bitmap(1696,432);
    try { AugmentImageRecognizer.Recognize(blank,new Size(2544,1353),production,names,cancellation.Token); passed=false; }
    catch(OperationCanceledException) { Console.WriteLine("Cancellation: passed"); }
    var noCards=AugmentImageRecognizer.Recognize(blank,new Size(2544,1353),production,names);
    bool emptyPassed=noCards.AugmentIds.Count==0; passed &= emptyPassed;
    Console.WriteLine($"Empty image: {emptyPassed}");
    File.WriteAllText(Path.Combine(output,"production.json"),JsonSerializer.Serialize(productionRows,new JsonSerializerOptions {WriteIndented=true}));
    using var process=Process.GetCurrentProcess();
    File.WriteAllText(Path.Combine(output,"runtime.json"),JsonSerializer.Serialize(new {
        SingleFile=string.IsNullOrEmpty(typeof(PaddleOcrAll).Assembly.Location),
        ProcessPeakWorkingSetMB=Math.Round(process.PeakWorkingSet64/1024d/1024d,1),
        Passed=passed
    },new JsonSerializerOptions {WriteIndented=true}));
    if(!passed) Environment.ExitCode=1;
}
record Sample(string Name,string Tier,string Path,int X,int Y,int Width,int Height,string[] Expected);
