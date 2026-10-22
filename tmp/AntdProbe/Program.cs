using System;
class Probe { static void Main() { Console.WriteLine(typeof(AntdUI.Table).GetProperty("RowHeight")); foreach(var p in typeof(AntdUI.CellText).GetProperties()) Console.WriteLine(p); } }
