# EventFast Benchmark

測試日期：2026-08-23  
環境：Windows 11、Intel Core i7-13700HX（24 logical processors）、31.7 GB RAM、.NET SDK 10.0.400、Release x64。

## 真實 System Event Log

| Case | Events | First batch | Total | CPU | Peak RAM | Events/s |
|---|---:|---:|---:|---:|---:|---:|
| 24h Warning+ | 20 | 130.3 ms | 130.5 ms | 109.4 ms | 33.6 MB | 153 |
| 30d Event 51 | 385 | 9.8 ms | 24.3 ms | 31.2 ms | 41.5 MB | 15,817 |
| 30d Event 153 | 416 | 11.6 ms | 27.0 ms | 0.0 ms* | 50.2 MB | 15,414 |

\* `Process.TotalProcessorTime` 的單次取樣解析度不足；不可解讀為沒有使用 CPU。

## 合成大型資料

| Events | Group | Groups | XLSX export | XLSX | Managed RAM |
|---:|---:|---:|---:|---:|---:|
| 10,000 | 26.9 ms | 50 | 95.0 ms | 0.7 MB | 12.8 MB |
| 100,000 | 126.6 ms | 50 | 879.3 ms | 7.1 MB | 29.9 MB |
| 500,000 | 570.1 ms | 50 | 1,448.5 ms | 35.7 MB | 131.3 MB |
| 1,000,000 | 538.7 ms | 50 | 3,154.2 ms | 71.2 MB | 380.2 MB |

執行：

```powershell
dotnet run --project benchmarks/EventFast.Benchmarks -c Release -- --large
```

以上是單次開發機量測，不是 Windows Event Viewer 對照中位數，也不是所有硬體的保證值。
