# EventFast v1.1.0

## 新增與改善

- 全新淺色雙語介面：工具列分區、可換行篩選、空結果提示及可調整高度的詳細資料區。
- 開啟 EVTX（Ctrl+O）、返回本機、重新整理（F5）與重設篩選。
- 保留多選並匯出全部選取問題；「已掃描事件」更清楚標示匯出範圍。
- 選取問題自動載入最新事件，瀏覽發生紀錄不再強制切換分頁。

## 修正

- 搜尋框 Enter 被事件詳細資料攔截、手動搜尋與快捷條件狀態不一致。
- 查詢／匯出互相取代、取消後預覽與匯出資料不同。
- 大量資料篩選與分組阻塞介面；背景分組現在可取消。
- Excel 匯出截斷 emoji、非法 XML 字元，以及取消收尾仍覆寫原檔。
- 窄視窗控制項與狀態裁切；剪貼簿忙碌時提供重試提示。

## English

- Redesigned bilingual desktop UI with wrapping filters, clear empty states, and resizable details.
- Added Open EVTX, return to local logs, Refresh, and Reset actions.
- Export all selected problems and preserve selection when sorting or switching languages.
- Fixed Enter handling, quick-filter state, overlapping operations, and cancelled-query restoration.
- Moved filtering/grouping off the UI thread with cancellation support.
- Fixed Unicode/XML export boundaries and cancellation immediately before replacing the destination file.

## 下載與驗證 / Download and verification

`EventFast-v1.1.0-win-x64.exe`：Windows 10／11 x64，單檔 self-contained，不需安裝 .NET。
以同頁 `.sha256` 檔案核對下載檔雜湊。

驗證涵蓋 Release 建置、一般／原生／UI 測試、EVTX 往返、匯出安全及雙語版面。
完整 Windows／DPI 矩陣與百萬筆壓力測試不在本次驗證範圍。
