# EventFast v1.1.1

## 修正與改善

- Excel 匯出完整訊息／XML 改為批次範圍讀取，100 筆原生匯出由約 4.3 秒降至約 0.8 秒。
- 不需要 XML 時不再渲染 XML；找不到 provider metadata 時快取結果，避免重複查詢。
- 搜尋可輸入多個事件 ID，查詢以分組 XPath 避免超過 Windows 條件數上限。
- 快捷條件與問題分類共用同一份規則；新增 Kernel-Power 41 至電源問題。
- 切換語言時重新標示既有分組與嚴重性，不再重新分組，且保持預設排序。
- 快取大小估算納入訊息內容。

## English

- Batched range reads for full Message/XML export; 100-event native export dropped from about 4.3 s to 0.8 s.
- Skip XML rendering when it is not exported, and cache missing provider metadata.
- Search accepts multiple event IDs, with nested XPath groups to stay under the Windows term limit.
- Quick filters and problem classification now share one rule table; Kernel-Power 41 added to power issues.
- Language switching relabels existing groups and severity without regrouping.
- Cache size estimates include message text.

## 下載與驗證 / Download and verification

`EventFast-v1.1.1-win-x64.exe`：Windows 10／11 x64，單檔 self-contained，不需安裝 .NET。
以同頁 `.sha256` 檔案核對下載檔雜湊。

驗證涵蓋 Release 建置、一般／原生整合／UI 測試。
完整 Windows／DPI 矩陣與百萬筆匯出壓力測試不在本次驗證範圍。
