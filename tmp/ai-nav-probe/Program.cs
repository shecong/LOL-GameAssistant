using System;
using System.Linq;
foreach(var m in typeof(AntdUI.YScrollBar).GetMethods().Where(m=>m.Name=="GetValue")) Console.WriteLine(m+" : "+string.Join(",",m.GetParameters().Select(p=>p.Name)));
