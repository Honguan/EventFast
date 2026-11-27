# EventFast v1.2.0

## 新增與改善

- 問題列表在問題名稱後顯示最新事件的內容摘要，同名問題可直接分辨；滑鼠停留顯示完整內容。
- 嚴重性以顏色標示：嚴重（紅色粗體）、錯誤（橘紅）、警告（琥珀色），文字仍保留。
- 視窗高度不足時，快捷條件收合為「快捷條件 ▾」按鈕，結果列表保留更多空間。
- 匯出範圍加上「匯出：」標籤；詳細資料區改為與篩選區一致的卡片樣式。
- 次數、Event ID、Record ID 欄位改為靠右對齊。

## 修正

- 啟用篩選時下拉選單文字貼頂、內距消失。
- 日期欄位文字未垂直置中。

## English

- Problem rows show a one-line summary of the latest event, so groups with the same label can be told apart; hover for the full text.
- Severity is color-coded (Critical, Error, Warning) while keeping the text label.
- Short windows collapse quick filters behind a "Quick filters ▾" toggle.
- Added an "Export:" label, card styling for the details pane, and right-aligned numeric columns.
- Fixed active filter drop-downs losing alignment and padding, and date text not vertically centered.

## 下載與驗證 / Download and verification

`EventFast-v1.2.0-win-x64.exe`：Windows 10／11 x64，單檔 self-contained，不需安裝 .NET。
以同頁 `.sha256` 檔案核對下載檔雜湊。

驗證涵蓋 Release 建置、一般／原生整合／UI／Excel 測試，以及中英文三種視窗尺寸的離屏截圖版面檢查。
完整 Windows／DPI 矩陣與百萬筆匯出壓力測試不在本次驗證範圍。
