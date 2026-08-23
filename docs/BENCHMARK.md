# EventFast Benchmark

測試日期：2026-08-23
環境：Windows 11、Intel Core i7-13700HX（24 logical processors）、31.7 GB RAM、.NET SDK 10.0.400、Release x64。

正式 self-contained single-file 三次全新 bundle extraction 冷啟動：777／729／747 ms，中位數 747 ms；三次執行中 TCP／UDP endpoint 均為 0，正常關閉後皆於 2 秒內完全退出。

## 真實 System Event Log

| Case | Events | First batch | Total | CPU | Peak RAM | Events/s |
|---|---:|---:|---:|---:|---:|---:|
| 24h Warning+ | 20 | 34.1 ms | 34.7 ms | 15.6 ms | 31.9 MB | 576 |
| 30d Event 51 | 385 | 7.2 ms | 21.5 ms | 0.0 ms | 32.8 MB | 17,920 |
| 30d Event 153 | 416 | 8.3 ms | 19.4 ms | 0.0 ms | 33.5 MB | 21,410 |
| Application 30d Event 1000 | 9,690 | 9.5 ms | 187.2 ms | 109.4 ms | 51.5 MB | 51,753 |
| 30d Disk/NTFS Provider + ID | 2,022 | 8.5 ms | 61.3 ms | 46.9 ms | 54.2 MB | 32,993 |

完整 Message 關鍵字 `disk`：134.5 ms；相同 24 小時查詢 cold／warm cache：17.5 ms／0.114 ms。

120 分鐘 native query/message soak：300,228 次，handle -3，private memory +17.2 MB。

## 合成大型資料

| Events | Group | Groups | XLSX export | Export/s | XLSX | Managed RAM |
|---:|---:|---:|---:|---:|---:|---:|
| 10,000 | 33.5 ms | 50 | 113.5 ms | 88,106 | 0.7 MB | 18.5 MB |
| 100,000 | 164.9 ms | 50 | 1,053.5 ms | 94,923 | 7.1 MB | 25.6 MB |
| 500,000 | 499.0 ms | 50 | 2,329.4 ms | 214,650 | 35.5 MB | 134.6 MB |
| 1,000,000 | 809.6 ms | 50 | 5,683.1 ms | 175,959 | 71.1 MB | 383.8 MB |

執行：

```powershell
dotnet run --project benchmarks/EventFast.Benchmarks -c Release -- --large
```

以上是單次開發機量測，不是 Windows Event Viewer 對照中位數，也不是所有硬體的保證值。

## 真實大型 EVTX

JPCERT/CC LogonTracer 固定 commit `b2c2fc6` 的 `Security.evtx`：

- SHA256：`b3f8498d8a99740f7381518fd332cbb67c0bfed0a5b4320d407e485b3ee682fb`
- Windows `wevtutil` 記錄數：62,031
- EventFast 第一批：12.9 ms
- 完整查詢：0.82 s
- 群組：138.5 ms／61 groups
- Managed memory：44.9 MB

這份樣本證明超過舊 50k 上限的完整讀取，但沒有達到 100k gate。
