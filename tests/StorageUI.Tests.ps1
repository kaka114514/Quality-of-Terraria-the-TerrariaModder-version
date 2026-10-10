$ErrorActionPreference = 'Stop'
$source = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\UI\ExtremeStorageUI.cs') -Raw -Encoding UTF8
function Part([string]$signature) {
    $start = $source.IndexOf($signature)
    if ($start -lt 0) { throw "找不到生产代码：$signature" }
    $open = $source.IndexOf('{', $start); $end = $open + 1; $depth = 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start)
}
$methods = @('private static void AdjustLayout()', 'private static void ApplySorting()', 'private static bool MatchesTab(', 'private static bool IsFurniture(', 'private static string GetSortModeDisplayName(', 'private static void DrawItemGrid(', 'private static void DrawBottomToolbar(', 'public static void Draw()', 'private static void DrawChestManagerPage(', 'private static void DrawSettingsPage(', 'private static void SortChests()') | ForEach-Object { Part $_ }
foreach ($optional in @('private static int CompareItems(', 'private static int GetItemKind(', 'private static bool DrawButton(')) {
    if ($source.Contains($optional)) { $methods += Part $optional }
}
$stateStart = $source.IndexOf('private static int _contentY')
$state = $source.Substring($stateStart, $source.IndexOf('private static void AdjustLayout()', $stateStart) - $stateStart)
$footerCall = [regex]::Match($source, 'DrawBottomToolbar\(PanelX \+ 16,[^;]+;').Value
$constants = @()
foreach ($idType in @('ItemID', 'TileID')) {
    $names = [regex]::Matches(($methods -join "`n"), ($idType + '\.(\w+)')) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
    $number = 100
    $fields = foreach ($name in $names) { "public const int $name = $number;"; $number++ }
    $constants += "public static class $idType { $($fields -join ' ') }"
}
$harness = @'
using System;
using System.Collections.Generic;
public class Item {
 public int type, stack=1, maxStack=99, rare, damage, pick, axe, hammer, fishingPole, ammo, createTile=-1, createWall, buffType, healLife, healMana, bait;
 public int headSlot=-1,bodySlot=-1,legSlot=-1; public byte prefix; public bool accessory,vanity,consumable; public string Name="";
 public bool IsAir { get { return type == 0 || stack <= 0; } }
 public void TurnToAir() { type=0;stack=0;prefix=0; }
 public string AffixName() { return prefix==0 ? Name : "前缀"+prefix+" "+Name; }
}
public class Player { public bool mouseInterface; }
public class Chest { public int x,y; public bool Marked=true; public string name=""; public Item[] item=new Item[40]; }
public static class Main { public static Player[] player={new Player()}; public static Player LocalPlayer=player[0]; public static int myPlayer,netMode,maxChests=2; public static Item mouseItem; public static Chest[] chest={new Chest(),new Chest()}; public static bool mouseLeft,mouseLeftRelease; }
public static class SoundEngine { public static void PlaySound(int id) {} }
public static class RemoteStorageOperations { public static string Status = "服务器已确认"; public static bool RequestOpenChest(int index) { return true; } }
public static class Patches { public static class StorageInventoryGuard { public static bool Locked; } }
public static class SoundID { public const int Grab=1, MenuTick=2; }
public static class MessageID { public const int SyncChestItem=1; }
public static class NetMessage { public static void SendData(params object[] values) {} }
public class Config { public bool InfinitePotions,PortableCraftingStations,InfiniteStationsAndBanners; public int PotionStackRequirement=30; }
public static class Mod { public static Config Config=new Config(); }
public static class UIManager { public static bool CanRender=true; }
public static class RemoteStorageSnapshots { public static string Status="已确认"; }
public class TextInput { public void Unfocus() {} public void Update() {} public void Clear() {} public string Draw(int x,int y,int w,int h) { UIRenderer.DrawRect(x,y,w,h,0,0,0,255); return ""; } }
public static class StorageItemDrawing { public static void DrawIcon(Item item,int x,int y,int w,int h) {} }
__CONSTANTS__
public static class PanelLayout {
 public static int ScreenWidth=780,ScreenHeight=580;
 public static void ClampToScreen(ref int x,ref int y,int w,int h) { x=Math.Max(0,Math.Min(ScreenWidth-w,x)); y=Math.Max(0,Math.Min(ScreenHeight-h,y)); }
 public static void DrawFittedText(string s,int x,int y,int w,byte r,byte g,byte b) { UIRenderer.Texts.Add(new Rect(x,y,w,16)); }
}
public struct Rect { public int X,Y,W,H; public Rect(int x,int y,int w,int h) { X=x;Y=y;W=w;H=h; } }
public static class UIRenderer {
 public static int MouseX=-100, MouseY=-100; public static bool MouseLeftClick,MouseRightClick;
 public static List<Rect> Rects=new List<Rect>();
 public static List<Rect> Texts=new List<Rect>();
 public static void DrawRect(int x,int y,int w,int h,byte r,byte g,byte b,byte a) { Rects.Add(new Rect(x,y,w,h)); }
 public static void DrawRectOutline(int x,int y,int w,int h,byte r,byte g,byte b,byte a,int t) {}
 public static void DrawTextSmall(string s,int x,int y,byte r,byte g,byte b,byte a) {}
 public static void DrawItem(int type,int x,int y,int w,int h,byte a) {}
 public static int MeasureText(string s) { return s.Length*14; }
 public static void DrawPanel(int x,int y,int w,int h,byte r,byte g,byte b,byte a) { DrawRect(x,y,w,h,r,g,b,a); }
 public static void DrawText(string s,int x,int y,byte r,byte g,byte b,byte a) {}
 public static void RegisterPanelBounds(string id,int x,int y,int w,int h) {}
 public static void ConsumeClick() { MouseLeftClick=false; } public static void ConsumeRightClick() {}
}
__ENUMS__
public static class StorageUIRegression {
 public static int PanelX,PanelY,PanelWidth=780,PanelHeight=580,CurrentPage,ChestManagerPage;
 public static StorageTab CurrentTab=StorageTab.All; public static ItemSortMode CurrentSortMode;
 public const string PanelId="test"; public static bool IsOpen=true; private static bool _isDragging,_canSave=true; private static int _dragOffsetX,_dragOffsetY; private static TextInput _search=new TextInput(); public static string SearchText="";
 private static readonly HashSet<long> _markedChests = new HashSet<long>{1};
 private static readonly List<ItemSlotRef> _filteredSlots = new List<ItemSlotRef>();
 public struct ItemSlotRef { public int ChestIndex,SlotIndex; public Item Item; }
 __STATE__
 __METHODS__
 private static bool PutItemIntoAnyChest(Item item) { return false; }
 private static void TakeItemToPlayer(ItemSlotRef slot,bool takeAll) {}
 private static void DrawItemTooltip(Item item,ItemSlotRef slot,int x,int y) {}
 private static void RefreshItems() {} private static void HandleToolbarAction(int i) {}
 private static void Toggle() {} private static void MarkAllChests() {} private static void ClearAllMarkedChests() {} private static void MarkNearbyChests(float range) {} private static void ToggleMarkChest(Chest chest) {}
 private static Chest GetChestMetadata(int i) { return Main.chest[i]; } private static Chest GetChestForRead(int i) { return Main.chest[i]; } private static bool IsChestMarked(Chest chest) { return chest.Marked; }
 private static HashSet<int> blockedChests=new HashSet<int>();
 private static bool CanMutateChest(int i) { return !blockedChests.Contains(i); }
 private static int checks,failures;
 private static void Check(bool value,string label) { checks++; Console.WriteLine((value?"PASS ":"FAIL ")+label); if(!value) failures++; }
 private static void Add(Item item) { _filteredSlots.Add(new ItemSlotRef { Item=item }); }
 public static void Run() {
  Item[] ordered={
   new Item {type=1,Name="武器",damage=20},
   new Item {type=2,Name="防具",headSlot=1},
   new Item {type=3,Name="饰品",accessory=true},
   new Item {type=4,Name="工具",damage=12,pick=50},
   new Item {type=5,Name="弹药",damage=8,ammo=1},
   new Item {type=6,Name="建筑",createTile=999,consumable=true},
   new Item {type=7,Name="药水",buffType=1},
   new Item {type=8,Name="家具",createTile=TileID.Chairs,consumable=true},
   new Item {type=9,Name="其他"}
  };
  for(int i=ordered.Length-1;i>=0;i--) Add(ordered[i]);
  CurrentSortMode=(ItemSortMode)5; ApplySorting();
  for(int i=0;i<ordered.Length;i++) Check(_filteredSlots[i].Item==ordered[i], "种类顺序 "+ordered[i].Name);
  Check(GetSortModeDisplayName(CurrentSortMode)=="种类", "排序按钮显示种类");
  _filteredSlots.Clear();
  Add(new Item {type=12,Name="Z",damage=2}); Add(new Item {type=10,Name="A",damage=2}); Add(new Item {type=11,Name="A",damage=2});
  ApplySorting(); Check(_filteredSlots[0].Item.type==10 && _filteredSlots[1].Item.type==11 && _filteredSlots[2].Item.type==12, "同类按名称及ID确定顺序");
  CurrentSortMode=ItemSortMode.Default; Item first=_filteredSlots[0].Item; ApplySorting(); Check(_filteredSlots[0].Item==first, "默认保持箱子顺序");
  _filteredSlots.Clear();
  foreach(int[] size in new[]{new[]{780,580},new[]{640,480},new[]{360,240},new[]{480,360}}) {
   PanelLayout.ScreenWidth=size[0]; PanelLayout.ScreenHeight=size[1]; CurrentTab=StorageTab.All;
   AdjustLayout(); UIRenderer.Rects.Clear(); DrawItemGrid(PanelX+16,PanelY+_contentY);
   int right=0,bottom=0; foreach(Rect rect in UIRenderer.Rects) {right=Math.Max(right,rect.X+rect.W);bottom=Math.Max(bottom,rect.Y+rect.H);}
   Check(right<=PanelX+PanelWidth-16 && PanelX+PanelWidth-16-right<=16, "网格横向利用空间 "+size[0]+"x"+size[1]);
   UIRenderer.Rects.Clear(); __FOOTER_CALL__
   int top=int.MaxValue; bool inside=true; foreach(Rect rect in UIRenderer.Rects) {top=Math.Min(top,rect.Y);inside&=rect.X>=PanelX&&rect.Y>=PanelY&&rect.X+rect.W<=PanelX+PanelWidth&&rect.Y+rect.H<=PanelY+PanelHeight;}
   Check(bottom+4<=top, "网格不与操作栏重叠 "+size[0]+"x"+size[1]);
   Check(inside, "操作和翻页按钮限屏 "+size[0]+"x"+size[1]);
  }
  CurrentTab=StorageTab.ChestManager; UIRenderer.Rects.Clear(); DrawBottomToolbar(0,0);
  Check(UIRenderer.Rects.Count==0, "箱子管理不显示物品存取工具栏");
  CurrentTab=StorageTab.Settings; UIRenderer.Rects.Clear(); DrawBottomToolbar(0,0);
  Check(UIRenderer.Rects.Count==0, "说明页不显示物品存取工具栏");
  foreach(int[] size in new[]{new[]{780,580},new[]{640,480},new[]{360,240},new[]{480,360}}) {
   PanelLayout.ScreenWidth=size[0]; PanelLayout.ScreenHeight=size[1];
   foreach(StorageTab tab in new[]{StorageTab.All,StorageTab.ChestManager,StorageTab.Settings}) {
    CurrentTab=tab; AdjustLayout(); UIRenderer.Rects.Clear(); Draw(); bool inside=true;
    foreach(Rect rect in UIRenderer.Rects) inside&=rect.X>=PanelX&&rect.Y>=PanelY&&rect.X+rect.W<=PanelX+PanelWidth&&rect.Y+rect.H<=PanelY+PanelHeight;
    Check(inside, "完整页面限屏 "+tab+" "+size[0]+"x"+size[1]);
   }
  }
  PanelLayout.ScreenWidth=780; PanelLayout.ScreenHeight=580; CurrentTab=StorageTab.All; AdjustLayout();
  UIRenderer.MouseX=PanelX+PanelWidth-170; UIRenderer.MouseY=PanelY+10; UIRenderer.MouseLeftClick=true; Draw();
  Check(CurrentTab==StorageTab.ChestManager && !_isDragging, "标题箱子管理按钮可点击且不触发拖动");
  UIRenderer.MouseX=PanelX+PanelWidth-80; UIRenderer.MouseLeftClick=true; Draw();
  Check(CurrentTab==StorageTab.Settings && !_isDragging, "标题说明按钮可点击且不触发拖动");
  UIRenderer.MouseX=PanelX+40; UIRenderer.MouseY=PanelY+50; UIRenderer.MouseLeftClick=true; Draw();
  Check(CurrentTab==StorageTab.All, "分类按钮可从管理页返回物品页");
  CurrentSortMode=ItemSortMode.Default; UIRenderer.MouseX=PanelX+PanelWidth-80; UIRenderer.MouseY=PanelY+_searchY+5;
  var modes=new[]{(ItemSortMode)5,ItemSortMode.Rarity,ItemSortMode.Stack,ItemSortMode.Name,ItemSortMode.ID,ItemSortMode.Default};
  foreach(ItemSortMode expected in modes) { UIRenderer.MouseLeftClick=true; Draw(); Check(CurrentSortMode==expected, "排序入口可选择 "+expected); }
  Main.chest[0].item=new Item[11];
  for(int i=0;i<ordered.Length;i++) Main.chest[0].item[i]=ordered[ordered.Length-1-i];
  Main.chest[0].item[9]=new Item {type=0,stack=0};
  CurrentSortMode=(ItemSortMode)5; SortChests();
  for(int i=0;i<ordered.Length;i++) Check(Main.chest[0].item[i]==ordered[i], "实体箱子按种类整理 "+ordered[i].Name);
  Check(Main.chest[0].item[9]==null || Main.chest[0].item[9].IsAir, "实体箱空格排在末尾");
  var a=new Item {type=20,Name="物品A",stack=3,rare=4}; var b=new Item {type=21,Name="物品B",stack=12,rare=1};
  Main.chest[0].item=new[]{a,b}; CurrentSortMode=ItemSortMode.Stack; SortChests();
  Check(Main.chest[0].item[0]==b, "实体箱子使用所选数量顺序");
  CurrentSortMode=ItemSortMode.Default; SortChests();
  Check(Main.chest[0].item[0]==a, "默认实体整理保留原稀有度规则");
  _filteredSlots.Clear(); Add(new Item {type=31,Name="宠物",buffType=1}); Add(new Item {type=30,Name="召唤杖",damage=20,buffType=1});
  CurrentSortMode=(ItemSortMode)5; ApplySorting(); Check(_filteredSlots[0].Item.type==30, "有增益的召唤武器仍归武器");
  Check(!MatchesTab(new Item {type=5,damage=8,ammo=1},StorageTab.Weapons), "弹药分类与武器分类一致");
  PanelLayout.ScreenWidth=360; PanelLayout.ScreenHeight=240; CurrentTab=StorageTab.All; AdjustLayout();
  _markedChests.Clear(); UIRenderer.Texts.Clear(); DrawItemGrid(PanelX+16,PanelY+_contentY);
  bool emptyFits=true; foreach(Rect text in UIRenderer.Texts) emptyFits&=text.Y>=PanelY+_contentY&&text.Y+text.H<=PanelY+_footerY-4;
  Check(emptyFits && UIRenderer.Texts.Count>0, "小屏未标记提示不与操作栏重叠");
  CurrentSortMode=(ItemSortMode)5; blockedChests.Clear();
  var sourceStack=new Item {type=70,Name="材料",prefix=2,stack=8}; var targetStack=new Item {type=70,Name="材料",prefix=2,stack=7};
  Main.chest=new[]{new Chest {item=new[]{targetStack}},new Chest {item=new[]{sourceStack}}};
  SortChests(); Check(targetStack.stack==15 && sourceStack.IsAir && targetStack.prefix==2, "跨箱同ID同前缀合并并保留目标前缀");
  var different=new Item {type=70,Name="材料",prefix=3,stack=9}; targetStack.stack=7;
  Main.chest[1].item=new[]{different}; SortChests();
  Check(targetStack.stack==7 && different.stack==9 && different.prefix==3, "跨箱不同前缀严格隔离");
  targetStack.stack=90; sourceStack=new Item {type=70,Name="材料",prefix=2,stack=30}; Main.chest[1].item=new[]{sourceStack};
  SortChests(); Check(targetStack.stack==99 && sourceStack.stack==21 && targetStack.prefix==2 && sourceStack.prefix==2, "跨箱部分合并遵守上限及数量守恒");
  targetStack.stack=7; sourceStack.stack=8; blockedChests.Add(1); SortChests();
  Check(targetStack.stack==7 && sourceStack.stack==8, "不可写箱子不参与合并");
  blockedChests.Clear(); Main.chest[1].Marked=false; SortChests();
  Check(targetStack.stack==7 && sourceStack.stack==8, "未标记箱子不参与合并");
  Main.chest[1].Marked=true; targetStack.maxStack=1;sourceStack.maxStack=1;targetStack.stack=1;sourceStack.stack=1;SortChests();
  Check(targetStack.stack==1 && sourceStack.stack==1, "不可堆叠装备保持两件且前缀不变");
  targetStack.maxStack=99;sourceStack.maxStack=99;targetStack.stack=120;sourceStack.stack=30;SortChests();
  Check(targetStack.stack==120 && sourceStack.stack==30, "历史超上限堆叠不反向合并或改变数量");
  Main.maxChests=8; Main.chest=new Chest[8]; blockedChests.Clear(); blockedChests.Add(2);
  var random=new Random(731); var totalsBefore=new Dictionary<long,long>();var originals=new Dictionary<Item,byte>();
  for(int c=0;c<8;c++) {
   Main.chest[c]=new Chest {Marked=c!=3};
   for(int s=0;s<40;s++) {
    var item=new Item {type=random.Next(70,75),prefix=(byte)random.Next(0,4),stack=random.Next(1,160),Name="材料"};
    Main.chest[c].item[s]=item; originals[item]=item.prefix;
    long key=((long)item.type<<8)|item.prefix; if(!totalsBefore.ContainsKey(key))totalsBefore[key]=0;totalsBefore[key]+=item.stack;
   }
  }
  Item[] blockedItems=(Item[])Main.chest[2].item.Clone(); Item[] unmarkedItems=(Item[])Main.chest[3].item.Clone();
  int blockedTotal=0,unmarkedTotal=0;foreach(Item item in blockedItems)blockedTotal+=item.stack;foreach(Item item in unmarkedItems)unmarkedTotal+=item.stack;
  SortChests();var totalsAfter=new Dictionary<long,long>();bool prefixesIntact=true;
  foreach(Chest chest in Main.chest) foreach(Item item in chest.item) {
   if(item==null||item.IsAir)continue;long key=((long)item.type<<8)|item.prefix;if(!totalsAfter.ContainsKey(key))totalsAfter[key]=0;totalsAfter[key]+=item.stack;
   prefixesIntact&=originals[item]==item.prefix;
  }
  bool conserved=totalsBefore.Count==totalsAfter.Count;foreach(var pair in totalsBefore)conserved&=totalsAfter.ContainsKey(pair.Key)&&totalsAfter[pair.Key]==pair.Value;
  Check(conserved&&prefixesIntact,"320组混合物品跨8箱整理按ID和前缀逐组守恒");
  int afterBlocked=0,afterUnmarked=0;bool untouched=true;for(int s=0;s<40;s++) {untouched&=object.ReferenceEquals(blockedItems[s],Main.chest[2].item[s])&&object.ReferenceEquals(unmarkedItems[s],Main.chest[3].item[s]);afterBlocked+=Main.chest[2].item[s].stack;afterUnmarked+=Main.chest[3].item[s].stack;}
  Check(untouched&&blockedTotal==afterBlocked&&unmarkedTotal==afterUnmarked,"混合整理不移动或扣除受限箱内容");
  Console.WriteLine("终极存储UI检查 "+(checks-failures)+"/"+checks);
  if(failures>0) throw new Exception(failures+" 项失败");
 }
}
'@
$harness = $harness.Replace('__CONSTANTS__', ($constants -join "`n")).Replace('__ENUMS__', ((Part 'public enum StorageTab') + (Part 'public enum ItemSortMode'))).Replace('__STATE__', $state).Replace('__METHODS__', ($methods -join "`n")).Replace('__FOOTER_CALL__', $footerCall)
Add-Type -TypeDefinition $harness
[StorageUIRegression]::Run()
