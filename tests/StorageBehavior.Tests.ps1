$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$extreme = Get-Content (Join-Path $root 'UI/ExtremeStorageUI.cs') -Raw
$vault = Get-Content (Join-Path $root 'UI/SuperVaultUI.cs') -Raw
function Method([string]$source, [string]$signature) {
    $start = $source.IndexOf($signature)
    if ($start -lt 0) { throw "缺少生产方法：$signature" }
    $open = $source.IndexOf('{', $start)
    $depth = 1; $end = $open + 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start)
}
$policy = Method $extreme 'public static bool CanMutateChest('
$layout = Method $extreme 'private static void AdjustLayout()'
$layoutStateStart = $extreme.IndexOf('private static int _contentY')
$layoutState = $extreme.Substring($layoutStateStart, $extreme.IndexOf('private static void AdjustLayout()', $layoutStateStart) - $layoutStateStart)
$storageTab = Method $extreme 'public enum StorageTab'
$vaultLayout = Method $vault 'private static void AdjustLayout()'
$tooltip = Method $extreme 'private static void DrawItemTooltip('
$vaultTipStart = $vault.IndexOf('            // Tooltip')
$vaultTooltip = $vault.Substring($vaultTipStart, $vault.IndexOf('            // 4. 底部工具栏', $vaultTipStart) - $vaultTipStart)
$search = [regex]::Match($extreme, 'internal static bool MatchesSearch[^\r\n]+').Value
$gridStart = $vault.IndexOf('int startX = PanelX + 16;')
$gridEnd = $vault.IndexOf('Item hoveredItem', $gridStart)
$grid = $vault.Substring($gridStart, $gridEnd - $gridStart)
$stubs = @'
namespace Terraria {
 public class Item { public int type, stack, rare; public string Name=""; public byte prefix; public bool IsAir { get { return type == 0 || stack <= 0; } } public void SetDefaults(int id) { type = id; } public bool Prefix(byte p) { prefix = p; return true; } public string AffixName() { return prefix==0 ? Name : "前缀"+prefix+" "+Name; } }
 public class Player { public bool active = true; public int chest = -1; }
 public static class Main { public static int netMode, maxPlayers = 4, maxChests = 10, maxTilesX = 100, maxTilesY = 100, myPlayer; public static ulong GameUpdateCount; public static Player LocalPlayer = new Player(); public static Player[] player = { LocalPlayer, new Player() }; public static Chest[] chest = new Chest[10]; }
 public class Chest {
   public int index, x, y; public string name = ""; public Item[] item; public static bool Locked; public static int Owner = -1;
   public static Chest CreateOutOfArray(int i, int x, int y, int count) { var c = new Chest { index = i, x = x, y = y, item = new Item[count] }; for(int s=0;s<count;s++) c.item[s] = new Item(); return c; }
   public static bool IsLocked(int x, int y) { return Locked; }
   public static int UsingChest(int i) { return Owner; }
   public static int FindChest(int x, int y) { for(int i=0;i<Main.chest.Length;i++) if(Main.chest[i]!=null && Main.chest[i].x==x && Main.chest[i].y==y) return i; return -1; }
 }
 public static class Recipe { public static void UpdateRecipeList() {} }
}
namespace Terraria.ID { public static class ItemID { public const int Count = 1000; } }
namespace TerrariaModder.Core.UI {
 public static class UIRenderer {
  public static int X,Y,W,H;
  public static System.Collections.Generic.List<string> Texts = new System.Collections.Generic.List<string>();
  public static int MeasureText(string text) { return text.Length*14; }
  public static void DrawPanel(int x,int y,int w,int h,int r,int g,int b,int a) { X=x;Y=y;W=w;H=h; }
  public static void DrawRectOutline(int x,int y,int w,int h,int r,int g,int b,int a,int thickness) {}
 }
}
namespace TerrariaModder.Core.Net {
 public static class NetSync {
  public static Action<int,string,string> OnStorageRequest;
  public static Action<bool,string,string> OnStorageResponse;
  public static string Request, Response, Operation;
  public static void SendStorageRequest(string op, string payload) { Operation = op; Request = payload; }
  public static void SendStorageResponseTo(int player, bool ok, string op, string payload) { Response = payload; }
 }
}
namespace QualityOfTerraria {
 public static class Mod { public static void LogError(string s, Exception e) { throw new Exception(s, e); } }
}
namespace QualityOfTerraria.UI {
 public static class UIManager { public static bool CanRender = true; }
 public static class PanelLayout {
  public static int ScreenWidth = 720, ScreenHeight = 480;
  public static void ClampToScreen(ref int x, ref int y,int w,int h) { x=Math.Max(0,Math.Min(ScreenWidth-w,x)); y=Math.Max(0,Math.Min(ScreenHeight-h,y)); }
  public static void DrawFittedText(string s,int x,int y,int width,byte r,byte g,byte b) { TerrariaModder.Core.UI.UIRenderer.Texts.Add(s); }
 }
 public static class ExtremeStorageUI {
  public static int PanelX=140, PanelY=80, PanelWidth=780, PanelHeight=580, CurrentPage;
  public static StorageTab CurrentTab=StorageTab.All;
  private static readonly List<ItemSlotRef> _filteredSlots=new List<ItemSlotRef>();
  __LAYOUTSTATE__
  public static List<long> Keys = new List<long>();
  public static IEnumerable<long> GetMarkedCoordinates() { return Keys; }
  public static void RefreshItems() {}
  public struct ItemSlotRef { public int ChestIndex; }
  public struct Color { public byte R,G,B; }
  public static Color GetRarityColor(int rare) { return new Color(); }
  public static Terraria.Chest GetChestForRead(int i) { return Terraria.Main.chest[i]; }
  __POLICY__
  __LAYOUT__
  __SEARCH__
  __TOOLTIP__
  public static void Layout() { AdjustLayout(); }
  public static void Tooltip(Terraria.Item item,int x,int y) { DrawItemTooltip(item,new ItemSlotRef{ChestIndex=3},x,y); }
 }
 public static class SuperVaultUI {
  public static int PanelX=260, PanelY=160, PanelWidth=560, PanelHeight=620;
  __VAULTLAYOUT__
  public static void Tooltip(Terraria.Item hoveredItem) { int mouseX=10,mouseY=10; __VAULTTOOLTIP__ }
  public static int GridBottom() { AdjustLayout(); __GRID__ return startX >= 0 && cols == 10 ? startY + (rows-1)*(slotSize+spacing)+slotSize : -1; }
 }
 public static class StorageBehavior {
  static int checks;
  static void Check(bool v,string s) { checks++; if(!v) throw new Exception(s); }
  public static int Run() {
   Check(ExtremeStorageUI.MatchesSearch("玻璃瓶", " 玻璃 "), "中文过滤与首尾空格");
   Check(ExtremeStorageUI.MatchesSearch("Iron", "IRON"), "大小写过滤");
   Check(!ExtremeStorageUI.MatchesSearch("铜锭", "银"), "排除不同名称");
   var chest = Terraria.Chest.CreateOutOfArray(3,2,4,40); chest.item[0].SetDefaults(5); chest.item[0].stack=9;
   Terraria.Main.chest[3]=chest;
   Check(ExtremeStorageUI.CanMutateChest(3), "单人可访问");
   Terraria.Chest.Owner=1; Check(!ExtremeStorageUI.CanMutateChest(3), "占用拒绝");
   Terraria.Chest.Owner=-1; Terraria.Chest.Locked=true; Check(!ExtremeStorageUI.CanMutateChest(3), "锁箱拒绝");
   Terraria.Chest.Locked=false; Terraria.Main.netMode=1; Check(!ExtremeStorageUI.CanMutateChest(3), "未确认开箱拒绝写入");
   Terraria.Main.LocalPlayer.chest=3; Check(ExtremeStorageUI.CanMutateChest(3), "本地已开箱会话");
   Terraria.Main.LocalPlayer.chest=-1;
   ExtremeStorageUI.Layout(); Check(ExtremeStorageUI.PanelX+ExtremeStorageUI.PanelWidth<=720 && ExtremeStorageUI.PanelY+ExtremeStorageUI.PanelHeight<=480, "小屏幕存储边界");
   Check(SuperVaultUI.GridBottom()<=SuperVaultUI.PanelY+SuperVaultUI.PanelHeight-58, "小屏幕大背包网格与按钮无重叠");
   PanelLayout.ScreenWidth=1280; PanelLayout.ScreenHeight=720; ExtremeStorageUI.PanelX=2000; ExtremeStorageUI.Layout();
   Check(ExtremeStorageUI.PanelX+ExtremeStorageUI.PanelWidth<=1280 && ExtremeStorageUI.PanelHeight==580, "分辨率变化重新校正");
   PanelLayout.ScreenWidth=360; PanelLayout.ScreenHeight=240; chest.name=new string('名',100); chest.item[0].Name=new string('长',100);
   ExtremeStorageUI.Tooltip(chest.item[0],355,235);
   Check(TerrariaModder.Core.UI.UIRenderer.X>=0 && TerrariaModder.Core.UI.UIRenderer.X+TerrariaModder.Core.UI.UIRenderer.W<=360, "长名称Tooltip实际宽度限屏");
   Check(TerrariaModder.Core.UI.UIRenderer.Y>=0 && TerrariaModder.Core.UI.UIRenderer.Y+TerrariaModder.Core.UI.UIRenderer.H<=240, "Tooltip实际高度限屏");
   chest.item[0].Name="武器"; chest.item[0].prefix=7;
   TerrariaModder.Core.UI.UIRenderer.Texts.Clear(); ExtremeStorageUI.Tooltip(chest.item[0],10,10);
   Check(TerrariaModder.Core.UI.UIRenderer.Texts[0]==chest.item[0].AffixName(), "终极存储悬停显示完整前缀名称");
   TerrariaModder.Core.UI.UIRenderer.Texts.Clear(); SuperVaultUI.Tooltip(chest.item[0]);
   Check(TerrariaModder.Core.UI.UIRenderer.Texts[0]==chest.item[0].AffixName(), "大背包悬停显示完整前缀名称");
   ExtremeStorageUI.Keys.Add(((long)2<<32)|4);
   Terraria.Main.GameUpdateCount=10; RemoteStorageSnapshots.Initialize(); RemoteStorageSnapshots.Update();
   Check(RemoteStorageSnapshots.GetChest(3)==null, "发送请求不等于确认");
   Terraria.Main.netMode=2; TerrariaModder.Core.Net.NetSync.OnStorageRequest(0,TerrariaModder.Core.Net.NetSync.Operation,TerrariaModder.Core.Net.NetSync.Request);
   Terraria.Main.netMode=1; string firstResponse=TerrariaModder.Core.Net.NetSync.Response;
   TerrariaModder.Core.Net.NetSync.OnStorageResponse(true,TerrariaModder.Core.Net.NetSync.Operation,firstResponse);
   Check(RemoteStorageSnapshots.GetChest(3).item[0].stack==9, "确认快照保留原始索引与数量");
   Terraria.Main.GameUpdateCount=310; RemoteStorageSnapshots.Update();
   Terraria.Chest.Owner=1; Terraria.Main.netMode=2;
   TerrariaModder.Core.Net.NetSync.OnStorageRequest(0,TerrariaModder.Core.Net.NetSync.Operation,TerrariaModder.Core.Net.NetSync.Request);
   Terraria.Main.netMode=1; TerrariaModder.Core.Net.NetSync.OnStorageResponse(true,TerrariaModder.Core.Net.NetSync.Operation,TerrariaModder.Core.Net.NetSync.Response);
   Check(RemoteStorageSnapshots.GetChest(3)==null, "服务器占用后旧快照失效");
   RemoteStorageSnapshots.ResetSession(); RemoteStorageSnapshots.Initialize(); Terraria.Main.GameUpdateCount=620;
   RemoteStorageSnapshots.Update(); TerrariaModder.Core.Net.NetSync.OnStorageResponse(true,TerrariaModder.Core.Net.NetSync.Operation,firstResponse);
   Check(RemoteStorageSnapshots.GetChest(3)==null, "旧会话响应不能污染新世界");
   Terraria.Chest.Owner=-1; Terraria.Main.netMode=2;
   TerrariaModder.Core.Net.NetSync.OnStorageRequest(0,TerrariaModder.Core.Net.NetSync.Operation,TerrariaModder.Core.Net.NetSync.Request);
   Terraria.Main.netMode=1; TerrariaModder.Core.Net.NetSync.OnStorageResponse(true,TerrariaModder.Core.Net.NetSync.Operation,TerrariaModder.Core.Net.NetSync.Response);
   Terraria.Main.GameUpdateCount=1521; Check(RemoteStorageSnapshots.GetChest(3)==null, "未刷新数据过期");
   RemoteStorageSnapshots.ResetSession(); Check(TerrariaModder.Core.Net.NetSync.OnStorageResponse==null, "退出取消网络订阅");
   return checks;
  }
 }
}
'@
$stubs = $stubs.Replace('__POLICY__',$policy).Replace('__LAYOUT__',$layout).Replace('__LAYOUTSTATE__',$layoutState).Replace('__VAULTLAYOUT__',$vaultLayout).Replace('__VAULTTOOLTIP__',$vaultTooltip).Replace('__SEARCH__',$search).Replace('__GRID__',$grid).Replace('__TOOLTIP__',$tooltip)
Add-Type -TypeDefinition ('using TerrariaModder.Core.UI;' + (Get-Content (Join-Path $root 'UI/RemoteStorageSnapshots.cs') -Raw) + $storageTab + $stubs)
$count = [QualityOfTerraria.UI.StorageBehavior]::Run()
"$count 项存储布局、搜索、会话和快照行为检查通过"


