# 把 GodDemo 複製到指定資料夾,並建立三位作者(Alice、Bob、Carol)的 git 歷史,
# 讓你可以實際試「冤親債主」。不會動到原本的資料夾。
# 用法:  powershell -ExecutionPolicy Bypass -File 建立git歷史.ps1 [目的資料夾]
param([string]$Target = (Join-Path $env:TEMP 'GodDemo-git'))

$ErrorActionPreference = 'Stop'
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { Write-Host '找不到 git,請先安裝 Git for Windows 或確認 git 在 PATH。'; exit 1 }
if (Test-Path $Target) { Remove-Item $Target -Recurse -Force }
New-Item -ItemType Directory -Path $Target | Out-Null
Copy-Item (Join-Path $PSScriptRoot '*') $Target -Recurse -Exclude '建立git歷史.ps1'
Set-Location $Target

function Commit($name, $msg) {
    $env:GIT_AUTHOR_NAME = $name; $env:GIT_AUTHOR_EMAIL = "$name@example.test"
    $env:GIT_COMMITTER_NAME = $name; $env:GIT_COMMITTER_EMAIL = "$name@example.test"
    git add -A | Out-Null
    git commit -q -m $msg
}
function Edit($file, $old, $new) {
    $t = [IO.File]::ReadAllText($file, [Text.Encoding]::UTF8)
    if (-not $t.Contains($old)) { throw "找不到要修改的文字:$old" }
    [IO.File]::WriteAllText($file, $t.Replace($old, $new), (New-Object Text.UTF8Encoding($true)))
}

git init -q .
git config core.autocrlf false
git config i18n.commitEncoding UTF-8
$f = Join-Path $Target 'GodForm.cs'

Commit 'Alice' 'feat: 建立檢測主畫面與範例專案'
Edit $f 'txtGain.Text = _gain.ToString();' 'txtGain.Text = _gain.ToString(); // 同步畫面'
Commit 'Bob' 'fix: 儲存設定時同步增益欄位'
Edit $f '_exposure = double.Parse(txtExposure.Text);' '_exposure = double.Parse(txtExposure.Text) * 1.0;'
Edit $f 'public void AddWafer() { _waferCount++; Log("add"); }' 'public void AddWafer() { _waferCount += 1; Log("add"); }'
Commit 'Carol' 'refactor: 調整儲存流程與晶圓計數'
Edit $f 'public string GetLot() { return _lotId; }' 'public string GetLot() { return _lotId ?? ""; }'
Edit $f '* 1.0;' '* 1.0 + 0;'
Commit 'Alice' 'fix: 批號不可為空'

Write-Host ''
Write-Host "已建立:$Target"
Write-Host "接著執行:CodeAtlas.exe debtors `"$Target\GodDemo.sln`" GodDemo.GodForm"
