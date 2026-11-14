$ErrorActionPreference = 'Stop'

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
$temporaryArchivePath = Join-Path $portableDirectory ("DesktopGuard-win-x64.{0}.tmp.zip" -f [Guid]::NewGuid().ToString('N'))
$backupArchivePath = Join-Path $portableDirectory ("DesktopGuard-win-x64.{0}.backup.zip" -f [Guid]::NewGuid().ToString('N'))
try {
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
