using System;using System.Threading;using System.Reflection;using System.Linq;using System.Security.Cryptography;using System.Text;using SilentOrders.Core;using Rubezh.UnityPort;using Newtonsoft.Json;
class CatalogCheck {
 static void Main(string[] args){Exception error=null;var t=new Thread(()=>{try{
 var assets=StudioCatalog.Assets;var ids=StandaloneWallMaterials.AllSourceSprites();
 if(assets.Count<937||ids.Count==0)throw new Exception("Incomplete catalog");
 string json=JsonConvert.SerializeObject(assets.OrderBy(x=>x.Key).ToArray());
 Console.WriteLine("assets="+assets.Count+" wallSprites="+ids.Count+" sha256="+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
 foreach(var m in typeof(StudioCatalog).GetMethods(BindingFlags.NonPublic|BindingFlags.Static).Where(m=>m.Name=="Create"||m.Name.StartsWith("AddCatalogPart"))){var b=m.GetMethodBody();Console.WriteLine(m.Name+" IL="+b.GetILAsByteArray().Length+" locals="+b.LocalVariables.Count);}
 }catch(Exception e){error=e;}},args.Length>0?int.Parse(args[0]):256*1024);t.Start();t.Join();if(error!=null)throw error;}
}
