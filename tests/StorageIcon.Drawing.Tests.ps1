$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\UI\StorageItemDrawing.cs') -Raw -Encoding UTF8
$harness = @'
namespace Microsoft.Xna.Framework {
 public struct Vector2 { public float X,Y; public Vector2(float x,float y) {X=x;Y=y;} }
 public struct Color { public int Value; public static Color White {get{return new Color {Value=255};}} }
}
namespace Terraria {
 public class Item { public int type=1,stack=10; public byte prefix=7; public Microsoft.Xna.Framework.Color color; public bool IsAir {get{return type==0||stack<=0;}} }
 public static class Main { public static object spriteBatch; }
}
namespace Terraria.UI {
 public static class ItemSlot {
  public static class Context { public const int ChestItem=3; }
  public static Terraria.Item LastItem; public static object Batch; public static int Calls,LastContext;
  public static Microsoft.Xna.Framework.Vector2 Center; public static float Limit; public static Microsoft.Xna.Framework.Color Tint;
  public static float DrawItemIcon(Terraria.Item item,int context,object batch,Microsoft.Xna.Framework.Vector2 center,float scale,float sizeLimit,Microsoft.Xna.Framework.Color environment,float fade=1,bool flip=false) {
   LastItem=item;Batch=batch;Center=center;Limit=sizeLimit;Tint=item.color;Calls++;LastContext=context;return scale;
  }
 }
}
namespace QualityOfTerraria.UI {
 public static class IconDrawingBehavior {
  static void Check(bool value,string label) {if(!value)throw new System.Exception(label);System.Console.WriteLine("PASS "+label);}
  public static void Run() {
   var gel=new Terraria.Item {color=new Microsoft.Xna.Framework.Color {Value=123}};
   Terraria.Main.spriteBatch=new object();
   StorageItemDrawing.DrawIcon(gel,10,20,32,48);
   Check(object.ReferenceEquals(Terraria.UI.ItemSlot.LastItem,gel)&&Terraria.UI.ItemSlot.Tint.Value==123,"图标绘制传原物品并保留凝胶染色");
   Check(object.ReferenceEquals(Terraria.UI.ItemSlot.Batch,Terraria.Main.spriteBatch)&&Terraria.UI.ItemSlot.LastContext==3,"原版图标使用当前UI批次与箱子上下文");
   Check(Terraria.UI.ItemSlot.Center.X==26&&Terraria.UI.ItemSlot.Center.Y==44&&Terraria.UI.ItemSlot.Limit==32,"图标居中且限制在槽位短边");
   Check(gel.prefix==7&&gel.stack==10,"绘制不改前缀和物品数量");
   var dynamite=new Terraria.Item {color=new Microsoft.Xna.Framework.Color {Value=456}};
   StorageItemDrawing.DrawIcon(dynamite,0,0,40,40);
   Check(Terraria.UI.ItemSlot.Tint.Value==456,"雷管自身染色保留给原版流程");
   int count=Terraria.UI.ItemSlot.Calls;StorageItemDrawing.DrawIcon(null,0,0,40,40);StorageItemDrawing.DrawIcon(new Terraria.Item {type=0},0,0,40,40);
   Check(Terraria.UI.ItemSlot.Calls==count,"空物品不绘制");
   Terraria.Main.spriteBatch=null;StorageItemDrawing.DrawIcon(gel,0,0,40,40);
   Check(Terraria.UI.ItemSlot.Calls==count,"批次未就绪时不触发绘制");
  }
 }
}
'@
Add-Type -TypeDefinition ($source + "`n" + $harness)
[QualityOfTerraria.UI.IconDrawingBehavior]::Run()
