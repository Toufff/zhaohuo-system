# 趙活系統：《活俠傳》遊戲內修改器

[简体中文](README.md) | 繁體中文 | [English](README.en.md) | [한국어](README.ko.md)

《活俠傳》（Legend of Mortal）的遊戲內修改器，以 BepInEx 外掛的形式執行。進入遊戲後按 F8 開啟面板，可以：
- 查看這次行動會觸發哪些事件，並指定要觸發哪一個；
- 直接修改屬性和好感；
- 預先看到對話選項會帶來什麼結果；
- 指定骰子擲出的結果；
- 在戰鬥中開啟無敵、一擊必殺。

![行動前在面板裡查看這次可能觸發的事件，並指定其中一個](media/event-choose.webp)

## 功能

- **事件**：每次行動前，面板會列出這次可能觸發的事件和觸發條件。條件會附上目前的數值（例如「行動次數 3>=1」），已滿足的條件顯示為綠色。
  - 選好事件後按「指定本次行動」，這次行動就會觸發它。
  - 條件還差一點時，可以按條件旁的「設為 N」，直接把數值改到滿足。
- **地圖**：地點旁會標出有幾個事件，有可以觸發的事件時，標記顯示為綠色。滑鼠移上去可以看到事件清單。

  <img src="media/map-hover.webp" width="420" alt="地圖上的事件數標記和懸停清單">

- **數值**：屬性、人物好感、門派關係、劇情標記都可以直接修改。

  <img src="media/values-tab.webp" width="560" alt="數值頁">

- **對話選項**：選項後面會用括號寫出選了會怎樣，例如屬性增減、「若…」條件分支、劇情標記、隨機事件、擲骰。
  - 整個模組裡顏色只有一種含義：綠色表示條件滿足、數值增加或獲得物品，紅色表示條件不滿足、數值減少或失去物品。
  - 滑鼠移到選項上，可以看到完整的條件和具體標記。

  <img src="media/options.webp" width="560" alt="選項後的結果概述和懸停詳情">

- **劇情分支**：劇情依標記或條件分支時，對話框左側的固定位置會跳出小框，列出每個分支的條件和目前的數值，並標出這次走的是哪一支。出現或更新時會彈一下、標題閃爍提醒。小框可以拖動，也可以關掉，下次分支時會回到原位再跳出。

  <img src="media/branch.webp" width="620" alt="劇情分支小框">

- **骰子**：每個結果旁都有「指定 N%」按鈕，按哪個就擲出哪個，N 是這一檔原本的機率。加成太高或太低、正常擲不出來的結果（顯示 0%）也能選，骰面會依總點數倒推，算式照樣對得上。不按的話，就是遊戲原本的隨機。

  <img src="media/dice.webp" width="520" alt="按「指定 50%」後轉盤停在該檔">

- **商店**：道具說明下方會多一行實際效果。

  <img src="media/shop.webp" width="560" alt="道具說明下的效果行">

- **戰鬥**：無敵、一擊必殺、氣力不減、技能無冷卻，即時戰役和回合制決鬥都能用。這些開關只在戰鬥中生效，不會寫入存檔。

  <img src="media/battle-tab.webp" width="560" alt="戰鬥頁開關">

  <img src="media/duel.webp" width="720" alt="回合制決鬥：開啟無敵和一擊必殺">

- **截圖**：可以一鍵隱藏模組的全部介面，也可以為它設定快捷鍵。

## 安裝

適用 Steam 版 build 25957922。

