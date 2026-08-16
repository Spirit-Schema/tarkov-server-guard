# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$InputPath,

    [string[]]$InputLabel,

    [ValidateSet('Auto', 'Directory', 'Setup', 'Archive', 'File')]
    [string[]]$InputMode = @('Auto'),

    [string]$ProjectRoot,

    [string]$AllowlistPath,

    [string]$ReportPath,

    [switch]$ReturnStatus,

    [ValidateRange(1, 100000)]
    [int]$MaximumArchiveEntries = 20000,

    [ValidateRange(1, 500000)]
    [int]$MaximumTotalArchiveEntries = 50000,

    [ValidateRange(1, 4096)]
    [int]$MaximumArchives = 128,

    [ValidateRange(1, 2147483647)]
    [long]$MaximumArchiveEntryBytes = 268435456,

    [ValidateRange(1, 9223372036854775807)]
    [long]$MaximumArchiveExpandedBytes = 2147483648,

    [ValidateRange(1, 100000)]
    [double]$MaximumCompressionRatio = 250,

    [ValidateRange(1, 64)]
    [int]$MaximumArchivePathDepth = 24,

    [ValidateRange(0, 8)]
    [int]$MaximumNestedArchiveDepth = 4
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$script:findings = New-Object Collections.ArrayList
$script:allowlist = @{}
$script:archiveExpandedBytes = [long]0
$script:archiveCount = 0
$script:totalArchiveEntries = 0
$script:reportPathValidated = $false
$script:scanFailed = $false
$script:temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    'TSG-ReleaseScan-' + [Guid]::NewGuid().ToString('N'))
$script:maximumBinaryFileBytes = [long]268435456
$script:maximumTextFileBytes = [long]67108864

if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = Split-Path -Parent $PSScriptRoot
}
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)

if (-not ('TsgReleaseByteStrings' -as [type])) {
    Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public sealed class TsgReleaseByteString
{
    public long Offset { get; set; }
    public string EncodingName { get; set; }
    public string Text { get; set; }
}

public static class TsgReleaseByteStrings
{
    private const int MaximumChunkCharacters = 65536;
    private const int ChunkOverlapCharacters = 512;

    public static List<TsgReleaseByteString> Extract(string path, int minimumCharacters)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var result = new List<TsgReleaseByteString>();
        ExtractAscii(bytes, minimumCharacters, result);
        ExtractUtf16(bytes, 0, minimumCharacters, result);
        ExtractUtf16(bytes, 1, minimumCharacters, result);
        return result;
    }

    private static void ExtractAscii(
        byte[] bytes,
        int minimumCharacters,
        List<TsgReleaseByteString> result)
    {
        int start = -1;
        for (int index = 0; index <= bytes.Length; index++)
        {
            bool printable = index < bytes.Length
                && bytes[index] >= 0x20 && bytes[index] <= 0x7e;
            if (printable)
            {
                if (start < 0) start = index;
                continue;
            }
            if (start >= 0 && index - start >= minimumCharacters)
            {
                string text = Encoding.ASCII.GetString(bytes, start, index - start);
                AddChunks(result, start, 1, "ASCII", text);
            }
            start = -1;
        }
    }

    private static void ExtractUtf16(
        byte[] bytes,
        int alignment,
        int minimumCharacters,
        List<TsgReleaseByteString> result)
    {
        int start = -1;
        int count = 0;
        for (int index = alignment; index <= bytes.Length; index += 2)
        {
            bool atEnd = index + 1 >= bytes.Length;
            char value = atEnd ? '\0' : (char)(bytes[index] | (bytes[index + 1] << 8));
            // Executables contain many arbitrary byte pairs that happen to be valid
            // Unicode. Release secrets and Windows paths are ASCII-shaped, so keep
            // UTF-16LE extraction bounded to printable ASCII and avoid huge false
            // strings/memory growth from arbitrary binary data.
            bool printable = !atEnd && value >= 0x20 && value <= 0x7e;
            if (printable)
            {
                if (start < 0) start = index;
                count++;
                continue;
            }
            if (start >= 0 && count >= minimumCharacters)
            {
                string text = Encoding.Unicode.GetString(bytes, start, count * 2);
                AddChunks(result, start, 2, "UTF-16LE", text);
            }
            start = -1;
            count = 0;
        }
    }

    private static void AddChunks(
        List<TsgReleaseByteString> result,
        long byteOffset,
        int bytesPerCharacter,
        string encodingName,
        string text)
    {
        if (text.Length <= MaximumChunkCharacters)
        {
            result.Add(new TsgReleaseByteString {
                Offset = byteOffset,
                EncodingName = encodingName,
                Text = text
            });
            return;
        }

        int step = MaximumChunkCharacters - ChunkOverlapCharacters;
        for (int start = 0; start < text.Length; start += step)
        {
            int length = Math.Min(MaximumChunkCharacters, text.Length - start);
            result.Add(new TsgReleaseByteString {
                Offset = byteOffset + ((long)start * bytesPerCharacter),
                EncodingName = encodingName,
                Text = text.Substring(start, length)
            });
            if (start + length >= text.Length) break;
        }
    }
}

