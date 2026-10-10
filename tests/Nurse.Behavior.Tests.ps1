$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$tools=Get-Content (Join-Path $root 'WandsAndTools.cs') -Raw
function Method($source,$signature){$start=$source.IndexOf($signature);if($start -lt 0){throw "Missing $signature"};$open=$source.IndexOf('{',$start);$end=$open+1;$depth=1;while($depth){if($source[$end] -eq '{'){$depth++};if($source[$end] -eq '}'){$depth--};$end++};$source.Substring($start,$end-$start)}
$quick=Method $tools 'public static void ExecuteQuickNurse()'
$coins=Method $tools 'private static string FormatCoins('
$dialogue=(Method (Get-Content (Join-Path $root 'Patches/NursePatches.cs') -Raw) 'internal static bool Prefix(').Replace('internal static','public static')
$nurse=''
if(Test-Path (Join-Path $root 'Features/NurseTreatment.cs')){$nurse=Get-Content (Join-Path $root 'Features/NurseTreatment.cs') -Raw}
$stubs=@'
using System; using System.Collections.Generic; using Terraria; using Terraria.Audio; using Terraria.ID; using QualityOfTerraria.Features; using QualityOfTerraria.Networking; using HarmonyLib;
namespace Terraria {
 public class Player { public bool active=true,dead; public int whoAmI,statLife=100,statLifeMax2=100,statMana=10,statManaMax2=20; public int[] buffType=new int[4],buffTime=new int[4]; public long Coins=5000; public int BuyCalls; public bool BuyItem(long cost,int slot=-1){BuyCalls++;if(Coins<cost)return false;Coins-=cost;return true;} public void DelBuff(int i){buffType[i]=0;buffTime[i]=0;} public void HealEffect(int n,bool broadcast=true){} }
 public static class Main {public static Player LocalPlayer=new Player();public static Player[] player={LocalPlayer};public static int netMode,myPlayer;public static bool gameMenu;public static bool[] debuff=new bool[100],buffNoTimeDisplay=new bool[100];public static string Text,npcChatText;public static int CostOverride=-1;public static void NewText(string s,int r=255,int g=255,int b=255){Text=s;} public static int GetNurseHealCost(){if(CostOverride>=0)return CostOverride;int v=Math.Max(0,LocalPlayer.statLifeMax2-LocalPlayer.statLife);for(int i=0;i<4;i++)if(LocalPlayer.buffType[i]>0 && debuff[LocalPlayer.buffType[i]] && LocalPlayer.buffTime[i]>60 && !ID.BuffID.Sets.NurseCannotRemoveDebuff[LocalPlayer.buffType[i]])v+=100;return v;}}
 public static class NetMessage { public static void SendData(int t,int a=-1,int b=-1,object text=null,int number=0){} }
}
namespace Terraria.ID {public static class MessageID {public const int PlayerLifeMana=16,PlayerBuffs=50;}public static class SoundID {public const int Item4=4;} public static class BuffID {public const int Count=100;public static class Sets {public static bool[] NurseCannotRemoveDebuff=new bool[100];}}}
namespace Terraria.Audio {public static class SoundEngine {public static void PlaySound(int i){}}}
namespace HarmonyLib {public static class AccessTools {public static System.Reflection.MethodInfo Method(Type t,string s,Type[] p=null){return t.GetMethod(s,System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);}}}
namespace QualityOfTerraria.Features {}
namespace QualityOfTerraria.Networking {public static class RemoteStorageOperations {public static bool Busy;} public static class ToolNetwork {public static bool Busy;}}
namespace QualityOfTerraria { public static class Mod { public static bool WorldActive=true; } public static class WandsAndTools {__QUICK__ __COINS__} }
namespace QualityOfTerraria.Patches {public static class NurseDialog { __DIALOGUE__ }}
public static class NurseTests {static int checks;static void Check(bool ok,string name){checks++;if(!ok)throw new Exception("FAIL: "+name);}
 public static void Run(){var p=Terraria.Main.LocalPlayer;Terraria.Main.debuff[1]=true;
 p.buffType[0]=1;p.buffTime[0]=100;Terraria.ID.BuffID.Sets.NurseCannotRemoveDebuff[1]=true;QualityOfTerraria.WandsAndTools.ExecuteQuickNurse();Check(p.BuyCalls==0 && p.Coins==5000,"不可清除负面状态不能空扣钱");
 Terraria.ID.BuffID.Sets.NurseCannotRemoveDebuff[1]=false;p.buffTime[0]=0;QualityOfTerraria.WandsAndTools.ExecuteQuickNurse();Check(p.BuyCalls==0,"已过期负面状态不扣钱");
 p.buffTime[0]=61;QualityOfTerraria.WandsAndTools.ExecuteQuickNurse();Check(p.Coins==4900 && p.buffType[0]==0,"满血可清除状态按原版价格治疗");
 p.buffType[0]=1;p.buffTime[0]=60;QualityOfTerraria.WandsAndTools.ExecuteQuickNurse();Check(p.Coins==4900 && p.buffType[0]==0,"原版60刻内状态免费清除");
 QualityOfTerraria.WandsAndTools.ExecuteQuickNurse();Check(p.BuyCalls==1 && Terraria.Main.Text.Contains("生命值已满且无可清除的负面状态，无需治疗"),"完全无需治疗提示且不BuyItem");
 p.statLife=90;QualityOfTerraria.WandsAndTools.ExecuteQuickNurse();Check(p.statLife==100 && p.Coins==4890,"缺少生命按原版价格");
 p.statLife=80;p.Coins=0;QualityOfTerraria.WandsAndTools.ExecuteQuickNurse();Check(p.statLife==80,"金币不足不治疗");
 p.statLife=100;p.Coins=5000;Main.buffNoTimeDisplay[1]=true;p.buffType[0]=1;p.buffTime[0]=100;QualityOfTerraria.WandsAndTools.ExecuteQuickNurse();Check(p.Coins==4900 && p.buffType[0]==0,"不显示计时不等于护士不可清除");
 int cost=999;Check(!QualityOfTerraria.Patches.NurseDialog.Prefix(ref cost) && p.Coins==4900 && Main.npcChatText.Contains("无需治疗"),"护士对话无实际需求拦截付款");
 p.buffType[0]=1;p.buffTime[0]=61;Check(QualityOfTerraria.Patches.NurseDialog.Prefix(ref cost) && cost==100,"对话有需求保留原版治疗并重算价格");
 p.buffTime[0]=60;Check(!QualityOfTerraria.Patches.NurseDialog.Prefix(ref cost) && p.buffType[0]==0 && p.Coins==4900,"对话短时间状态免费清除不BuyItem");
 p.statLife=99;Main.CostOverride=0;Check(!QualityOfTerraria.Patches.NurseDialog.Prefix(ref cost) && p.statLife==100 && p.Coins==4900,"折扣后免费生命治疗不BuyItem");Main.CostOverride=-1;
 p.statLife=90;RemoteStorageOperations.Busy=true;int calls=p.BuyCalls;QualityOfTerraria.WandsAndTools.ExecuteQuickNurse();Check(p.BuyCalls==calls && p.statLife==90,"库存事务期间快捷护士不修改金币");Check(!QualityOfTerraria.Patches.NurseDialog.Prefix(ref cost) && p.statLife==90,"库存事务期间护士对话不付款");
 Console.WriteLine("护士行为检查: "+checks+"/"+checks);}}
'@
$nurse=[regex]::Replace($nurse,'(?m)^using .*;\r?\n','')
$stubs=$stubs.Replace('__QUICK__',$quick).Replace('__COINS__',$coins).Replace('__DIALOGUE__',$dialogue)
Add-Type -TypeDefinition ($stubs + "`n" + $nurse) -Language CSharp
[NurseTests]::Run()
