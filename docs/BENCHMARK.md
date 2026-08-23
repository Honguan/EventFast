# EventFast Benchmark

測試日期：2026-08-23
環境：Windows 11、Intel Core i7-13700HX（24 logical processors）、31.7 GB RAM、.NET SDK 10.0.400、Release x64。

## 真實 System Event Log

| Case | Events | First batch | Total | CPU | Peak RAM | Events/s |
|---|---:|---:|---:|---:|---:|---:|
| 24h Warning+ | 20 | 132.4 ms | 132.7 ms | 125.0 ms | 33.6 MB | 151 |
| 30d Event 51 | 385 | 10.4 ms | 25.5 ms | 15.6 ms | 40.9 MB | 15,126 |
| 30d Event 153 | 416 | 10.5 ms | 22.8 ms | 0.0 ms* | 47.9 MB | 18,206 |

\* `Process.TotalProcessorTime` 的單次取樣解析度不足；不可解讀為沒有使用 CPU。

## 合成大型資料

| Events | Group | Groups | XLSX export | XLSX | Managed RAM |
|---:|---:|---:|---:|---:|---:|
| 10,000 | 27.5 ms | 50 | 100.5 ms | 0.7 MB | 11.7 MB |
| 100,000 | 132.5 ms | 50 | 857.4 ms | 7.1 MB | 30.2 MB |
| 500,000 | 571.3 ms | 50 | 1,341.5 ms | 35.5 MB | 148.8 MB |
| 1,000,000 | 482.4 ms | 50 | 2,679.6 ms | 71.3 MB | 387.2 MB |

執行：

```powershell
dotnet run --project benchmarks/EventFast.Benchmarks -c Release -- --large
```

以上是單次開發機量測，不是 Windows Event Viewer 對照中位數，也不是所有硬體的保證值。

## 真實大型 EVTX

Yamato Security `hayabusa-evtx` 的 `security_big_sample.evtx`：

- SHA256：`b3f8498d8a99740f7381518fd332cbb67c0bfed0a5b4320d407e485b3ee682fb`
- Windows `wevtutil` 記錄數：62,031
- EventFast 第一批：119.0 ms
- 完整查詢：2.29 s
- 群組：95.3 ms／61 groups
- Managed memory：47.4 MB

這份樣本證明超過舊 50k 上限的完整讀取，但沒有達到 100k gate。
