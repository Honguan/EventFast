# Release checklist

## v1.0.0 Release gate

候選：`artifacts/release-20260824-01/EventFast-v1.0.0-win-x64.exe`

SHA256：`e7a9bbd8376871d54dbcb585cfaf7996297d0a1051160b122d735ed67d9f30fc`

發布仍需專案擁有者明確授權。

- [x] Release build 成功，零警告
- [x] win-x64 self-contained EXE 產生並可啟動
- [x] System／Application、Event ID、Provider、時間與 Level 查詢
- [x] 問題群組、排序、群組事件與完整 Details
- [x] Excel 匯出、鎖檔、磁碟不足、取消與原檔保護
- [x] 自動測試無重大失敗
- [x] Native leak loop 無明顯 memory／handle leak
- [x] 本機 Microsoft Excel 開啟兩張工作表
- [x] 本機實際使用：雙 Channel 查詢、UI、Details、Excel 與錯誤路徑正常
- [ ] 人工核准 GitHub Release

## 可選、非 Release Gate

- CI／Clean Windows／完整 Windows 版本矩陣
- 100k+ 真實 EVTX／Event Viewer 2× 比較
- GUI bridge／多裝置／ARM64／Code Signing
