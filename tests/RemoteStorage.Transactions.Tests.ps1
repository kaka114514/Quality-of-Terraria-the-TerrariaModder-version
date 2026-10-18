$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$sourcePath = Join-Path $root 'Networking/StorageTransfers.cs'
if (!(Test-Path $sourcePath)) { throw 'FAIL: 缺少服务器物品转移实现，远程箱子操作尚未实现' }
$production = Get-Content $sourcePath -Raw
$stub = @'
using System;
using System.Collections.Generic;
using Terraria;
namespace Terraria {
 public class Item {
  public int type,stack,maxStack=99; public byte prefix; public bool favorited;
  public bool IsAir { get { return type==0 || stack<=0; } }
  public Item Clone() { return (Item)MemberwiseClone(); }
  public void TurnToAir() { type=0;stack=0;prefix=0; }
 }
}
'@
$tests = @'
namespace QualityOfTerraria.Networking {
 public static class TransfersTests {
  static Terraria.Item I(int n,int s,int p=0,int cap=99) { return new Terraria.Item {type=n,stack=s,prefix=(byte)p,maxStack=cap}; }
  static Terraria.Item[] Box(params Terraria.Item[] a) { return a; }
  static void Check(bool b,string reason) { if(!b)throw new Exception(reason); }
  public static void Run() {
   var source=I(1,70,3); var a=Box(I(1,80,3),I(1,30,4),I(0,0));
   int moved=StorageTransfers.Move(source,new[]{a},false,50);
   Check(moved==50 && source.stack==20 && a[0].stack==99 && a[1].stack==30 && a[2].stack==31 && a[2].prefix==3,"部分转移保持物品数量与前缀");
   source=I(1,100);a=Box(I(1,98),I(2,99));
   moved=StorageTransfers.Move(source,new[]{a},false);
   Check(moved==1 && source.stack==99 && a[0].stack==99,"满箱必须保留余量");
   source=I(3,25,4);a=Box(I(0,0));
   Check(StorageTransfers.Move(source,new[]{a},true)==0 && source.stack==25,"快速堆叠不能放进空格");
   source=I(4,1,8,1); a=Box(I(0,0));
   Check(StorageTransfers.Move(source,new[]{a})==1 && source.IsAir && a[0].prefix==8,"不可堆叠装备保持前缀");
   source=I(1,30);a=Box(source,I(0,0));
   Check(StorageTransfers.Move(source,new[]{a},true)==0 && source.stack==30,"禁止自己与自己合并");
   a=Box(I(1,80,3),I(2,10));var b=Box(I(1,40,3),I(1,10,4));
   StorageTransfers.Merge(new[]{a,b});
   Check(a[0].stack==99 && b[0].stack==21 && b[1].prefix==4 && b[1].stack==10,"跨箱合并仅合并相同前缀且物品守恒");
   Console.WriteLine("PASS: 6 个物品守恒、前缀及容量行为场景");
  }
 }
}
'@
Add-Type -TypeDefinition ($stub + $production.Replace('using Terraria;', '').Replace('using System;', '').Replace('using System.Collections.Generic;', '') + $tests) -Language CSharp
[QualityOfTerraria.Networking.TransfersTests]::Run()
