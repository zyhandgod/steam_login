using System;
using System.IO;
using System.Text;
using SteamLoginLite.Services;
class Test {
 static void Main() {
  var dir=Path.Combine(Path.GetTempPath(),"steam-preferences-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
  try {
   foreach(var root in new[]{"UserLocalConfigStore","UserRoamingConfigStore"}) {
    var file=Path.Combine(dir,root+".vdf");var original="\""+root+"\"\r\n{\r\n\t\"unrelated\" \"keep\"\r\n}\r\n";
    File.WriteAllText(file,original,new UTF8Encoding(true));
    if(!SteamVdfConfig.ApplyLoginPreferences(file,root,true,true,true))throw new Exception("Apply failed");
    var text=File.ReadAllText(file);foreach(var expected in new[]{"\"NotifyAvailableGames\"\t\t\"0\"","\"SignIntoFriends\"\t\t\"0\"","\"SteamDefaultDialog\"\t\t\"#app_games\"","\"unrelated\" \"keep\""})if(!text.Contains(expected))throw new Exception("Missing "+expected);
    if(File.ReadAllText(file+".steam-login-lite.bak")!=original)throw new Exception("Backup mismatch");
    SteamVdfConfig.ApplyLoginPreferences(file,root,false,false,false);text=File.ReadAllText(file);
    foreach(var expected in new[]{"\"NotifyAvailableGames\"\t\t\"1\"","\"SignIntoFriends\"\t\t\"1\"","\"SteamDefaultDialog\"\t\t\"#store\""})if(!text.Contains(expected))throw new Exception("Off setting not restored: "+expected);
    if(File.ReadAllText(file+".steam-login-lite.bak")!=original)throw new Exception("Original backup overwritten");
    var before=File.ReadAllBytes(file);SteamVdfConfig.ApplyLoginPreferences(file,root,false,false,false);if(Convert.ToBase64String(before)!=Convert.ToBase64String(File.ReadAllBytes(file)))throw new Exception("Not idempotent");
    File.WriteAllText(file,"\""+root+"\" { \"Software\" \"invalid\" }");
    try{SteamVdfConfig.ApplyLoginPreferences(file,root,true,true,true);throw new Exception("Invalid accepted");}catch(InvalidDataException){}
   }
   Console.WriteLine("PASS: local + roaming preferences, library, switch OFF restore, original backup, idempotence, invalid VDF rejection.");
  } finally {Directory.Delete(dir,true);}
 }
}
