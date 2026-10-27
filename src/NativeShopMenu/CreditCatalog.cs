using System;
public sealed class CreditEntry {
 public readonly string Name,Author,Url;
 public CreditEntry(string name,string author,string url){Name=name;Author=author;Url=url;}
}
public static class CreditCatalog {
 public static readonly CreditEntry[] Entries={
  new CreditEntry("TCG Card Shop Simulator","Original game — OPNeon Games","https://store.steampowered.com/app/3070070/TCG_Card_Shop_Simulator/"),
  new CreditEntry("Pokémon Shop / Real TCG Overhaul","Real TCG Overhaul — iiTzSamurai","https://www.nexusmods.com/tcgcardshopsimulator/mods/577"),
  new CreditEntry("QOL ShopOS","Evers665 / QOL Mods","https://www.nexusmods.com/tcgcardshopsimulator/mods/1754"),
  new CreditEntry("Enhanced Prefab Loader","GhostNarwhal","https://www.nexusmods.com/tcgcardshopsimulator/mods/496"),
  new CreditEntry("EPL API","GhostNarwhal","https://www.nexusmods.com/tcgcardshopsimulator/mods/1144"),
  new CreditEntry("TextureReplacer","Shaklin","https://www.nexusmods.com/tcgcardshopsimulator/mods/69"),
  new CreditEntry("Collection Tracker","Real TCG Overhaul — iiTzSamurai","https://www.nexusmods.com/tcgcardshopsimulator/mods/867"),
  new CreditEntry("RTCGO Holographics","Real TCG Overhaul — iiTzSamurai","https://www.nexusmods.com/tcgcardshopsimulator/mods/1676"),
  new CreditEntry("BepInEx + Configuration Manager","BepInEx project; Nexus pack by iiTzSamurai","https://www.nexusmods.com/tcgcardshopsimulator/mods/1555"),
  new CreditEntry("BepInEx","Plugin framework — BepInEx contributors","https://github.com/BepInEx/BepInEx")
 };
}
