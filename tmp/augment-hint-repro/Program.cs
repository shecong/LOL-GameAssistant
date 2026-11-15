using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using LOL_GameAssistant.Infrastructure.GameData;
using LOL_GameAssistant.Application.GameData;

var assembly = typeof(NativeGameOverlayBridge).Assembly;
var flags = BindingFlags.Static | BindingFlags.NonPublic;
using var original = new Bitmap(Path.Combine(AppContext.BaseDirectory, "../../../native.png"));
var data = original.LockBits(new Rectangle(Point.Empty, original.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
var pixels = new byte[original.Width * original.Height * 4];
try { for (int row = 0; row < original.Height; row++) Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * original.Width * 4, original.Width * 4); }
finally { original.UnlockBits(data); }
using var fixedImage = (Bitmap)typeof(NativeGameOverlayBridge).GetMethod("CreateCaptureBitmap", flags)!.Invoke(null, [pixels, original.Width, original.Height])!;
using var engine = (IDisposable)assembly.GetType("LOL_GameAssistant.Infrastructure.GameData.AugmentOcrEngine")!.GetMethod("Create", flags)!.Invoke(null, null)!;
using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../LOL-GameAssistant/Resources/augment-names.json")));
var names = catalog.RootElement.EnumerateObject().Where(p => int.Parse(p.Name) >= 1000).SelectMany(p => new[] {(int.Parse(p.Name), p.Value.GetProperty("name").GetString()!), (int.Parse(p.Name), p.Value.TryGetProperty("englishName", out var en) ? en.GetString()! : "")}).ToArray();
var recognize = assembly.GetType("LOL_GameAssistant.Infrastructure.GameData.AugmentImageRecognizer")!.GetMethod("Recognize", flags)!;
var before = (AugmentScanResult)recognize.Invoke(null, [original, new Size(2560,1440), engine, names, CancellationToken.None])!;
var after = (AugmentScanResult)recognize.Invoke(null, [fixedImage, new Size(2560,1440), engine, names, CancellationToken.None])!;
Console.WriteLine($"Original alpha={original.GetPixel(400,180).A}: {before.AugmentIds.Count} matches; fixed alpha={fixedImage.GetPixel(400,180).A}: {string.Join(',', after.AugmentIds)}");
if (before.AugmentIds.Count != 0 || !after.AugmentIds.SequenceEqual(new[]{2143,3060,2140})) Environment.ExitCode=1;

