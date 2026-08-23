# EventFast

Windows Event Log 高速查詢工具。

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
EventFast.exe --query disk
EventFast.exe C:\Logs\system.evtx
```

需要 .NET 10 SDK；正式發布產物為 Windows x64 self-contained single-file。

目前支援：

- System／Application 原生平行批次查詢，直接讀 System／EventData 欄位，完整 Message／XML 延後載入
- 1 小時至 30 天／自訂時間、等級、Event ID、完整 Message 文字與混合條件
- 快速問題分類、重複事件群組、虛擬化群組事件明細、排序與 RAM 快取
- 選取時才載入完整 Message 與 XML
- 串流 `.xlsx` 問題摘要／完整事件匯出
- 查詢啟動參數、`.evtx` 啟動參數與拖放離線分析

正式 Release 尚須完成企劃中的完整測試、Benchmark、.NET 10 單檔發布與 Clean Machine gate。

GitHub Actions 僅執行 build 與測試；沒有自動 Tag 或 Release。`release.ps1` 也只建立本機候選檔，仍需人工完成 Release gate。