public static class TsgReleaseReparsePoint
{
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FsctlGetReparsePoint = 0x000900A8;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(
        string name,
        uint access,
        uint share,
        IntPtr security,
        uint creation,
        uint flags,
        IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        IntPtr device,
        uint controlCode,
        IntPtr input,
        uint inputSize,
        byte[] output,
        uint outputSize,
        out uint returned,
        IntPtr overlapped);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public static uint GetTag(string path)
    {
        IntPtr handle = CreateFile(
            path,
            0,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint | FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle == new IntPtr(-1))
            throw new IOException("Could not inspect a release input reparse point.");
        try
        {
            byte[] buffer = new byte[16384];
            uint returned;
            if (!DeviceIoControl(
                handle,
                FsctlGetReparsePoint,
                IntPtr.Zero,
                0,
                buffer,
                (uint)buffer.Length,
                out returned,
                IntPtr.Zero) || returned < 8)
                throw new IOException("Could not read a release input reparse tag.");
            return BitConverter.ToUInt32(buffer, 0);
        }
        finally { CloseHandle(handle); }
    }

    public static bool IsCloudPlaceholder(uint tag)
    {
        // Microsoft Cloud Files tags use 0x9000?01A. They virtualize the same
        // owned path and are not redirects; symlinks, junctions and every other
        // reparse tag remain rejected.
        return (tag & 0xFFFF0FFFu) == 0x9000001Au;
    }
}

public static class TsgReleaseAtomicFile
{
    private const uint MoveFileReplaceExisting = 0x00000001;
    private const uint MoveFileWriteThrough = 0x00000008;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(
        string existingName,
        string newName,
        uint flags);

    public static void Replace(string source, string destination)
    {
        if (!MoveFileEx(
            source,
            destination,
            MoveFileReplaceExisting | MoveFileWriteThrough))
            throw new IOException("Could not atomically publish the sanitized release report.");
    }
}
'@
}

function Get-Sha256Bytes([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return $sha.ComputeHash($Bytes) }
    finally { $sha.Dispose() }
}

function Get-Sha256Text([string]$Value) {
    $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes(
        $(if ($null -eq $Value) { '' } else { $Value }))
    $hex = [BitConverter]::ToString((Get-Sha256Bytes $bytes))
    return 'sha256:' + $hex.Replace('-', '').ToLowerInvariant()
}

