using System.IO.Compression;
using System.Text.Json;
var results = new List<object>();
foreach (var name in new[]{"valid.zip","corrupt.zip"})
foreach (var mode in new[]{"full","first-byte","open-only"}) {
  int bytes=0; string outcome="ok";
  try {
    using var archive = ZipFile.OpenRead(name);
    using var entry = archive.GetEntry("data.txt")!.Open();
    if(mode=="full") { var buffer = new byte[3]; int n; while((n=entry.Read(buffer))>0) bytes+=n; }
    else if(mode=="first-byte") { if(entry.ReadByte()>=0) bytes++; }
  } catch(Exception e) { outcome=e.GetType().Name; }
  results.Add(new {name,mode,bytes,outcome});
}
Console.WriteLine(JsonSerializer.Serialize(new {runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,results}));
