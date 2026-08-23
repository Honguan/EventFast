# EventFast

Windows Event Log 高速查詢工具的 v0.1 原型。

```powershell
dotnet run
dotnet run -- --self-test
dotnet publish -p:PublishProfile=win-x64
```

目前支援 System／Application、最近 24 小時、事件等級、Event ID、Provider、批次原生查詢與事件 XML 詳情。
