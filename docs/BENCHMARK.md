# EventFast Benchmark

測試日期：2026-08-23
環境：Windows 11、Intel Core i7-13700HX（24 logical processors）、31.7 GB RAM、.NET SDK 10.0.400、Release x64。

正式 self-contained single-file 三次全新 bundle extraction 冷啟動：816／687／700 ms，中位數 700 ms；三次執行中 TCP／UDP endpoint 均為 0，正常關閉後皆於 2 秒內完全退出。

## 真實 System Event Log

| Case | Events | First batch | Total | CPU | Peak RAM | Events/s |
|---|---:|---:|---:|---:|---:|---:|
| 24h Warning+ | 20 | 23.8 ms | 31.4 ms | 15.6 ms | 33.7 MB | 637 |
| 30d Event 51 | 385 | 4.0 ms | 26.7 ms | 0.0 ms | 41.0 MB | 14,393 |
| 30d Event 153 | 416 | 6.1 ms | 25.3 ms | 31.2 ms | 48.1 MB | 16,463 |
| Application 30d Event 1000 | 9,690 | 7.7 ms | 499.9 ms | 453.1 ms | 61.0 MB | 19,386 |
| 30d Disk/NTFS Provider + ID | 2,022 | 5.0 ms | 89.3 ms | 78.1 ms | 62.5 MB | 22,632 |

完整 Message 關鍵字 `disk`：90.1 ms；相同 24 小時查詢 cold／warm cache：13.9 ms／0.085 ms。

120 分鐘 native query/message soak：300,228 次，handle -3，private memory +17.2 MB。

## 合成大型資料

| Events | Group | Groups | XLSX export | Export/s | XLSX | Managed RAM |
|---:|---:|---:|---:|---:|---:|---:|
| 10,000 | 28.3 ms | 50 | 110.2 ms | 90,777 | 0.7 MB | 24.5 MB |
| 100,000 | 140.7 ms | 50 | 1,012.4 ms | 98,778 | 7.1 MB | 30.4 MB |
| 500,000 | 487.8 ms | 50 | 1,476.7 ms | 338,587 | 35.7 MB | 147.4 MB |
| 1,000,000 | 505.6 ms | 50 | 2,943.0 ms | 339,792 | 71.2 MB | 388.3 MB |

執行：

```powershell
dotnet run --project benchmarks/EventFast.Benchmarks -c Release -- --large
```

以上是單次開發機量測，不是 Windows Event Viewer 對照中位數，也不是所有硬體的保證值。

## 真實大型 EVTX

JPCERT/CC LogonTracer 固定 commit `b2c2fc6` 的 `Security.evtx`：

- SHA256：`b3f8498d8a99740f7381518fd332cbb67c0bfed0a5b4320d407e485b3ee682fb`
- Windows `wevtutil` 記錄數：62,031
- EventFast 第一批：3.5 ms
- 完整查詢：1.68 s
- 群組：149.0 ms／61 groups
- Managed memory：44.9 MB

這份樣本證明超過舊 50k 上限的完整讀取，但沒有達到 100k gate。
