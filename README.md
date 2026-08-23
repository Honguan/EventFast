# EventFast

Windows Event Log 高速查詢工具。

```powershell
dotnet run
dotnet run -- --self-test
dotnet publish -p:PublishProfile=win-x64
```

目前支援：

- System／Application 原生平行批次查詢
- 1 小時至 30 天、等級、Event ID、文字與混合條件
- 快速問題分類、重複事件群組、排序與 RAM 快取
- 選取時才載入完整 Message 與 XML
- 串流 `.xlsx` 問題摘要／完整事件匯出

正式 Release 尚須完成企劃中的完整測試、Benchmark、.NET 10 單檔發布與 Clean Machine gate。
