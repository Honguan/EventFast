# 介面與操作改善紀錄

檢查日期：2026-09-07。範圍：主視窗、篩選與來源切換、查詢生命週期、問題分組、詳細資料、雙語、Excel 匯出及既有測試。

## 修改前

- .NET 10 WPF，可攜式 Windows x64 工具，未使用第三方 UI 套件。
- 初始位於 `main`，工作目錄乾淨；版本為 1.0.5。
- 已有原生事件讀取、快取、快捷查詢、EVTX 拖曳、分組、訊息/XML、Excel 與雙語功能。
- 原有 18 組一般測試與原生整合測試可通過，但沒有涵蓋下列操作邊界。
- 固定水平工具列在 820 寬、自訂日期及英文文字下溢出；搜尋框無可見提示，空表格沒有下一步說明。

## 已修正與補齊

| 問題 | 實作 |
| --- | --- |
| 篩選、語言與狀態擠出視窗 | 分開來源／查詢／結果工具列，篩選換行，短視窗可捲動，狀態獨立顯示 |
| 操作層級不清楚 | 統一按鈕、資料列、字型、邊框、留白與搜尋重點色 |
| EVTX 只能拖曳，無返回入口 | 開啟檔案按鈕、Ctrl+O、來源提示與返回本機 |
| 缺少可見重新整理及完整重設 | F5 對應按鈕略過快取；重設可清除 CLI Provider，離線模式保留全部時間 |
| 搜尋框 Enter 被事件詳細資料攔截 | 只有表格取得焦點時才以 Enter 開啟事件 |
| 手動輸入仍顯示快捷條件，搜尋卻取消快捷 | 手動輸入解除快捷；未改輸入時搜尋保留快捷 |
| 修改條件後難辨識結果是否更新 | 顯示待套用提示；空白、無結果、查詢中與失敗有不同說明 |
| 查詢／匯出可被快捷、F5 或拖曳互相取代 | 共用忙碌守門與控制項狀態，保留取消入口 |
| 取消／例外後預覽與匯出資料不同 | 還原上一次完成的結果；切換來源先清除舊來源結果 |
| 大量資料分組占用 UI 執行緒 | 查詢篩選與分組、匯出重分組移至背景，分組支援取消 |
| 多選匯出只匯出第一組 | 匯出全部選取群組的事件；排序及語言切換保留多選 |
| 「全部事件」容易誤解為所有 Windows 記錄 | 改名「已掃描事件」，仍受本次來源、時間、等級與原生篩選限制 |
| 選取發生紀錄強制切換分頁 | 留在清單；雙擊／Enter 開啟內容，最新事件自動載入 |
| 詳細區高度固定、複製缺少回饋 | 加入分隔線、複製成功提示及剪貼簿忙碌處理，完整內容載入前停用複製 |
| Excel 字數邊界切斷 emoji 或含非法 XML 字元 | 以 Unicode Rune 清理及截斷，保持合法 XML |
| 最後一筆完成後取消仍可能覆寫目的檔 | 完成 ZIP 後、原子替換目的檔前再次檢查取消 |

## 驗證方式

修改檔案：`MainWindow.xaml`、`MainWindow.xaml.cs`、`Localization.cs`、`ProblemGrouping.cs`、`XlsxExporter.cs`、`tests/EventFast.Tests/Program.cs`、`README.md`、`README.zh-TW.md`、`DESIGN.md`、`docs/UI_REVIEW.md`。

本機 PATH 的 SDK 不符合 `global.json`，使用專案已有的 `.tools/dotnet/dotnet.exe`，未修改 SDK 政策。

```powershell
.tools/dotnet/dotnet.exe build -c Release
.tools/dotnet/dotnet.exe run --project tests/EventFast.Tests -c Release -- --integration --ui
.tools/dotnet/dotnet.exe run --project tests/EventFast.Tests -c Release -- --screenshots artifacts/ui-after
```

驗證結果：Release 建置零警告、零錯誤；18 組一般測試、原生整合與 UI 測試通過，7 張離屏截圖通過版面邊界檢查。
測試涵蓋新增的 Unicode／取消收尾、分組取消、多選匯出與排序、重設、忙碌守門、取消還原、Enter、EVTX 往返與最小視窗版面。
修改前後截圖位於本機 `artifacts/ui-before-*`、`artifacts/ui-after-*`，使用合成資料，未納入 Git。

## 驗證界線

本次未宣稱能窮盡所有問題。未執行完整 Windows／DPI／螢幕閱讀器矩陣、百萬筆 EVTX 壓力測試、真正填滿磁碟或系統管理員重新啟動。
既有磁碟不足模擬、檔案鎖定、匯出取消及原檔保護測試仍需通過。
未新增遠端功能、套件或遙測。改善原始提交為 `2afce82`；v1.1.0 發佈說明見 [RELEASE_NOTES.md](RELEASE_NOTES.md)。
