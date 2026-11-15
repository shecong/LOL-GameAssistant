Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class OverlayNames {
 [StructLayout(LayoutKind.Sequential)] struct U { public ushort Length, MaximumLength; public IntPtr Buffer; }
 [StructLayout(LayoutKind.Sequential)] struct A { public int Length; public IntPtr RootDirectory, ObjectName; public uint Attributes; public IntPtr SecurityDescriptor, SecurityQualityOfService; }
 [DllImport("ntdll.dll")] static extern int NtOpenDirectoryObject(out IntPtr handle, uint access, ref A attributes);
 [DllImport("ntdll.dll")] static extern int NtQueryDirectoryObject(IntPtr handle, IntPtr buffer, uint length, bool single, bool restart, ref uint context, out uint returned);
 [DllImport("ntdll.dll")] static extern int NtClose(IntPtr handle);
 public static string[] Read(string path) {
  IntPtr text=Marshal.StringToHGlobalUni(path), us=Marshal.AllocHGlobal(Marshal.SizeOf<U>()), buffer=Marshal.AllocHGlobal(65536);
  IntPtr handle=IntPtr.Zero;
  try {
   Marshal.StructureToPtr(new U {Length=(ushort)(path.Length*2),MaximumLength=(ushort)(path.Length*2+2),Buffer=text},us,false);
   A a=new A {Length=Marshal.SizeOf<A>(), ObjectName=us,Attributes=64};
   int status=NtOpenDirectoryObject(out handle,1,ref a); if(status<0) throw new Exception("open "+status);
   uint context=0, returned; bool restart=true; var names=new System.Collections.Generic.List<string>();
   while(NtQueryDirectoryObject(handle,buffer,65536,true,restart,ref context,out returned)>=0) {
    restart=false; var name=Marshal.PtrToStructure<U>(buffer); string s=Marshal.PtrToStringUni(name.Buffer,name.Length/2);
    if(s.StartsWith("SC.MayhemOverlay.v2.")) names.Add(s);
   }
   return names.ToArray();
  } finally {if(handle!=IntPtr.Zero) NtClose(handle); Marshal.FreeHGlobal(text);Marshal.FreeHGlobal(us);Marshal.FreeHGlobal(buffer);}
 }
}
'@
$session = (Get-Process -Name sc助手 | Select-Object -First 1).SessionId
$names = [OverlayNames]::Read("\Sessions\$session\BaseNamedObjects") | Where-Object { $_ -notlike '*.Lock' }
foreach ($name in $names) {
 $map = [System.IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting("Local\$name", [System.IO.MemoryMappedFiles.MemoryMappedFileRights]::Read)
 $view = $map.CreateViewAccessor(0,0,[System.IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
 try {
  $width=$view.ReadInt32(56); $height=$view.ReadInt32(60)
  Write-Output "Capture ${width}x${height}; game $($view.ReadInt32(64))x$($view.ReadInt32(68))"
  if($width -le 0 -or $height -le 0) {continue}
  $pixels=New-Object byte[] ($width*$height*4)
  [void]$view.ReadArray(65536,$pixels,0,$pixels.Length)
  $bitmap=New-Object System.Drawing.Bitmap($width,$height,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $data=$bitmap.LockBits([System.Drawing.Rectangle]::new(0,0,$width,$height),[System.Drawing.Imaging.ImageLockMode]::WriteOnly,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  try { [System.Runtime.InteropServices.Marshal]::Copy($pixels,0,$data.Scan0,$pixels.Length) } finally { $bitmap.UnlockBits($data) }
  $bitmap.Save("E:\code\weather-app\tmp\augment-hint-repro\native.png")
  $bitmap.Dispose()
 } finally {$view.Dispose();$map.Dispose()}
}
