First public release.

This release removes the per-frame garbage that DynamicBoneDistributionEditor (DBDE) creates in the character maker and CharaStudio. That garbage causes periodic "the whole game freezes for half a second" stutter, especially after loading outfits. See the [README](https://github.com/Jotorola-scv/DBDE-LookupCache#readme) for details and measurements.

### Downloads
| Game | File |
|---|---|
| Koikatsu / Koikatsu Party | `KK_DBDELookupCache.dll` |
| Koikatsu Sunshine | `KKS_DBDELookupCache.dll` |

Put the dll in `BepInEx/plugins` in your game folder.

### DBDE compatibility
- **1.5.1: supported, tested in game.** This is the version shipped by HF Patch and BetterRepack.
- **2.0.0: experimental, untested.** It was checked against the source code only. The plugin runs and logs an `EXPERIMENTAL` warning. Reports are welcome, including "works fine".
- Other versions: the plugin does nothing and logs a warning.

---

首次公開版本。修正 DBDE 在創角畫面和 Studio 每幀產生的記憶體垃圾，減少週期性的「整個遊戲突然停頓一下」，讀取服裝後特別明顯。

- 戀活 / Koikatsu Party：`KK_DBDELookupCache.dll`
- 戀活 Sunshine：`KKS_DBDELookupCache.dll`

放進遊戲資料夾的 `BepInEx/plugins` 即可。

DBDE 1.5.1 正式支援；2.0.0 為實驗性、未驗證，歡迎回報測試結果。

Code written by Claude (Anthropic). Problem report, direction and in-game testing by Jotorola-scv.
