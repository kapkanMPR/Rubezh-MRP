using System;using System.IO;using Rubezh.UnityPort.Editor;
class Check {
 static void Main(){var old=Directory.GetCurrentDirectory();var temp=Path.Combine(Path.GetTempPath(),"exterior-"+Guid.NewGuid());Directory.CreateDirectory(temp);Directory.SetCurrentDirectory(temp);try{
 Directory.CreateDirectory(NativeExteriorPreparation.Source);
 for(int i=0;i<96;i++)using(var w=new BinaryWriter(File.Create(NativeExteriorPreparation.Source+"/s"+i+".bytes"))){w.Write(1);w.Write(1);for(int j=0;j<4;j++)w.Write(1f);w.Write(0xffffffffu);}
 NativeExteriorPreparation.Prepare();NativeExteriorPreparation.Require();Console.WriteLine("PASS missing atlas rebuilt from 96 sources");
 string cat=NativeExteriorPreparation.Root+"/catalog.json";var first=File.ReadAllText(cat);NativeExteriorPreparation.Prepare();if(first!=File.ReadAllText(cat))throw new Exception("changed valid catalog");Console.WriteLine("PASS unchanged atlas preserved");
 using(var w=new BinaryWriter(File.OpenWrite(NativeExteriorPreparation.Source+"/s0.bytes"))){w.BaseStream.Position=24;w.Write(0xff000000u);}
 NativeExteriorPreparation.Prepare();if(first==File.ReadAllText(cat))throw new Exception("stale hash");NativeExteriorPreparation.Require();Console.WriteLine("PASS changed pixels regenerate catalog hashes");
 File.Delete(NativeExteriorPreparation.Root+"/page-00.png");NativeExteriorPreparation.Prepare();NativeExteriorPreparation.Require();Console.WriteLine("PASS missing page regenerated");
 File.Delete(NativeExteriorPreparation.Source+"/s0.bytes");bool failed=false;try{NativeExteriorPreparation.Prepare();}catch(Exception e){failed=e.Message.Contains("95 / 96");}if(!failed)throw new Exception("incomplete sources accepted");Console.WriteLine("PASS incomplete sources rejected");
 Console.WriteLine("Contracts only: PNG encoder is a stub; no Unity image/import verification.");
 }finally{Directory.SetCurrentDirectory(old);Directory.Delete(temp,true);}}
}
