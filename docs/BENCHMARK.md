# EventFast Benchmark

測試日期：2026-08-23
環境：Windows 11、Intel Core i7-13700HX（24 logical processors）、31.7 GB RAM、.NET SDK 10.0.400、Release x64。

正式 self-contained single-file 三次全新 bundle extraction 冷啟動：809／710／705 ms，中位數 710 ms；三次執行中 TCP／UDP endpoint 均為 0，正常關閉後皆於 2 秒內完全退出。

## 真實 System Event Log

| Case | Events | First batch | Total | CPU | Peak RAM | Events/s |
|---|---:|---:|---:|---:|---:|---:|
| 24h Warning+ | 20 | 130.2 ms | 130.4 ms | 125.0 ms | 38.0 MB | 153 |
| 30d Event 51 | 385 | 9.7 ms | 23.6 ms | 15.6 ms | 45.1 MB | 16,329 |
| 30d Event 153 | 416 | 10.6 ms | 23.4 ms | 31.2 ms | 52.2 MB | 17,783 |
| Application 30d Event 1000 | 9,690 | 13.1 ms | 499.7 ms | 468.8 ms | 61.8 MB | 19,391 |
| 30d Disk/NTFS Provider + ID | 2,019 | 11.9 ms | 93.1 ms | 93.8 ms | 66.5 MB | 21,676 |

完整 Message 關鍵字 `disk`：88.3 ms；相同 24 小時查詢 cold／warm cache：14.1 ms／0.083 ms。

120 分鐘 native query/message soak：300,228 次，handle -3，private memory +17.2 MB。

## 合成大型資料

| Events | Group | Groups | XLSX export | Export/s | XLSX | Managed RAM |
|---:|---:|---:|---:|---:|---:|---:|
| 10,000 | 27.5 ms | 50 | 101.0 ms | 98,989 | 0.7 MB | 11.4 MB |
| 100,000 | 130.7 ms | 50 | 994.2 ms | 100,587 | 7.1 MB | 40.5 MB |
| 500,000 | 478.6 ms | 50 | 1,433.0 ms | 348,918 | 35.7 MB | 142.3 MB |
| 1,000,000 | 503.7 ms | 50 | 2,837.5 ms | 352,421 | 71.2 MB | 384.4 MB |

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