function Get-FileSha256([string]$Path) {
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function Get-NormalizedDisplayPath([string]$Label, [string]$RelativePath) {
    $relative = $(if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        ''
    } else {
        $RelativePath.Replace('\', '/').TrimStart('/')
    })
    if ([string]::IsNullOrWhiteSpace($relative)) { return $Label }
    foreach ($rule in $script:contentRules) {
        if ($rule.Regex.IsMatch($relative)) {
            $pathFingerprint = (Get-Sha256Text $relative).Substring(7, 16)
            return $Label + '/[redacted-path-' + $pathFingerprint + ']'
        }
    }
    return $Label + '/' + $relative
}

function Get-ArchiveEntryDisplayPath(
    [string]$ArchiveDisplay,
    [string]$EntryName) {
    $normalized = $EntryName.Replace('\', '/')
    foreach ($rule in $script:contentRules) {
        if ($rule.Regex.IsMatch($normalized)) {
            $pathFingerprint = (Get-Sha256Text $normalized).Substring(7, 16)
            return $ArchiveDisplay + '::archive::/[redacted-path-' + $pathFingerprint + ']'
        }
    }
    return $ArchiveDisplay + '::archive::/' + $normalized
}

function Import-NarrowAllowlist([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw 'Release scan allowlist file was not found.'
    }
    $document = Get-Content -Raw -LiteralPath $fullPath -Encoding UTF8 | ConvertFrom-Json
    if ($null -eq $document -or [int]$document.schemaVersion -ne 1) {
        throw 'Release scan allowlist schema is invalid.'
    }
    foreach ($entry in @($document.entries)) {
        $file = [string]$entry.file
        $rule = [string]$entry.rule
        $fingerprint = [string]$entry.fingerprint
        $reason = [string]$entry.reason
        if ([string]::IsNullOrWhiteSpace($file) -or
            [string]::IsNullOrWhiteSpace($rule) -or
            $fingerprint -notmatch '^sha256:[0-9a-f]{64}$' -or
            [string]::IsNullOrWhiteSpace($reason) -or
            $reason.Length -lt 8 -or $reason.Length -gt 240 -or
            $file.IndexOfAny(@('*', '?')) -ge 0 -or
            $rule.IndexOfAny(@('*', '?')) -ge 0) {
            throw 'Release scan allowlist entries must use exact file, rule, fingerprint, and reason values.'
        }
        $key = $file.Replace('\', '/') + "`n" + $rule + "`n" + $fingerprint
        if ($script:allowlist.ContainsKey($key)) {
            throw 'Release scan allowlist contains a duplicate exact entry.'
        }
        $script:allowlist[$key] = $reason
    }
}

function Add-Finding(
    [string]$Rule,
    [string]$File,
    [string]$MatchedValue,
    [string]$Reason,
    [Nullable[int]]$Line,
    [Nullable[long]]$ByteOffset) {
    $fingerprint = Get-Sha256Text $MatchedValue
    $normalizedFile = $File.Replace('\', '/')
    $allowlistFiles = @($normalizedFile)
    # The archive delimiter contains ':'; safe Windows archive entry segments
    # reject ':', so a file name cannot forge this canonical source boundary.
    $sourceMarker = '::archive::/source/'
    $sourceMarkerIndex = $normalizedFile.LastIndexOf(
        $sourceMarker,
        [StringComparison]::Ordinal)
    if ($sourceMarkerIndex -ge 0) {
        $allowlistFiles += 'source/' + $normalizedFile.Substring(
            $sourceMarkerIndex + $sourceMarker.Length)
    }
    foreach ($allowlistFile in $allowlistFiles) {
        $allowlistKey = $allowlistFile + "`n" + $Rule + "`n" + $fingerprint
        if ($script:allowlist.ContainsKey($allowlistKey)) { return }
    }

    $finding = [ordered]@{
        rule = $Rule
        file = $normalizedFile
        fingerprint = $fingerprint
        reason = $Reason
    }
    if ($null -ne $Line) { $finding.line = [int]$Line }
    if ($null -ne $ByteOffset) {
        $finding.byteOffset = [long]$ByteOffset
    }
    [void]$script:findings.Add([pscustomobject]$finding)
}

$passwordWord = 'pass' + 'word'
$clientSecretWord = 'client' + '_secret'
$privateKeyMarker = '-----BEGIN ' + '(?:RSA |EC |OPENSSH )?' + 'PRIVATE KEY-----'
$script:contentRules = @(
    [pscustomobject]@{
        Id = 'WindowsUserPath'
        Regex = New-Object Text.RegularExpressions.Regex(
            '(?i)[A-Z]:[\\/]+Users[\\/]+[^\\/\s"''<>]{1,80}[\\/]+[^\r\n"''<>]{1,220}',
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        Group = 0
        Reason = 'A Windows user-scoped absolute path is present.'
    },
    [pscustomobject]@{
        Id = 'WindowsSid'
        Regex = New-Object Text.RegularExpressions.Regex(
            '(?<![A-Za-z0-9])S-1-5-21-(?:[0-9]{6,12}-){3}[0-9]{1,10}(?![0-9])',
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        Group = 0
        Reason = 'A Windows account SID is present.'
    },
    [pscustomobject]@{
        Id = 'AccountEmail'
        Regex = New-Object Text.RegularExpressions.Regex(
            '(?i)(?<![A-Z0-9._%+-])[A-Z0-9._%+-]{2,64}@[A-Z0-9.-]+\.[A-Z]{2,24}(?![A-Z0-9.-])',
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        Group = 0
        Reason = 'A likely personal account email is present.'
    },
    [pscustomobject]@{
        Id = 'GitHubToken'
        Regex = New-Object Text.RegularExpressions.Regex(
            '(?<![A-Za-z0-9_])(?:gh[pousr]_[A-Za-z0-9]{36,255}|github_pat_[A-Za-z0-9_]{40,255})(?![A-Za-z0-9_])',
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        Group = 0
        Reason = 'A GitHub access token shape is present.'
    },
    [pscustomobject]@{
        Id = 'ApiToken'
        Regex = New-Object Text.RegularExpressions.Regex(
            '(?<![A-Za-z0-9])(?:sk-[A-Za-z0-9_-]{20,}|AKIA[0-9A-Z]{16})(?![A-Za-z0-9])',
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        Group = 0
        Reason = 'A known API credential shape is present.'
    },
    [pscustomobject]@{
        Id = 'BearerToken'
        Regex = New-Object Text.RegularExpressions.Regex(
            '(?i)\bBearer\s+(?<secret>[A-Za-z0-9._~+/-]{24,}={0,2})',
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        Group = 'secret'
        Reason = 'A bearer credential is present.'
    },
    [pscustomobject]@{
        Id = 'JwtToken'
        Regex = New-Object Text.RegularExpressions.Regex(
            '(?<![A-Za-z0-9_-])eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}(?![A-Za-z0-9_-])',
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        Group = 0
        Reason = 'A JWT-shaped credential is present.'
    },
    [pscustomobject]@{
        Id = 'CredentialAssignment'
        Regex = New-Object Text.RegularExpressions.Regex(
            ('(?i)\b(?:' + [regex]::Escape($passwordWord) + '|passwd|pwd|' +
                [regex]::Escape($clientSecretWord) + '|api[_-]?key|access[_-]?token|' +
                'connection(?:string)?)\b\s*[:=]\s*["'']?(?<secret>[^\s"'';,]{8,})'),
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        Group = 'secret'
        Reason = 'A password, secret, API key, or connection credential assignment is present.'
    },
    [pscustomobject]@{
        Id = 'GameAccountIdentifier'
        Regex = New-Object Text.RegularExpressions.Regex(
            '(?i)\b(?:profile[_-]?id|account[_-]?id|user[_-]?id|aid|sid)\b\s*[:=]\s*["'']?(?<secret>[A-Za-z0-9_-]{8,64})',
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        Group = 'secret'
        Reason = 'A game profile or account identifier assignment is present.'
    },
    [pscustomobject]@{
        Id = 'PrivateKeyMarker'
        Regex = New-Object Text.RegularExpressions.Regex(
            $privateKeyMarker,
            [Text.RegularExpressions.RegexOptions]::CultureInvariant)
        Group = 0
        Reason = 'A private-key marker is present.'
    }
)

function Add-DynamicLiteralRule([string]$Id, [string]$Value, [string]$Reason) {
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.Length -lt 3) { return }
    $script:contentRules += [pscustomobject]@{
        Id = $Id
        Regex = New-Object Text.RegularExpressions.Regex(
            [regex]::Escape($Value),
            ([Text.RegularExpressions.RegexOptions]::IgnoreCase -bor
                [Text.RegularExpressions.RegexOptions]::CultureInvariant))
        Group = 0
        Reason = $Reason
    }
}

Add-DynamicLiteralRule 'KnownProjectPath' $ProjectRoot `
    'The exact local project path is present.'
Add-DynamicLiteralRule 'KnownUserProfile' ([Environment]::GetFolderPath(
    [Environment+SpecialFolder]::UserProfile)) 'The current Windows user profile path is present.'
Add-DynamicLiteralRule 'KnownUserName' ([Environment]::UserName) `
    'The current Windows account name is present.'
Add-DynamicLiteralRule 'KnownMachineName' ([Environment]::MachineName) `
    'The current computer name is present.'

function Test-TextSegment(
    [string]$Text,
    [string]$DisplayFile,
    [Nullable[int]]$LineNumber,
    [Nullable[long]]$BaseByteOffset,
    [int]$BytesPerCharacter) {
    if ([string]::IsNullOrEmpty($Text)) { return }
    foreach ($rule in $script:contentRules) {
        foreach ($match in $rule.Regex.Matches($Text)) {
            $group = $(if ($rule.Group -is [string]) {
                $match.Groups[[string]$rule.Group]
            } else {
                $match.Groups[[int]$rule.Group]
            })
            if ($null -eq $group -or -not $group.Success) { continue }
            $candidate = $group.Value
            $offset = $null
            if ($null -ne $BaseByteOffset) {
                $offset = [Nullable[long]]([long]$BaseByteOffset +
                    ([long]$group.Index * [long]$BytesPerCharacter))
            }
            Add-Finding $rule.Id $DisplayFile $candidate $rule.Reason `
                $LineNumber $offset
        }
    }
}

function Test-BannedFileName([string]$RelativePath, [string]$DisplayFile) {
    $normalized = $RelativePath.Replace('\', '/')
    $leaf = [IO.Path]::GetFileName($normalized)
    $extension = [IO.Path]::GetExtension($leaf).ToLowerInvariant()
    $lowerLeaf = $leaf.ToLowerInvariant()
    $bannedExtensions = @(
        '.log', '.mmdb', '.pfx', '.p12', '.pem', '.key', '.kdbx', '.sqlite', '.sqlite3')
    $bannedNames = @(
        '.env', 'id_rsa', 'id_ed25519', 'credentials.json', 'secrets.json',
        'blocked-server-metadata.json', 'update-check-state.json', 'settings.txt',
        'usage-guide.shown')
    $banned = $bannedExtensions -contains $extension
    $banned = $banned -or ($bannedNames -contains $lowerLeaf)
    $banned = $banned -or $lowerLeaf.StartsWith('.env.')
    $banned = $banned -or ($lowerLeaf.StartsWith(
        'tarkovserverguard-blocked-servers-') -and $extension -eq '.json')
    if ($banned) {
        Add-Finding 'BannedFileName' $DisplayFile $leaf `
            'A private-data, credential, log, database, or key file name is present.' $null $null
    }
    $bannedSegments = @(
        'raidnotes', 'userreportmemos', 'dbiplite', 'local-app-data', 'user-data',
        'runtime-data')
    foreach ($segment in @($normalized.Split('/') | Where-Object { $_.Length -gt 0 })) {
        if ($bannedSegments -contains $segment.ToLowerInvariant()) {
            Add-Finding 'BannedPathSegment' $DisplayFile $segment `
                'A runtime or private-user-data directory is present in release output.' $null $null
        }
    }
}

function Test-IsFileSystemLink([IO.FileSystemInfo]$Item) {
    if ($null -eq $Item) { return $false }
    $linkType = $Item.PSObject.Properties['LinkType']
    if ($null -ne $linkType -and
        -not [string]::IsNullOrWhiteSpace([string]$linkType.Value)) { return $true }
    if (($Item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) { return $false }
    try {
        $tag = [TsgReleaseReparsePoint]::GetTag($Item.FullName)
        return -not [TsgReleaseReparsePoint]::IsCloudPlaceholder($tag)
    }
    catch { return $true }
}

function Get-SafeDirectoryFiles([string]$Root, [string]$Label) {
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $rootItem = Get-Item -LiteralPath $rootPath -Force
    if (Test-IsFileSystemLink $rootItem) {
        Add-Finding 'FileSystemLink' $Label $Label `
            'Input directories cannot be links or reparse points.' $null $null
        return @()
    }
    $rootPrefix = $rootPath + [IO.Path]::DirectorySeparatorChar
    $pending = New-Object Collections.Generic.Stack[string]
    $pending.Push($rootPath)
    $files = New-Object Collections.ArrayList
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($item in @(Get-ChildItem -LiteralPath $directory -Force)) {
            $fullPath = [IO.Path]::GetFullPath($item.FullName)
            if (-not $fullPath.StartsWith(
                $rootPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
                Add-Finding 'PathBoundary' $Label $item.Name `
                    'An input path escaped its declared root.' $null $null
                continue
            }
            $relative = $fullPath.Substring($rootPrefix.Length)
            $display = Get-NormalizedDisplayPath $Label $relative
            Test-TextSegment $relative $display $null $null 1
            if (Test-IsFileSystemLink $item) {
                Add-Finding 'FileSystemLink' $display $relative `
                    'Input files and directories cannot be links or reparse points.' $null $null
                continue
            }
            if ($item.PSIsContainer) { $pending.Push($fullPath) }
            else {
                [void]$files.Add([pscustomobject]@{
                    FullPath = $fullPath
                    RelativePath = $relative
                    DisplayPath = $display
                    Length = [long]$item.Length
                })
            }
        }
    }
    return @($files | Sort-Object RelativePath)
}

function Get-DirectorySnapshot([string]$Root, [string]$Label) {
    $files = @(Get-SafeDirectoryFiles $Root $Label)
    $entries = New-Object Collections.ArrayList
    foreach ($file in $files) {
        [void]$entries.Add([pscustomobject]@{
            relativePath = $file.RelativePath.Replace('\', '/')
            length = $file.Length
            sha256 = Get-FileSha256 $file.FullPath
        })
    }
    $canonical = (($entries | ForEach-Object {
        $_.relativePath + "`t" + $_.length + "`t" + $_.sha256
    }) -join "`n")
    return [pscustomobject]@{
        Files = $files
        Entries = @($entries)
        Digest = Get-Sha256Text $canonical
    }
}

function Test-TextFile([string]$Path, [string]$DisplayFile) {
    $info = Get-Item -LiteralPath $Path
    if ($info.Length -gt $script:maximumTextFileBytes) {
        Add-Finding 'TextFileSizeLimit' $DisplayFile ([string]$info.Length) `
            'A text file exceeds the bounded scanner size.' $null $null
        return
    }
    $reader = New-Object IO.StreamReader(
        $Path,
        (New-Object Text.UTF8Encoding($false, $true)),
        $true,
        4096)
    try {
        $lineNumber = 0
        while ($true) {
            $line = $reader.ReadLine()
            if ($null -eq $line) { break }
            $lineNumber++
            Test-TextSegment $line $DisplayFile ([Nullable[int]]$lineNumber) $null 1
        }
    }
    finally { $reader.Dispose() }
}

function Test-BinaryFile([string]$Path, [string]$DisplayFile) {
    $info = Get-Item -LiteralPath $Path
    if ($info.Length -gt $script:maximumBinaryFileBytes) {
        Add-Finding 'BinaryFileSizeLimit' $DisplayFile ([string]$info.Length) `
            'A binary file exceeds the bounded string scanner size.' $null $null
        return
    }
    foreach ($segment in [TsgReleaseByteStrings]::Extract($Path, 6)) {
        $bytesPerCharacter = $(if ($segment.EncodingName -eq 'UTF-16LE') { 2 } else { 1 })
        Test-TextSegment $segment.Text $DisplayFile $null `
            ([Nullable[long]]$segment.Offset) $bytesPerCharacter
    }
}

function Test-RegularFile(
    [string]$Path,
    [string]$RelativePath,
    [string]$DisplayFile,
    [int]$ArchiveDepth) {
    Test-BannedFileName $RelativePath $DisplayFile
    $extension = [IO.Path]::GetExtension($Path).ToLowerInvariant()
    if ($extension -eq '.zip' -or $extension -eq '.nupkg') {
        Test-ArchiveFile $Path $DisplayFile ($ArchiveDepth + 1)
        return
    }
    $textExtensions = @(
        '.cs', '.ps1', '.psm1', '.psd1', '.md', '.txt', '.json', '.xml', '.config',
        '.yml', '.yaml', '.props', '.targets', '.cmd', '.bat', '.nuspec', '.license')
    if ($textExtensions -contains $extension -or [string]::IsNullOrEmpty($extension)) {
        Test-TextFile $Path $DisplayFile
    }
    else {
        Test-BinaryFile $Path $DisplayFile
    }
}

function Test-DirectoryContent(
    [string]$Root,
    [string]$Label,
    [int]$ArchiveDepth,
    $Snapshot) {
    $snapshotValue = $(if ($null -eq $Snapshot) {
        Get-DirectorySnapshot $Root $Label
    } else { $Snapshot })
    foreach ($file in $snapshotValue.Files) {
        Test-RegularFile $file.FullPath $file.RelativePath $file.DisplayPath $ArchiveDepth
    }
}

function Test-ArchiveEntryPath([string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Name) -or $Name.IndexOf([char]0) -ge 0) { return $false }
    $normalized = $Name.Replace('\', '/')
    if ($normalized.StartsWith('/') -or $normalized.StartsWith('\') -or
        $normalized -match '^[A-Za-z]:') { return $false }
    $parts = $normalized.Split('/')
    for ($index = 0; $index -lt $parts.Length; $index++) {
        $part = $parts[$index]
        if ($part.Length -eq 0) {
            if ($index -eq $parts.Length - 1) { continue }
            return $false
        }
        if ($part -eq '.' -or $part -eq '..' -or
            $part.EndsWith('.') -or $part.EndsWith(' ') -or
            $part.IndexOfAny([char[]]'<>:"|?*') -ge 0) { return $false }
        foreach ($character in $part.ToCharArray()) {
            if ([int]$character -lt 32) { return $false }
        }
        $deviceBase = $part.Split('.')[0]
        if ($deviceBase -match '^(?i:CON|PRN|AUX|NUL|CLOCK\$|COM[1-9]|LPT[1-9])$') {
            return $false
        }
    }
    return $true
}

function Test-ArchiveFile([string]$ArchivePath, [string]$DisplayFile, [int]$ArchiveDepth) {
    if ($ArchiveDepth -gt $MaximumNestedArchiveDepth) {
        Add-Finding 'ArchiveNestingLimit' $DisplayFile ([string]$ArchiveDepth) `
            'Nested archive depth exceeds the bounded limit.' $null $null
        return
    }
    if ($script:archiveCount -ge $MaximumArchives) {
        Add-Finding 'GlobalArchiveCountLimit' $DisplayFile ([string]($script:archiveCount + 1)) `
            'Total nested archive count exceeds the per-scan bounded limit.' $null $null
        return
    }
    $script:archiveCount++

    $beforeHash = Get-FileSha256 $ArchivePath
    $findingCountBefore = $script:findings.Count
    $extractRoot = Join-Path $script:temporaryRoot ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
    $extractPrefix = [IO.Path]::GetFullPath($extractRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

    Add-Type -AssemblyName System.IO.Compression
    $fileStream = $null
    $archive = $null
    try {
        $fileStream = [IO.File]::Open(
            $ArchivePath,
            [IO.FileMode]::Open,
            [IO.FileAccess]::Read,
            [IO.FileShare]::Read)
        $archive = New-Object IO.Compression.ZipArchive(
            $fileStream,
            [IO.Compression.ZipArchiveMode]::Read,
            $false)
        if ($archive.Entries.Count -gt $MaximumArchiveEntries) {
            Add-Finding 'ArchiveEntryCountLimit' $DisplayFile ([string]$archive.Entries.Count) `
                'Archive entry count exceeds the bounded limit.' $null $null
            return
        }
        if ($script:totalArchiveEntries -gt
                ($MaximumTotalArchiveEntries - $archive.Entries.Count)) {
            Add-Finding 'GlobalArchiveEntryCountLimit' $DisplayFile `
                ([string]($script:totalArchiveEntries + $archive.Entries.Count)) `
                'Total nested archive entry count exceeds the per-scan bounded limit.' $null $null
            return
        }
        $script:totalArchiveEntries += $archive.Entries.Count

        $seen = @{}
        $totalExpanded = [long]0
        foreach ($entry in $archive.Entries) {
            $entryName = [string]$entry.FullName
            $entryDisplay = Get-ArchiveEntryDisplayPath $DisplayFile $entryName
            if (-not (Test-ArchiveEntryPath $entryName)) {
                Add-Finding 'ArchiveUnsafePath' $entryDisplay $entryName `
                    'Archive entry paths cannot be rooted, drive-qualified, or contain parent traversal.' $null $null
                continue
            }
            $normalizedName = $entryName.Replace('\', '/').TrimEnd('/')
            if ([string]::IsNullOrWhiteSpace($normalizedName)) { continue }
            $depth = @($normalizedName.Split('/') | Where-Object { $_.Length -gt 0 }).Count
            if ($depth -gt $MaximumArchivePathDepth) {
                Add-Finding 'ArchivePathDepthLimit' $entryDisplay ([string]$depth) `
                    'Archive entry path depth exceeds the bounded limit.' $null $null
            }
            $key = $normalizedName.ToLowerInvariant()
            if ($seen.ContainsKey($key)) {
                Add-Finding 'ArchiveDuplicatePath' $entryDisplay $normalizedName `
                    'Archive contains duplicate case-insensitive output paths.' $null $null
            }
            else { $seen[$key] = $true }

            $externalAttributes = [BitConverter]::ToUInt32(
                [BitConverter]::GetBytes([int]$entry.ExternalAttributes),
                0)
            $unixMode = ($externalAttributes -shr 16) -band 0xF000
            $windowsAttributes = $externalAttributes -band 0xFFFF
            if ($unixMode -eq 0xA000 -or
                ($windowsAttributes -band [uint32][IO.FileAttributes]::ReparsePoint) -ne 0) {
                Add-Finding 'ArchiveLinkEntry' $entryDisplay $normalizedName `
                    'Archive link and reparse-point entries are not allowed.' $null $null
            }

            if ($entry.Length -gt $MaximumArchiveEntryBytes) {
                Add-Finding 'ArchiveEntrySizeLimit' $entryDisplay ([string]$entry.Length) `
                    'Archive entry expanded size exceeds the bounded limit.' $null $null
            }
            if ($entry.Length -gt 0 -and $entry.CompressedLength -eq 0) {
                Add-Finding 'ArchiveCompressionRatioLimit' $entryDisplay 'infinite' `
                    'Archive entry compression ratio is unsafe.' $null $null
            }
            elseif ($entry.CompressedLength -gt 0 -and
                ([double]$entry.Length / [double]$entry.CompressedLength) -gt
                    $MaximumCompressionRatio) {
                Add-Finding 'ArchiveCompressionRatioLimit' $entryDisplay `
                    ([string]([math]::Round(
                        ([double]$entry.Length / [double]$entry.CompressedLength), 2))) `
                    'Archive entry compression ratio exceeds the bounded limit.' $null $null
            }
            if ([long]::MaxValue - $totalExpanded -lt $entry.Length) {
                Add-Finding 'ArchiveExpandedSizeLimit' $DisplayFile 'overflow' `
                    'Archive expanded size overflowed its bounded counter.' $null $null
            }
            else { $totalExpanded += [long]$entry.Length }
            Test-BannedFileName $normalizedName $entryDisplay
        }

        if ($totalExpanded -gt $MaximumArchiveExpandedBytes -or
            [long]::MaxValue - $script:archiveExpandedBytes -lt $totalExpanded -or
            $script:archiveExpandedBytes + $totalExpanded -gt $MaximumArchiveExpandedBytes) {
            Add-Finding 'ArchiveExpandedSizeLimit' $DisplayFile ([string]$totalExpanded) `
                'Archive expanded size exceeds the per-scan bounded limit.' $null $null
        }

        if ($script:findings.Count -eq $findingCountBefore) {
            $script:archiveExpandedBytes += $totalExpanded
            $buffer = New-Object byte[] 65536
            foreach ($entry in $archive.Entries) {
                $normalizedName = $entry.FullName.Replace('\', '/').TrimEnd('/')
                if ([string]::IsNullOrWhiteSpace($normalizedName)) { continue }
                $targetPath = [IO.Path]::GetFullPath((Join-Path $extractRoot $normalizedName))
                if (-not $targetPath.StartsWith(
                    $extractPrefix,
                    [StringComparison]::OrdinalIgnoreCase)) {
                    Add-Finding 'ArchiveUnsafePath' `
                        (Get-ArchiveEntryDisplayPath $DisplayFile $normalizedName) `
                        $normalizedName 'Archive extraction target escaped its dedicated root.' $null $null
                    break
                }
                if ($entry.FullName.EndsWith('/') -or $entry.FullName.EndsWith('\')) {
                    New-Item -ItemType Directory -Force -Path $targetPath | Out-Null
                    continue
                }
                $targetParent = Split-Path -Parent $targetPath
                New-Item -ItemType Directory -Force -Path $targetParent | Out-Null
                $entryStream = $null
                $targetStream = $null
                try {
                    $entryStream = $entry.Open()
                    $targetStream = New-Object IO.FileStream(
                        $targetPath,
                        [IO.FileMode]::CreateNew,
                        [IO.FileAccess]::Write,
                        [IO.FileShare]::None)
                    $written = [long]0
                    while (($read = $entryStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                        $written += $read
                        if ($written -gt $entry.Length -or
                            $written -gt $MaximumArchiveEntryBytes) {
                            throw 'Archive entry exceeded its declared bounded size.'
                        }
                        $targetStream.Write($buffer, 0, $read)
                    }
                    if ($written -ne $entry.Length) {
                        throw 'Archive entry size did not match its declared value.'
                    }
                }
                finally {
                    if ($null -ne $targetStream) { $targetStream.Dispose() }
                    if ($null -ne $entryStream) { $entryStream.Dispose() }
                }
            }

            if ($script:findings.Count -eq $findingCountBefore) {
                $nestedLabel = $DisplayFile + '::archive::'
                Test-DirectoryContent $extractRoot $nestedLabel $ArchiveDepth $null
            }
        }
    }
    catch {
        Add-Finding 'ArchiveReadFailure' $DisplayFile $beforeHash `
            'Archive metadata or bounded extraction could not be validated.' $null $null
    }
    finally {
        if ($null -ne $archive) { $archive.Dispose() }
        if ($null -ne $fileStream) { $fileStream.Dispose() }
        $afterHash = Get-FileSha256 $ArchivePath
        if (-not [string]::Equals($beforeHash, $afterHash, [StringComparison]::Ordinal)) {
            Add-Finding 'InputChanged' $DisplayFile ($beforeHash + ':' + $afterHash) `
                'The archive changed while it was being scanned.' $null $null
        }
    }
}

function Resolve-InputMode([string]$Path, [string]$RequestedMode) {
    if ($RequestedMode -ne 'Auto') { return $RequestedMode }
    if (Test-Path -LiteralPath $Path -PathType Container) { return 'Directory' }
    $extension = [IO.Path]::GetExtension($Path).ToLowerInvariant()
    if ($extension -eq '.zip' -or $extension -eq '.nupkg') { return 'Archive' }
    if ([IO.Path]::GetFileName($Path) -match '(?i)Setup.*\.exe$') { return 'Setup' }
    return 'File'
}

function Get-ModeAt([int]$Index) {
    if ($InputMode.Count -eq 1) { return $InputMode[0] }
    return $InputMode[$Index]
}

function Assert-ReportOutsideInputs {
    if ([string]::IsNullOrWhiteSpace($ReportPath)) {
        $script:reportPathValidated = $true
        return
    }
    $reportFullPath = [IO.Path]::GetFullPath($ReportPath)
    if (-not [string]::IsNullOrWhiteSpace($AllowlistPath) -and
        [string]::Equals(
            $reportFullPath,
            [IO.Path]::GetFullPath($AllowlistPath),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release scan report cannot overwrite the exact allowlist input.'
    }
    for ($index = 0; $index -lt $InputPath.Count; $index++) {
        $inputFullPath = [IO.Path]::GetFullPath($InputPath[$index])
        if ([string]::Equals(
            $reportFullPath,
            $inputFullPath,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Release scan report must be outside every scanned input.'
        }
        $requestedMode = Get-ModeAt $index
        $isDirectory = $requestedMode -eq 'Directory' -or
            ($requestedMode -eq 'Auto' -and
                (Test-Path -LiteralPath $inputFullPath -PathType Container))
        if ($isDirectory) {
            $inputPrefix = $inputFullPath
            if (-not $inputPrefix.EndsWith([string][IO.Path]::DirectorySeparatorChar) -and
                -not $inputPrefix.EndsWith([string][IO.Path]::AltDirectorySeparatorChar)) {
                $inputPrefix += [IO.Path]::DirectorySeparatorChar
            }
            if ($reportFullPath.StartsWith(
                $inputPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Release scan report must be outside every scanned input.'
            }
        }
    }
    $script:reportPathValidated = $true
}

function Write-ScanReport([object[]]$Inputs, [bool]$Success) {
    if ([string]::IsNullOrWhiteSpace($ReportPath) -or
        -not $script:reportPathValidated) { return }
    $reportFullPath = [IO.Path]::GetFullPath($ReportPath)
    $reportParent = Split-Path -Parent $reportFullPath
    if (-not [string]::IsNullOrWhiteSpace($reportParent)) {
        New-Item -ItemType Directory -Force -Path $reportParent | Out-Null
    }
    $document = [ordered]@{
        schemaVersion = 1
        success = $Success
        inputs = $Inputs
        findings = @($script:findings)
    }
    $json = $document | ConvertTo-Json -Depth 8
    $temporaryReportPath = $reportFullPath + '.tmp.' + [Guid]::NewGuid().ToString('N')
    try {
        [IO.File]::WriteAllText(
            $temporaryReportPath,
            $json,
            (New-Object Text.UTF8Encoding($false)))
        [TsgReleaseAtomicFile]::Replace($temporaryReportPath, $reportFullPath)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryReportPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryReportPath -Force
        }
    }
}

$reportInputs = New-Object Collections.ArrayList
try {
    if ($InputPath.Count -eq 0) { throw 'At least one release scan input is required.' }
    if ($InputMode.Count -ne 1 -and $InputMode.Count -ne $InputPath.Count) {
        throw 'InputMode must contain one value or one value per input.'
    }
    if ($null -eq $InputLabel -or $InputLabel.Count -eq 0) {
        $InputLabel = @(for ($index = 0; $index -lt $InputPath.Count; $index++) {
            'input' + ($index + 1)
        })
    }
    if ($InputLabel.Count -ne $InputPath.Count) {
        throw 'InputLabel must contain one safe label per input.'
    }
    foreach ($label in $InputLabel) {
        if ($label -notmatch '^[A-Za-z0-9._-]{1,64}$') {
            throw 'Input labels may only use 1-64 ASCII letters, digits, dot, underscore, or dash.'
        }
    }
    Assert-ReportOutsideInputs
    Import-NarrowAllowlist $AllowlistPath
    New-Item -ItemType Directory -Force -Path $script:temporaryRoot | Out-Null

    for ($index = 0; $index -lt $InputPath.Count; $index++) {
        $path = [IO.Path]::GetFullPath($InputPath[$index])
        $label = $InputLabel[$index]
        if (-not (Test-Path -LiteralPath $path)) {
            Add-Finding 'MissingInput' $label $label `
                'A declared release scan input does not exist.' $null $null
            continue
        }
        $mode = Resolve-InputMode $path (Get-ModeAt $index)
        if ($mode -eq 'Directory') {
            $before = Get-DirectorySnapshot $path $label
            Test-DirectoryContent $path $label 0 $before
            $after = Get-DirectorySnapshot $path $label
            if (-not [string]::Equals(
                $before.Digest,
                $after.Digest,
                [StringComparison]::Ordinal)) {
                Add-Finding 'InputChanged' $label ($before.Digest + ':' + $after.Digest) `
                    'The input directory changed while it was being scanned.' $null $null
            }
            [void]$reportInputs.Add([pscustomobject]@{
                label = $label
                mode = $mode
                beforeFingerprint = $before.Digest
                afterFingerprint = $after.Digest
            })
        }
        else {
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                Add-Finding 'InputType' $label $label `
                    'A file scan mode received a non-file input.' $null $null
                continue
            }
            $beforeHash = Get-FileSha256 $path
            if ($mode -eq 'Archive') {
                Test-ArchiveFile $path $label 0
            }
            elseif ($mode -eq 'Setup') {
                Test-BinaryFile $path $label
            }
            else {
                Test-RegularFile $path ([IO.Path]::GetFileName($path)) $label 0
            }
            $afterHash = Get-FileSha256 $path
            if (-not [string]::Equals($beforeHash, $afterHash, [StringComparison]::Ordinal)) {
                Add-Finding 'InputChanged' $label ($beforeHash + ':' + $afterHash) `
                    'The input file changed while it was being scanned.' $null $null
            }
            [void]$reportInputs.Add([pscustomobject]@{
                label = $label
                mode = $mode
                beforeFingerprint = 'sha256:' + $beforeHash
                afterFingerprint = 'sha256:' + $afterHash
            })
        }
    }

    $success = $script:findings.Count -eq 0
    Write-ScanReport @($reportInputs) $success
    if (-not $success) {
        foreach ($finding in $script:findings) {
            $location = $(if ($null -ne $finding.PSObject.Properties['line']) {
                ':line ' + $finding.line
            } elseif ($null -ne $finding.PSObject.Properties['byteOffset']) {
                ':byte ' + $finding.byteOffset
            } else { '' })
            Write-Host ('FAIL [' + $finding.rule + '] ' + $finding.file + $location +
                ' ' + $finding.fingerprint + ' - ' + $finding.reason)
        }
        $script:scanFailed = $true
    }
    else {
        Write-Host ('Release sanitization passed: ' + ($InputLabel -join ', '))
    }
}
catch {
    Add-Finding 'ScannerFailure' 'scanner' 'internal-failure' `
        'The release scanner could not complete safely.' $null $null
    Write-ScanReport @($reportInputs) $false
    $script:scanFailed = $true
}
finally {
    try {
        if (Test-Path -LiteralPath $script:temporaryRoot) {
            $resolvedTemporary = [IO.Path]::GetFullPath($script:temporaryRoot)
            $systemTemporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd(
                [IO.Path]::DirectorySeparatorChar,
                [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
            if (-not $resolvedTemporary.StartsWith(
                $systemTemporary,
                [StringComparison]::OrdinalIgnoreCase) -or
                [IO.Path]::GetFileName($resolvedTemporary) -notmatch '^TSG-ReleaseScan-[0-9a-f]{32}$') {
                throw 'Release scanner refused an unexpected temporary cleanup path.'
            }
            Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
        }
    }
    catch {
        Add-Finding 'ScannerFailure' 'scanner' 'cleanup-failure' `
            'The release scanner could not clean its isolated temporary directory.' $null $null
        Write-ScanReport @($reportInputs) $false
        $script:scanFailed = $true
    }
}

if ($script:scanFailed) {
    [Console]::Error.WriteLine(
        'Release sanitization failed. Only sanitized fingerprints were reported.')
    if ($ReturnStatus) {
        $global:LASTEXITCODE = 1
        return
    }
    exit 1
}
if ($ReturnStatus) { $global:LASTEXITCODE = 0 }
