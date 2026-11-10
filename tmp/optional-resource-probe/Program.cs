using LOL_GameAssistant.Infrastructure.Skins;
string root = Path.GetFullPath(args[1]);
using var service = new OptionalCslolSkinService(Path.Combine(root, "app"), Path.Combine(root, "cache"), Path.Combine(root, "work"), Path.Combine(root, "source.json"), () => false, () => false);
await service.ImportResourcesAsync(Path.GetFullPath(args[0]));
Console.WriteLine($"Available={service.IsAvailable}; Catalog={(await service.LoadCatalogAsync()).Count}; Patch={service.CatalogPatch}");
