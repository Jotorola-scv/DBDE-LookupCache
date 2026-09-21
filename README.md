# DBDE Lookup Cache

A small BepInEx performance fix for [DynamicBoneDistributionEditor](https://github.com/Njaecha/DynamicBoneDistributionEditor) (DBDE) in **Koikatsu (KK)** and **Koikatsu Sunshine (KKS)**, for the character maker and CharaStudio.

It removes the per-frame garbage that DBDE creates. That garbage causes the periodic "the whole game freezes for half a second" stutter, especially after loading outfits.

[中文說明在下方](#中文說明) · [日本語の概要](#日本語の概要)

## Symptoms this fixes

If you have DBDE installed and see any of these, this plugin is for you:

- **The character maker freezes for ~0.5 s every few seconds**, typically **after loading an outfit / coordinate card**
- The maker keeps **stuttering even when you do nothing** (no camera movement, no clicking)
- **CharaStudio framerate is unstable or drops** with characters that use dynamic bones, and it gets worse the more characters are in the scene
- Frequent **GC spikes** / "hitches" in a profiler, with lots of memory being allocated every frame

It does **not** fix DBDE's functional bugs (UI, saving/loading, accessory transfer…). Please report those to [DBDE](https://github.com/Njaecha/DynamicBoneDistributionEditor/issues).

> **Still stuttering while idle in the maker?** The same kind of GC stutter has a second common cause, **BrowserFolders** (Maker/Studio Browser Folders). With a large `UserData/coordinate` folder (thousands of files), its *Automatically refresh when files change* option makes Unity's old Mono re-scan the whole folder tree about every 0.75 s in the background. Setting it to `false` in `BepInEx/config/marco.FolderBrowser.cfg` fixed the remaining idle stutter in our test. The only downside is that you press the refresh button yourself after adding files outside the game.

---

## The problem

Every frame, `DBDECharaController.Update` calls `UpdateActiveStack()` for every edit in the current outfit of every character. That call resolves the edit's dynamic bones through `WouldYouBeSoKindTohandMeTheDynamicBonePlease()`. Three things in this path allocate memory every frame:

1. **Failed lookups are never cached.** If an edit's bones are not on the character, for example after loading a different outfit, DBDE runs `GetComponentsInChildren<DynamicBone>(true)` over the whole character, plus LINQ grouping and name building, **on every frame**.
2. Cached hits are validated with `List.Any(lambda)`, which allocates an enumerator on every call.
3. `UpdateActiveStack` counts the enabled bones with `FindAll(...).Count()`, which creates a new list per edit per frame.

KK and KKS run an old Mono runtime whose garbage collector stops the whole game while it scans the entire managed heap, often 1–2 GB with mods. The more garbage per second, the more often you get a 0.4–0.5 s freeze.

## What this plugin does

It patches DBDE at runtime with Harmony. DBDE itself is not modified, and the results are the same as the original code.

| Where | Original DBDE | With DBDE Lookup Cache |
|---|---|---|
| Failed bone lookup | Full-character search every frame | The miss is remembered for 1 s per character. The cache is dropped immediately when DBDE itself refreshes: clothes, accessory or outfit changes, bone list refresh, reload. |
| Cached hit | `List.Any(lambda)` validation | The same validity check (`bone` and `m_Root` alive) with a plain loop, allocation-free |
| `UpdateActiveStack` | `FindAll(...).Count()` | The same decisions, without creating lists |

The worst case is that a dynamic bone spawned outside DBDE's own refresh events is picked up up to 1 s later.

## Measured effect (KK character maker, one character, outfits with DBDE edits)

| | Before | After |
|---|---|---|
| Garbage from DBDE per 10 s | 24–66 MB | ~2 MB (same level as vanilla UI code) |
| DBDE CPU time per frame | ~1–3 ms | < 0.1 ms |

Test system: Ryzen 9 9950X3D, measured with a profiler attached. The garbage figure depends mostly on how many characters and DBDE edits are loaded, not on your hardware. CPU time depends on your CPU. Overall FPS depends on your whole PC and scene, so no FPS figure is given here.

In CharaStudio every character runs this per frame, so scenes with many characters benefit more.

These numbers come from one setup. **Reports from other setups are very welcome**; see [Reporting results](#reporting-results).

## Compatibility

| DBDE version | Status |
|---|---|
| **1.5.1** | **Supported, tested in game** (KK maker, KK CharaStudio, KKS CharaStudio). This is the version shipped by current HF Patch and BetterRepack. |
| 2.0.0 | **Experimental, untested.** The source code was compared and the logic and member names are identical, but it has not been tested in game. The plugin runs and logs an `EXPERIMENTAL` warning. If you use DBDE 2.0.0, please test and [report](#reporting-results), including "works fine". |
| other | The plugin **does nothing** and logs a warning (see *Ignore DBDE version check*) |

The version check exists because the plugin re-implements two small pieces of DBDE logic. If a future DBDE changes that logic, the plugin must not silently override it.

Requirements: BepInEx 5, KKAPI / KKSAPI, and DynamicBoneDistributionEditor.

## Installation

1. Download the dll for your game from [Releases](../../releases):
   - Koikatsu / Koikatsu Party: `KK_DBDELookupCache.dll`
   - Koikatsu Sunshine: `KKS_DBDELookupCache.dll`
2. Put it in `BepInEx/plugins` in your game folder.
3. Start the game. `output_log.txt` should contain:
   ```
   [Info   :DBDE Lookup Cache] DBDE 1.5.1: patched lookup, UpdateActiveStack and 9 invalidation points
   ```

To uninstall, delete the dll.

## Settings (`BepInEx/config/jotorola.dbde.lookupcache.cfg`)

| Setting | Default | |
|---|---|---|
| `[General] Enabled` | `true` | `false` restores DBDE's original behaviour, useful for A/B comparisons |
| `[General] Miss cache seconds` | `1` | How long a failed lookup is remembered |
| `[Advanced] Ignore DBDE version check` | `false` | Also run on unchecked DBDE versions, at your own risk |

## Reporting results

Please open an [issue](../../issues) with:

- Game (KK / KKS), maker or Studio, DBDE version
- Roughly how many characters, and whether they have DBDE edits
- What you noticed, e.g. fewer freezes, FPS before and after, or problems
- If something looks wrong: the `DBDE Lookup Cache` lines from `output_log.txt`, and whether it goes away with `Enabled = false`

## Building

`build/build.sh` compiles against your own game install with the .NET SDK's Roslyn compiler (no NuGet):

```sh
sh build/build.sh KK  "D:/Games/Koikatsu"
sh build/build.sh KKS "D:/Games/Koikatsu Sunshine"
```

The output goes to `bin/`. `sh build/package.sh <KK folder> <KKS folder>` builds both variants and copies the release dlls to `dist/`.

## Credits

- **Code:** written by **Claude**, Anthropic's AI assistant. That includes the diagnosis (profiling the GC stutter down to DBDE's per-frame lookups) and the plugin itself.
- **Problem report, direction and in-game testing:** [Jotorola-scv](https://github.com/Jotorola-scv), who also maintains this repository.
- DynamicBoneDistributionEditor by [Njaecha](https://github.com/Njaecha/DynamicBoneDistributionEditor). This project is not affiliated with DBDE and does not contain any DBDE code. It references DBDE at runtime only.
- License: MIT

---

## 中文說明

**DBDE Lookup Cache** 是 [DynamicBoneDistributionEditor（DBDE）](https://github.com/Njaecha/DynamicBoneDistributionEditor) 的效能修正插件，支援戀活（KK）和戀活 Sunshine（KKS）的創角畫面與 CharaStudio。

### 能解決的症狀

有安裝 DBDE，並且遇到下列情況的話，就適合使用這個插件：

- **創角畫面每隔幾秒就突然卡住約 0.5 秒**，特別是**讀取服裝（coordinate）之後**
- 創角畫面**靜止不動、什麼都沒操作也會卡頓**
- **Studio 幀率不穩、掉幀**，場景裡使用動態骨骼的角色越多越明顯
- 記憶體垃圾回收（GC）過於頻繁，造成**週期性的瞬間停頓**

簡體中文關鍵字：恋活 / 恋活 Sunshine 捏人界面卡顿、读取服装后卡顿、突然卡一下、Studio 掉帧、DBDE 卡顿。

**不處理的問題：** DBDE 本身的功能 bug（UI、存讀檔、飾品轉移等），請回報到 [DBDE 原作](https://github.com/Njaecha/DynamicBoneDistributionEditor/issues)。

> **裝了之後，創角畫面靜止時還是會卡？** 同類型的 GC 卡頓還有一個常見原因：**BrowserFolders**（Maker/Studio Browser Folders）。如果 `UserData/coordinate` 資料夾很大（上千個檔案），它的 *Automatically refresh when files change* 選項會讓舊版 Mono 在背景每 0.75 秒左右重新掃描整個資料夾。在 `BepInEx/config/marco.FolderBrowser.cfg` 把它設為 `false`，我們測試時剩下的靜止卡頓就消失了。唯一的代價是在遊戲外新增檔案後，要自己按一下刷新按鈕。

### 解決什麼問題

DBDE 每一幀都會對每個角色目前服裝的每一筆編輯，查詢對應的動態骨骼。這個過程有三處會不停產生記憶體垃圾：

1. **查不到的結果不會快取。** 骨骼不在角色身上時（例如換過服裝），**每一幀**都會搜尋整個角色。
2. 快取命中時用 `List.Any(lambda)` 檢查，每次呼叫都產生物件。
3. `UpdateActiveStack` 用 `FindAll(...).Count()`，每一幀都建立新的 List。

戀活使用舊版 Mono，垃圾回收時會**暫停整個遊戲**來掃描全部記憶體。垃圾越多，就越常出現約 0.4–0.5 秒的「突然卡一下」，讀取服裝之後特別明顯。

### 插件做了什麼

用 Harmony 在執行時修補 DBDE，不修改 DBDE 本身，結果與原版相同：

- **查不到的結果記住 1 秒。** 換衣服、換服裝套數、飾品變更、刷新骨骼清單、重新載入時會立刻清除。
- **快取命中改用不產生垃圾的檢查**，檢查條件相同。
- **`UpdateActiveStack` 改寫成不產生垃圾的版本**，判斷邏輯相同。

### 實測效果（KK 創角畫面）

| | 修正前 | 修正後 |
|---|---|---|
| DBDE 每 10 秒產生的垃圾 | 24–66 MB | 約 2 MB |
| DBDE 每幀 CPU 時間 | 約 1–3 ms | 不到 0.1 ms |

測試環境：Ryzen 9 9950X3D，量測時掛著分析工具。記憶體垃圾量主要取決於角色和 DBDE 編輯的數量，跟硬體關係不大；CPU 時間會隨 CPU 而不同。整體 FPS 取決於整台電腦和場景，所以這裡不提供 FPS 數據。

Studio 裡每個角色都會各自執行這個流程，角色越多效果越明顯。

### 相容性

- DBDE **1.5.1**：**正式支援，已在遊戲中實測**。目前 HF Patch 和 BetterRepack 內附的就是這個版本。
- DBDE **2.0.0**：**實驗性，尚未驗證**。已比對原始碼，邏輯和名稱都相同，但沒有在遊戲中實測過。插件會照常啟用，並在 log 顯示 `EXPERIMENTAL` 警告。使用 2.0.0 的人歡迎自行測試並[回報](#回報使用結果)，沒問題也請回報。
- 其他版本：插件**不會啟用**，並在 log 顯示警告，避免蓋掉新版 DBDE 的行為。

### 安裝

1. 從 [Releases](../../releases) 下載對應遊戲的 DLL：
   - 戀活 / Koikatsu Party：`KK_DBDELookupCache.dll`
   - 戀活 Sunshine：`KKS_DBDELookupCache.dll`
2. 放進遊戲資料夾的 `BepInEx/plugins`。
3. 啟動遊戲後，`output_log.txt` 裡應該會出現 `DBDE Lookup Cache` 的 `patched lookup ...` 訊息。

### 設定

設定檔是 `BepInEx/config/jotorola.dbde.lookupcache.cfg`：

- `Enabled`：設為 `false` 就恢復 DBDE 原本的行為，可以用來對照測試。
- `Miss cache seconds`：查不到的結果記住幾秒。
- `Ignore DBDE version check`：在未確認過的 DBDE 版本上強制啟用，風險自負。

### 回報使用結果

歡迎在 [Issues](../../issues) 回報：遊戲（KK/KKS）、創角或 Studio、DBDE 版本、角色數量、使用前後的差異（卡頓頻率、FPS），或遇到的問題以及相關的 log。

### 製作

- **程式碼：** 由 Anthropic 的 AI 助理 **Claude** 撰寫，包括卡頓的診斷（把 GC 停頓一路追到 DBDE 每幀的骨骼查詢）和插件本身。
- **問題回報、方向與遊戲內測試：** [Jotorola-scv](https://github.com/Jotorola-scv)，也是這個專案的維護者。
- DynamicBoneDistributionEditor 原作：[Njaecha](https://github.com/Njaecha/DynamicBoneDistributionEditor)。本專案與 DBDE 無關，不包含任何 DBDE 的程式碼。

---

## 日本語の概要

**DBDE Lookup Cache** は、コイカツ（KK）／コイカツ・サンシャイン（KKS）用 MOD [DynamicBoneDistributionEditor（DBDE）](https://github.com/Njaecha/DynamicBoneDistributionEditor) のパフォーマンス修正プラグインです。キャラメイクと CharaStudio に対応しています。

こんな症状に効きます：

- **キャラメイクで数秒おきに 0.5 秒ほどフリーズする**。特に**コーデ（服装）を読み込んだ後**
- キャラメイクで**何も操作していなくてもカクつく**
- **スタジオのフレームレートが不安定、重い**。揺れもの（ダイナミックボーン）を使うキャラが多いほど悪化する

DBDE が毎フレーム生成しているガベージ（不要なメモリ）をなくし、GC による一時停止を減らします。DBDE 本体は変更せず、動作結果も同じです。

- 対応：DBDE **1.5.1**（動作確認済み、HF Patch と BetterRepack に同梱の版）、**2.0.0**（実験的、未検証）
- 何もしていない時のカクつきが残る場合：**BrowserFolders** の `Automatically refresh when files change`（`BepInEx/config/marco.FolderBrowser.cfg`）を `false` にすると改善することがあります（`UserData/coordinate` のファイル数が多い環境）。
- インストール：[Releases](../../releases) から `KK_DBDELookupCache.dll`（コイカツ）または `KKS_DBDELookupCache.dll`（サンシャイン）をダウンロードし、`BepInEx/plugins` に入れてください。