1. 從 [Releases](https://github.com/Toufff/zhaohuo-system/releases) 下載最新的 `ZhaoHuoSystem-v*.zip`，解壓縮到任意資料夾。
2. 關閉遊戲，按兩下 `install.bat`。
   - 它會自動找到 Steam 裡的《活俠傳》；找不到時會跳出視窗，讓你選擇遊戲資料夾（有 `Mortal.exe` 的那個）。
   - 遊戲還沒有 BepInEx 時，會一併安裝壓縮檔裡附帶的官方 BepInEx 6.0.0-be.788（Unity.Mono，Windows x64）。
3. 啟動遊戲，讀取存檔後按 F8 開啟面板。

修改數值和指定事件都會寫入存檔，第一次使用前請先備份存檔。

- **更新**：下載新版本，再按兩下一次 `install.bat`。
- **解除安裝**：按兩下 `uninstall.bat`。它會刪除外掛和設定，然後詢問是否一併刪除 BepInEx；其他 BepInEx 模組也依賴它，直接按 Enter 就會保留。存檔不受影響。
- 如果 Windows 跳出安全性提示，確認執行即可。

### 手動安裝

1. 在 Steam 收藏庫中對《活俠傳》按右鍵 → 管理 → 瀏覽本機檔案，開啟遊戲資料夾（有 `Mortal.exe` 的資料夾）。
2. 下載 [BepInEx-Unity.Mono-win-x64-6.0.0-be.788](https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.Mono-win-x64-6.0.0-be.788%2B5b766a3.zip)，把壓縮檔裡的全部內容解壓縮到遊戲資料夾。
3. 把 `ZhaoHuoSystem-v*.zip` 也解壓縮到遊戲資料夾，外掛會位於 `BepInEx/plugins/ZhaoHuoSystem/`。
4. 進入遊戲並讀取存檔，按 F8 開啟面板。

- **Steam Deck / Linux**：只能手動安裝，並在遊戲內容的啟動選項中填入 `WINEDLLOVERRIDES="winhttp=n,b" %command%`。
- **手動解除安裝**：刪除 `BepInEx/plugins/ZhaoHuoSystem` 資料夾。如果要連 BepInEx 一起移除，再刪除遊戲資料夾裡的 `BepInEx`、`winhttp.dll`、`doorstop_config.ini`、`.doorstop_version` 和 `changelog.txt`。

## 使用

- **開啟面板**：按 F8，或點擊畫面左側的「趙活系統」小按鈕。快捷鍵可以在「設定」頁修改，小按鈕可以拖曳。
- **語言**：介面語言跟隨遊戲，支援繁體中文、簡體中文和韓文。
- **設定檔**：設定儲存在 `BepInEx/config/local.zhaohuo.system.cfg`。

## 遊戲更新之後

外掛只在適配過的遊戲版本上運作。Steam 更新遊戲後，外掛會自動停用，不會改動遊戲；等這裡發布新版本後再更新即可。

想確認是不是這個原因，可以開啟 `BepInEx/LogOutput.log`：如果裡面出現 `compatible=False`，表示目前的遊戲版本還沒有適配。

## 回報問題

請在 [Issues](https://github.com/Toufff/zhaohuo-system/issues) 中說明遊戲語言、在哪個場景出的問題、大約什麼時候發生的。然後在面板「設定」頁按「匯出診斷」，把匯出的資料夾壓縮後附上（路徑會自動複製到剪貼簿）。

## 從原始碼建置

需要準備：
- Windows；
- [.NET SDK 9](https://dotnet.microsoft.com/download)；
- 一份已經安裝好 BepInEx 的遊戲。

外掛在編譯時會直接參照遊戲資料夾裡的組件，不會複製這些檔案。

```powershell
# 遊戲不在預設路徑時，用 -p:GameDir 指定遊戲資料夾
dotnet build src/ZhaoHuoSystem/ZhaoHuoSystem.csproj -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\LegendOfMortal"

# 不需要遊戲的測試，CI 也會執行這兩項
dotnet run --project tests/ui-state -c Release
dotnet run --project tests/localization -c Release -- .

# 需要本機遊戲的測試
dotnet run --project tests/native -c Release -p:GameDir="D:\...\LegendOfMortal" -- "D:\...\LegendOfMortal"
```

建置結果是 `src/ZhaoHuoSystem/bin/Release/netstandard2.0/ZhaoHuoSystem.dll`，把它放進 `BepInEx/plugins/ZhaoHuoSystem/` 即可。

## 授權

[MIT](LICENSE) © Toufff。可以自由使用、修改、再散布，也可以商用，只需保留著作權聲明。

這是玩家自製的模組，與《活俠傳》的開發商和發行商無關，不包含任何遊戲檔案。
