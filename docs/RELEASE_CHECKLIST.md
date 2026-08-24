# Release checklist

## v1.0.0 Release gate

候選：`artifacts/release-20260824-02/EventFast-v1.0.0-win-x64.exe`

SHA256：`0db2b1803ebc4a603f380cdec0569e4ad2591462af792cfd3b8d2ce11a501ca9`

驗證紀錄：`artifacts/release-20260824-02/verification.txt`、`benchmark.txt`、`startup.txt`、`privacy.txt`、`lifecycle.txt`

遠端 repository：`Honguan/EventFast`，分支：`main`

正式 Release：<https://github.com/Honguan/EventFast/releases/tag/v1.0.0>

- [x] Release build 成功，零警告
- [x] win-x64 self-contained EXE 產生並可啟動
- [x] System／Application、Event ID、Provider、時間與 Level 查詢
- [x] 問題群組、排序、群組事件與完整 Details
- [x] Excel 匯出、鎖檔、磁碟不足、取消與原檔保護
- [x] 自動測試無重大失敗
- [x] Native leak loop：500 次，handle 0、private memory +4.2 MB
- [x] 本機 Microsoft Excel 開啟兩張工作表
- [x] 本機實際使用：雙 Channel 查詢、UI、Details、Excel 與錯誤路徑正常
- [x] GitHub Release 已核准並發布；EXE 遠端 digest 與本機 SHA256 一致

## 可選、非 Release Gate

- CI／Clean Windows／完整 Windows 版本矩陣
- 100k+ 真實 EVTX／Event Viewer 2× 比較
- GUI bridge／多裝置／ARM64／Code Signing
