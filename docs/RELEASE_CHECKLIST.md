# Release checklist

目前不是正式 Release，禁止建立 Tag 或 GitHub Release。

最新本機候選：`artifacts/release-20260823-15/win-x64/EventFast.exe`

SHA256：`79f8cbb098bcd656e916d2b0a245f41dedbc22cda24c5a67c7350bc5fd8d077d`

- [x] .NET 10 Release build 成功，零警告
- [x] Parser、XPath、Grouping、Classifier、Sorting、Filter、Export mapping 測試成功
- [x] System、Application、Setup 真實 Event Log 整合抽樣成功
- [x] win-x64 self-contained single-file publish 成功
- [x] 三次全新 bundle extraction 冷啟動 777／729／747 ms，中位數 747 ms（門檻 < 1 秒）
- [x] 三次執行中正式候選沒有 TCP／UDP endpoint（門檻皆為 0）
- [x] 三次正常關閉後，正式候選程序皆於 2 秒內完全退出
- [x] Published EXE self-test 與 UI startup smoke 成功
- [x] 第一批 callback 回傳 Native System／EventData 且不 render 完整 XML；三個 Channel 各 25 筆與事件 XML 逐欄一致，查詢工作不在 UI thread
- [x] 10k／100k／500k／1M 群組及 XLSX benchmark 完成
- [x] 產生的 XLSX 已由本機 Microsoft Excel 開啟並確認兩張工作表
- [x] 500 次 native query/message 循環（handle -3、private memory +0.6 MB，門檻 +10／+32 MB）
- [x] SHA256 產生完成
- [x] `scripts/release.ps1` 已在乾淨工作樹完整執行成功
- [ ] GitHub Actions CI 已設定，但尚未由遠端 runner 執行
- [ ] Windows Event Viewer 同機對照中位數至少快 2 倍
- [x] 匯出的真實 `.evtx`、破損／空 `.evtx` 測試
- [x] 62,031 筆公開真實 EVTX 完整讀取（第一批 12.9 ms、完整查詢 0.82 s、44.9 MB managed memory）
- [x] 非系統管理員讀取受保護 `.evtx` 權限映射測試
- [ ] 100,000+ 事件的大型真實 `.evtx` 測試
- [x] Native 查詢超過每 Channel 1,000,000 筆時明確提示，不靜默截斷
- [x] XLSX 精確 `ERROR_DISK_FULL`（112）故障注入：保留原檔並清除暫存檔
- [x] WSL 隔離 4 MB tmpfs 實際耗盡：Windows 回傳 `0x80070070`，原 XLSX 保留、暫存檔清除
- [x] 120 分鐘 native query/message soak（300,228 次、handle -3、private memory +17.2 MB）
- [x] UI 自動驗證群組事件展開與查詢完成後保留排序
- [x] UI 驗證（命令列自動查詢、雙 Channel 36 筆／174 ms；候選檔 Enter、雙擊、Esc、Message/XML）
- [ ] 無 .NET Runtime／SDK 的 Clean Windows 測試
- [ ] 人工確認發布
