using System;
class Probe { static void Main(){ foreach(var p in typeof(AntdUI.SelectItemDraw).GetProperties())Console.WriteLine(p);foreach(var f in typeof(AntdUI.SelectItemDraw).GetFields())Console.WriteLine(f);foreach(var m in typeof(AntdUI.Canvas).GetMethods()) if(m.Name.Contains("String")||m.Name.Contains("Measure"))Console.WriteLine(m); } }
