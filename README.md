# EventFast

EventFast 是可攜式 Windows Event Log 查詢工具，直接讀取 Windows 原生事件 API，集中查詢、分組、檢視並匯出 System、Application 與離線 `.evtx`。

## 下載與需求

從 [EventFast v1.0.0 Release](https://github.com/Honguan/codex-settings/releases/tag/eventfast-v1.0.0) 下載 `EventFast-v1.0.0-win-x64.exe`。支援 Windows 10／11 x64；單檔 self-contained，不需安裝 .NET Runtime 或 SDK，也不需安裝程序。

## 使用方式

直接開啟 EXE，選時間、等級與 Channel，再以 Event ID、Provider、問題名稱或關鍵字搜尋。可把 `.evtx` 拖入視窗，或使用命令列：

```powershell
dotnet run
dotnet run -- --self-test
dotnet publish -p:PublishProfile=win-x64
./scripts/release.ps1 -OutputDirectory artifacts/release-candidate
./scripts/download-large-testdata.ps1
dotnet run --project tests/EventFast.Tests -c Release -- --large-evtx artifacts/testdata/security_big_sample.evtx
dotnet run --project tests/EventFast.Tests -c Release -- --soak-minutes 120
```

發布後可直接使用：

```powershell
EventFast.exe --today
EventFast.exe --hours 24
EventFast.exe --event-id 51
EventFast.exe --provider disk
EventFast.exe --query "disk 153"
EventFast.exe C:\Logs\system.evtx
```

選取問題後可查看每次發生的時間、完整 Message 與 XML。按「匯出 Excel」可輸出問題摘要與完整事件兩張工作表；寫入失敗或取消時會保留原檔並清理暫存檔。

## Screenshot

Environment Limitation：目前建置環境的 GUI capture bridge 無法連線；正式 Release 上傳時補上實機主視窗畫面。此項依企劃 §79 不阻塞 Release。

## 隱私

EventFast 完全在本機處理事件資料，不蒐集遙測、不建立網路連線，也不自動上傳 EVTX 或匯出檔。

## 已知限制

- 單次查詢上限 1,000,000 筆；超過時請縮短時間或增加篩選條件。
- 少數 Provider 無法格式化 Message 時，仍可查看原始事件欄位與 XML。

## License

尚未指定開源授權；未經權利人許可不得再散布或修改。

## 開發與驗證

目前支援：

- System／Application 原生平行批次查詢，直接讀 System／EventData 欄位，完整 Message／XML 延後載入
- 1 小時至 30 天／自訂時間、等級、Event ID、完整 Message 文字與混合條件
- 快速問題分類、重複事件群組、虛擬化群組事件明細、排序與 RAM 快取
- 選取時才載入完整 Message 與 XML
- 串流 `.xlsx` 問題摘要／完整事件匯出
- 查詢啟動參數、`.evtx` 啟動參數與拖放離線分析

正式 Release gate 只以本機功能、匯出安全、測試與人工使用確認為準。CI、Clean Windows、100k+ 真實 EVTX、Event Viewer 速度比較與 GUI bridge 都是可選檢查；`release.ps1 -ExtendedChecks` 才會執行 Excel、leak 與大型 benchmark。
