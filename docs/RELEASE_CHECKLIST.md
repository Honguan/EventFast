# Release checklist

目前不是正式 Release，禁止建立 Tag 或 GitHub Release。

最新本機候選：`artifacts/release-20260823-1/win-x64/EventFast.exe`

SHA256：`cd17ee9f24a800573b4e61683d4df2b2fd2c1bfc5793235903d799f800357518`

- [x] .NET 10 Release build 成功，零警告
- [x] Parser、XPath、Grouping、Classifier、Sorting、Filter、Export mapping 測試成功
- [x] System、Application、Setup 真實 Event Log 整合抽樣成功
- [x] win-x64 self-contained single-file publish 成功
- [x] Published EXE self-test 與 UI startup smoke 成功
- [x] 第一批 callback 在真實 Event Log 整合測試成功，查詢工作不在 UI thread
- [x] 10k／100k／500k／1M 群組及 XLSX benchmark 完成
- [x] 產生的 XLSX 已由本機 Microsoft Excel 開啟並確認兩張工作表
- [x] 500 次 native query/message 循環（handle -2、private memory +17.2 MB，門檻 +10／+32 MB）
- [x] SHA256 產生完成
- [x] `scripts/release.ps1` 已在乾淨工作樹完整執行成功
- [ ] GitHub Actions CI 已設定，但尚未由遠端 runner 執行
- [ ] Windows Event Viewer 同機對照中位數至少快 2 倍
- [x] 匯出的真實 `.evtx`、破損／空 `.evtx` 測試
- [x] 62,031 筆公開真實 EVTX 完整讀取（第一批 119.0 ms、47.4 MB managed memory）
- [x] 非系統管理員讀取受保護 `.evtx` 權限映射測試
- [ ] 100,000+ 事件的大型真實 `.evtx` 測試
- [ ] 磁碟空間不足時的 Excel 匯出測試
- [ ] 長時間（數小時）handle leak／memory leak soak test
- [ ] 查詢期間 UI 自動化與無凍結驗證
- [ ] 無 .NET Runtime／SDK 的 Clean Windows 測試
- [ ] 人工確認發布
