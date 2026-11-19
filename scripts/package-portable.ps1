$ErrorActionPreference = 'Stop'

function ConvertTo-PortableReadmeInlineHtml {
    param([Parameter(Mandatory)][string]$Text)

    $html = [System.Net.WebUtility]::HtmlEncode($Text)
    $linkEvaluator = [System.Text.RegularExpressions.MatchEvaluator] {
        param($match)

        $target = $match.Groups[2].Value
        if ($target -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*(?:#[A-Za-z0-9._-]+)?$') {
            throw "Portable README links must point to a local package file or anchor: $target"
        }

        '<a href="{0}">{1}</a>' -f $target, $match.Groups[1].Value
    }
    $html = [regex]::Replace($html, '\[([^\]]+)\]\(([^)]+)\)', $linkEvaluator)
    $html = [regex]::Replace($html, '`([^`]+)`', '<code>$1</code>')
    $html = [regex]::Replace($html, '\*\*(.+?)\*\*', '<strong>$1</strong>')
    return $html
}

function Add-PortableReadmeParagraph {
    param(
        [Parameter(Mandatory)][System.Text.StringBuilder]$Builder,
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Lines
    )

    if ($Lines.Count -eq 0) {
        return
    }

    $paragraph = (@($Lines | ForEach-Object { ConvertTo-PortableReadmeInlineHtml -Text $_ }) -join ' ')
    [void]$Builder.AppendLine("<p>$paragraph</p>")
    $Lines.Clear()
}

function Close-PortableReadmeList {
    param(
        [Parameter(Mandatory)][System.Text.StringBuilder]$Builder,
        [Parameter(Mandatory)][ref]$ListTag,
        [Parameter(Mandatory)][ref]$ListItemOpen
    )

    if (-not $ListTag.Value) {
        return
    }

    if ($ListItemOpen.Value) {
        [void]$Builder.AppendLine('</li>')
    }
    [void]$Builder.AppendLine("</$($ListTag.Value)>")
    $ListTag.Value = $null
    $ListItemOpen.Value = $false
}

function ConvertTo-PortableReadmeHtml {
    param([Parameter(Mandatory)][string]$Markdown)

    $builder = [System.Text.StringBuilder]::new()
    $paragraphLines = [System.Collections.Generic.List[string]]::new()
    $listTag = $null
    $listItemOpen = $false

    foreach ($line in ($Markdown -split "`r?`n")) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            Add-PortableReadmeParagraph -Builder $builder -Lines $paragraphLines
            Close-PortableReadmeList -Builder $builder -ListTag ([ref]$listTag) -ListItemOpen ([ref]$listItemOpen)
            continue
        }

        if ($line -match '^\s{0,3}(#{1,6})\s+(.+)$') {
            Add-PortableReadmeParagraph -Builder $builder -Lines $paragraphLines
            Close-PortableReadmeList -Builder $builder -ListTag ([ref]$listTag) -ListItemOpen ([ref]$listItemOpen)
            $level = $Matches[1].Length
            $heading = ConvertTo-PortableReadmeInlineHtml -Text $Matches[2]
            [void]$builder.AppendLine("<h$level>$heading</h$level>")
            continue
        }

        if ($line -match '^\s*(?:>|```|~~~|!\[|\|)') {
            throw "Portable README contains unsupported Markdown syntax: $line"
        }

        $newListTag = $null
        $listContent = $null
        if ($line -match '^\s*\d+\.\s+(.+)$') {
            $newListTag = 'ol'
            $listContent = $Matches[1]
        }
        elseif ($line -match '^\s*[-*+]\s+(.+)$') {
            $newListTag = 'ul'
            $listContent = $Matches[1]
        }

        if ($newListTag) {
            Add-PortableReadmeParagraph -Builder $builder -Lines $paragraphLines
            if ($listTag -and $listTag -ne $newListTag) {
                Close-PortableReadmeList -Builder $builder -ListTag ([ref]$listTag) -ListItemOpen ([ref]$listItemOpen)
            }
            if (-not $listTag) {
                $listTag = $newListTag
                [void]$builder.AppendLine("<$listTag>")
            }
            if ($listItemOpen) {
                [void]$builder.AppendLine('</li>')
            }
            $item = ConvertTo-PortableReadmeInlineHtml -Text $listContent
            [void]$builder.Append("<li>$item")
            $listItemOpen = $true
            continue
        }

        if ($listTag -and $line -match '^\s{2,}\S') {
            if (-not $listItemOpen) {
                throw "Portable README has a list continuation without a list item: $line"
            }
            $continuation = ConvertTo-PortableReadmeInlineHtml -Text $line.Trim()
            [void]$builder.Append("<br />$continuation")
            continue
        }

        Close-PortableReadmeList -Builder $builder -ListTag ([ref]$listTag) -ListItemOpen ([ref]$listItemOpen)
        $paragraphLines.Add($line)
    }

    Add-PortableReadmeParagraph -Builder $builder -Lines $paragraphLines
    Close-PortableReadmeList -Builder $builder -ListTag ([ref]$listTag) -ListItemOpen ([ref]$listItemOpen)

    $documentPrefix = @'
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<link rel="icon" type="image/x-icon" href="DesktopGuard.ico">
<title>Desktop Guard portable guide</title>
<style>
:root { color-scheme: light dark; font: 16px/1.65 "Segoe UI", sans-serif; }
body { margin: 0; padding: 2rem 1rem; background: #eef2f7; color: #172033; }
.page { max-width: 54rem; margin: 0 auto; }
.brand { display: flex; align-items: center; gap: .8rem; margin: 0 0 1rem .25rem; color: #34435c; font-weight: 600; }
.brand img { width: 40px; height: 40px; }
main { padding: clamp(1.25rem, 4vw, 2.5rem); border: 1px solid #d8dfeb; border-radius: 14px; background: #fff; box-shadow: 0 8px 24px #17203312; }
h1, h2, h3 { line-height: 1.25; color: #152b50; }
h1 { margin-top: 0; font-size: clamp(2rem, 5vw, 2.6rem); }
h2 { margin-top: 2rem; padding-bottom: .35rem; border-bottom: 1px solid #e2e8f0; }
a { color: #0759a5; text-underline-offset: .15em; }
a:focus-visible { outline: 3px solid #ed9a00; outline-offset: 3px; }
code { padding: .08em .3em; border-radius: 4px; background: #edf1f6; font-family: Consolas, monospace; overflow-wrap: anywhere; }
li { padding-left: .2rem; }
li + li { margin-top: .55rem; }
@media (prefers-color-scheme: dark) {
  body { background: #111827; color: #e5eaf2; }
  .brand { color: #cad5e5; }
  main { border-color: #354258; background: #1d2738; box-shadow: none; }
  h1, h2, h3 { color: #e3edff; }
  h2 { border-color: #354258; }
  a { color: #8cc7ff; }
  code { background: #303b4e; }
}
</style>
</head>
<body>
<div class="page">
<header class="brand"><img src="DesktopGuard.ico" alt=""><span>Portable guide</span></header>
<main>
'@
    $documentSuffix = @'
</main>
</div>
</body>
</html>
'@
    return $documentPrefix + $builder.ToString() + $documentSuffix
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$solutionPath = Join-Path $repositoryRoot 'DesktopGuard.sln'
$projectPath = Join-Path $repositoryRoot 'src/DesktopGuard.App/DesktopGuard.App.csproj'
$publishPath = Join-Path $repositoryRoot 'src/DesktopGuard.App/bin/Release/net10.0-windows10.0.22621.0/win-x64/publish-singlefile'
$executablePath = Join-Path $publishPath 'DesktopGuard.App.exe'
$artifactRoot = Join-Path $repositoryRoot 'artifacts'
$portableDirectory = Join-Path $artifactRoot 'portable'
$archivePath = Join-Path $portableDirectory 'DesktopGuard-win-x64.zip'

if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) {
    throw "Solution file was not found: $solutionPath"
}

foreach ($directoryPath in @($artifactRoot, $portableDirectory)) {
    if (Test-Path -LiteralPath $directoryPath) {
        $directory = Get-Item -LiteralPath $directoryPath -Force
        if (-not $directory.PSIsContainer -or ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Packaging directory must be a regular directory: $directoryPath"
        }
    }
    else {
        New-Item -ItemType Directory -Path $directoryPath | Out-Null
    }
}

if (Test-Path -LiteralPath $archivePath) {
    $archiveItem = Get-Item -LiteralPath $archivePath -Force
    if ($archiveItem.PSIsContainer -or ($archiveItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Package output must be a regular file: $archivePath"
    }
}

& dotnet restore $solutionPath --locked-mode
if ($LASTEXITCODE -ne 0) {
    throw "Locked restore failed with exit code $LASTEXITCODE."
}

& dotnet publish $projectPath -c Release -p:PublishProfile=SelfContainedSingleFile -p:DebugSymbols=false -p:DebugType=None --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Published executable was not found: $executablePath"
}

$packageFiles = @(
    [pscustomobject]@{
        SourcePath = $executablePath
        ArchiveName = 'DesktopGuard.App.exe'
    },
    [pscustomobject]@{
        SourcePath = Join-Path $repositoryRoot 'README.portable.md'
        ArchiveName = 'README.md'
    },
    [pscustomobject]@{
        SourcePath = Join-Path $repositoryRoot 'src/DesktopGuard.App/Resources/DesktopGuard.ico'
        ArchiveName = 'DesktopGuard.ico'
    },
    [pscustomobject]@{
        SourcePath = Join-Path $repositoryRoot 'LICENSE'
        ArchiveName = 'LICENSE'
    }
)
foreach ($packageFile in $packageFiles) {
    if (-not (Test-Path -LiteralPath $packageFile.SourcePath -PathType Leaf)) {
        throw "Required package file was not found: $($packageFile.SourcePath)"
    }
    $file = Get-Item -LiteralPath $packageFile.SourcePath -Force
    if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Package inputs must not be reparse points: $($packageFile.SourcePath)"
    }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$temporaryReadmePath = Join-Path $portableDirectory ("DesktopGuard-win-x64.{0}.README.html" -f [Guid]::NewGuid().ToString('N'))
$temporaryArchivePath = Join-Path $portableDirectory ("DesktopGuard-win-x64.{0}.tmp.zip" -f [Guid]::NewGuid().ToString('N'))
$backupArchivePath = Join-Path $portableDirectory ("DesktopGuard-win-x64.{0}.backup.zip" -f [Guid]::NewGuid().ToString('N'))
try {
    $readmeSourcePath = Join-Path $repositoryRoot 'README.portable.md'
    $readmeReader = [IO.StreamReader]::new($readmeSourcePath, [Text.UTF8Encoding]::new($false, $true), $true)
    try {
        $readmeMarkdown = $readmeReader.ReadToEnd()
    }
    finally {
        $readmeReader.Dispose()
    }

    $readmeHtml = ConvertTo-PortableReadmeHtml -Markdown $readmeMarkdown
    [IO.File]::WriteAllText($temporaryReadmePath, $readmeHtml, [Text.UTF8Encoding]::new($false))
    $packageFiles += [pscustomobject]@{
        SourcePath = $temporaryReadmePath
        ArchiveName = 'README.html'
    }

    $archive = [IO.Compression.ZipFile]::Open($temporaryArchivePath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($packageFile in $packageFiles) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive,
                $packageFile.SourcePath,
                $packageFile.ArchiveName,
                [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
    }

    if (Test-Path -LiteralPath $archivePath -PathType Leaf) {
        [IO.File]::Replace($temporaryArchivePath, $archivePath, $backupArchivePath)
    }
    else {
        [IO.File]::Move($temporaryArchivePath, $archivePath)
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryReadmePath -PathType Leaf) {
        [IO.File]::Delete($temporaryReadmePath)
    }
    if (Test-Path -LiteralPath $temporaryArchivePath -PathType Leaf) {
        [IO.File]::Delete($temporaryArchivePath)
    }
    if (Test-Path -LiteralPath $backupArchivePath -PathType Leaf) {
        if (Test-Path -LiteralPath $archivePath -PathType Leaf) {
            [IO.File]::Delete($backupArchivePath)
        }
        else {
            [IO.File]::Move($backupArchivePath, $archivePath)
        }
    }
}

Write-Output "Created portable package: $archivePath"
