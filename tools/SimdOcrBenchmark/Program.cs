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
var samples = JsonSerializer.Deserialize<Sample[]>(File.ReadAllText(Path.Combine(root,"tools/OcrBenchmark/samples.json")))!;
using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"LOL-GameAssistant/Resources/augment-names.json")));
var entries = catalog.RootElement.EnumerateObject().Where(p=>int.Parse(p.Name)>=1000).ToArray();
var names = entries.SelectMany(p=>new[] {(int.Parse(p.Name),p.Value.GetProperty("name").GetString()!),
    (int.Parse(p.Name),p.Value.TryGetProperty("englishName",out var en)?en.GetString()??"":"")}).ToArray();
var labels=entries.ToDictionary(p=>int.Parse(p.Name),p=>p.Value.GetProperty("name").GetString()!);
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
        using var source=new Bitmap(sample.Path);
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
        using var source=new Bitmap(sample.Path);
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
