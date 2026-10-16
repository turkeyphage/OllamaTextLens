# OllamaTextLens（Windows）

圈選英文、按下快捷鍵，在滑鼠旁查看原文、繁體中文翻譯與簡短白話解釋。使用 Ollama 本機模型，適合閱讀可選取文字的英文 PDF。

專案：[turkeyphage/OllamaTextLens](https://github.com/turkeyphage/OllamaTextLens)。

## GitHub CI/CD 與版本下載

已提供 `.github/workflows/windows.yml`：

| 觸發方式 | 執行內容 |
| --- | --- |
| 推送任意分支、開啟或更新 PR | Windows 編譯與模擬 Ollama 的 smoke tests |
| 推送 `v0.1.0` 等版本標籤 | 先通過上述檢查，再打包並自動建立 GitHub Release |
| Actions → OllamaTextLens CI and Release → Run workflow | 檢查與打包測試版，於該次執行的 Artifacts 下載；不建立 Release |

每個 Release 包含 Windows x64 的單一 EXE、含 EXE 與說明的 ZIP，以及 SHA256 校驗檔。EXE 內含 .NET 執行環境，不需要另外安裝 .NET；**Ollama 與模型不包含在下載檔內，使用者需自行安裝並啟動**。目前未做程式碼簽章，Windows 可能顯示未知發行者。

### 第一次上傳 GitHub

GitHub Repository 已建立。在尚未初始化 Git 的本專案目錄執行：

```powershell
git init -b main
git add .
git commit -m "Initial OllamaTextLens with CI and releases"
git remote add origin https://github.com/turkeyphage/OllamaTextLens.git
git push -u origin main
```

只需提交原始碼；`.gitignore` 已排除建置產物、執行檔和 Ollama 測試紀錄。使用者設定儲存在專案外，不會被提交。若要公開供人修改及散布，請先決定適合的開源授權，再加入 LICENSE。

### 發布版本

先確認 main 的 Actions 檢查成功，再針對要發布的 commit 建立新標籤：

```powershell
git tag -a v0.1.0 -m "Release v0.1.0"
git push origin v0.1.0
```

約定：功能新增用 `v0.2.0`，修正用 `v0.1.1`，測試版本可用 `v0.2.0-rc.1`（自動標為 Pre-release）。檔名與 EXE 的版本資訊會依標籤產生，不需要手動修改 csproj。每次發版請使用新的標籤；流程不覆寫既有 Release。

完成後，在 Repository 的 **Releases → 對應版本 → Assets** 下載 EXE 或 ZIP。工作流程使用 GitHub 自動提供的 `GITHUB_TOKEN`，只有打包／發版 job 有 `contents: write`，不需要另存 PAT 或模型 API key；組織政策仍需允許此權限與所用 Actions。

CI 不會操作真實 PDF 閱讀器或啟動 Ollama。正式發版前，仍需在 Windows 手動確認取詞、浮動位置與真實模型輸出。

本機重現打包（需 PowerShell 與 .NET 10 SDK，以及下載 runtime 所需的網路）：

```powershell
./scripts/publish.ps1 -Version 0.1.0
```

產物位於 `dist/`。工作流程尚需上傳 GitHub 後才能驗證雲端執行結果。

使用 Ollama 在本機產生繁體中文翻譯與白話解釋。原生 WPF 視窗，無第三方 NuGet 套件。

## 執行

1. 安裝並啟動 Ollama，下載能理解英文與繁中的聊天模型。
2. 執行 `publish\OllamaTextLens.exe`（若已建置），或安裝 .NET 10 SDK 後在專案目錄執行 `dotnet run`。
3. 選擇已安裝模型，按「儲存設定」，可用「測試翻譯」確認連線。
4. 在 PDF 閱讀器反白文字，放開滑鼠後按 **Ctrl + Alt + T**，隨即放開按鍵。
5. 滑鼠旁會出現浮動視窗，依序顯示【原文】、【繁體中文翻譯】、【白話解釋】，區塊以留白及分隔線區隔。白話解釋以一到兩句、約 60 個中文字內為原則。可調整大小、捲動閱讀、選取及複製結果，按關閉或在浮窗中按 Esc 停止請求並隱藏。

主視窗可以最小化，關閉主視窗會結束程式。快捷鍵衝突時會提示，請關閉衝突程式後重新啟動。

## 建置

```powershell
dotnet build
dotnet publish -c Release -r win-x64 --self-contained true -o publish --source https://api.nuget.org/v3/index.json
```

自包含發行版不需要另外安裝 .NET，但仍需 Ollama 與本機模型。
第一次自包含打包需要網路下載 .NET 執行環境。離線環境若已安裝 .NET 10 Desktop Runtime，可執行 `dotnet publish -c Release --self-contained false -o publish`。

測試：`dotnet run --project tests/Smoke.csproj`，使用模擬 HTTP 回應驗證模型探索、繁中提示詞、串流組合、截斷偵測及位址檢查，不會呼叫真實模型。

## 行為與限制

- 目前透過 Windows Ctrl+C 取得文字，適用於支援複製的文字型 PDF；不包含 OCR。
- 取詞期間會暫時使用剪貼簿，並嘗試還原原內容；若其他程式同步改寫剪貼簿則不覆蓋。特殊應用程式提供的延遲剪貼簿格式可能無法還原。
- 讀取不到新的剪貼簿內容時不會翻譯舊內容。PDF 閱讀器請與本程式使用相同權限層級。
- 會合併段內換行及英文跨行連字；多欄 PDF 複製順序仍由閱讀器決定。
- 單次最多 12,000 字元，模型回覆最多 1,400 tokens。首次載入與 CPU 推論可能較慢；請求逾時為五分鐘。
- 僅允許本機 HTTP Ollama 位址，停用系統 HTTP proxy；不呼叫雲端翻譯服務。請自行選用 Ollama 的本機模型，不要使用 cloud 模型。
- 程式不儲存原文、翻譯或歷史紀錄。只在 `%LOCALAPPDATA%\OllamaTextLens\settings.json` 儲存位址及模型名稱；Ollama 本身的紀錄由 Ollama 管理。
- 首次使用新名稱版本時，若新設定不存在，會讀取舊版 `%LOCALAPPDATA%\LocalPdfTranslator\settings.json`；按「儲存設定」後寫入新的 OllamaTextLens 設定目錄。
- 翻譯品質、繁中用詞與是否遵循格式取決於模型，需用實際閱讀材料確認。

## 手動驗收

使用 Edge、Adobe Acrobat 等閱讀器的可選文字 PDF，驗證反白取詞、段落換行、無選取不翻舊文字、剪貼簿文字與圖片還原、模型不存在／Ollama 未啟動、取消請求、第二螢幕及不同 DPI 的浮窗位置。受保護 PDF 與掃描 PDF 預期不支援。
