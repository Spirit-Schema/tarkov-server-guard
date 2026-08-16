# Copyright © 2026 Spirit-Schema. All rights reserved.
# Licensed under the Tarkov Server Guard Source-Available Freeware License 1.0. See LICENSE.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ExpectedManifest,
    [string]$CompletionMarker,
    [string]$ReportPath,
    [string]$FixtureReleaseJson,
    [string]$FixtureAssetDirectory,
    [string]$TempRoot
)

$ErrorActionPreference = 'Stop'
$repository = 'Spirit-Schema/tarkov-server-guard'
$apiOrigin = 'https://api.github.com'
$githubOrigin = 'https://github.com'
$maximumManifestBytes = 1MB
$maximumApiBytes = 2MB
$maximumAssetCount = 32
$maximumApiAssetCount = 64
$maximumAssetBytes = 1GB
$maximumTotalBytes = 4GB
$maximumRedirects = 5
$script:assetDownloadCount = 0
$script:httpDownloadRequestCount = 0
$script:completedDownloadCount = 0
$script:downloadedBytes = [long]0

function Read-StrictUtf8File([string]$Path, [long]$MaximumBytes, [string]$FailureCode) {
    try {
        $info = Get-Item -LiteralPath $Path -ErrorAction Stop
        if ($info.Length -lt 2 -or $info.Length -gt $MaximumBytes) {
            throw $FailureCode
        }
        $bytes = [IO.File]::ReadAllBytes($info.FullName)
        $encoding = New-Object System.Text.UTF8Encoding($false, $true)
        $text = $encoding.GetString($bytes)
        if ($text.Length -gt 0 -and $text[0] -eq [char]0xfeff) {
            $text = $text.Substring(1)
        }
        return $text
    }
    catch {
        if ($_.Exception.Message -ceq $FailureCode) { throw }
        throw $FailureCode
    }
}

function ConvertFrom-StrictJson([string]$Text, [string]$FailureCode) {
    try {
        $value = $Text | ConvertFrom-Json -ErrorAction Stop
        if ($null -eq $value) { throw $FailureCode }
        return $value
    }
    catch {
        throw $FailureCode
    }
}

function Test-ExactProperties([object]$Value, [string[]]$Expected) {
    if ($null -eq $Value) { return $false }
    $actual = @($Value.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object)
    $wanted = @($Expected | Sort-Object)
    return $actual.Count -eq $wanted.Count -and
        [string]::Join("`n", $actual) -ceq [string]::Join("`n", $wanted)
}

function Get-RoleForName([string]$Name, [string]$Version) {
    $escapedVersion = [Regex]::Escape($Version)
    if ($Name -ceq 'SpiritSchema.TarkovServerGuard-win-Setup.exe') {
        return 'windows-setup'
    }
    if ($Name -ceq 'SpiritSchema.TarkovServerGuard-win-Portable.zip') {
        return 'windows-portable'
    }
    if ($Name -cmatch ('^SpiritSchema\.TarkovServerGuard-' + $escapedVersion + '-full\.nupkg$')) {
        return 'full-nupkg'
    }
    if ($Name -cmatch ('^SpiritSchema\.TarkovServerGuard-' + $escapedVersion + '-delta\.nupkg$')) {
        return 'delta-nupkg'
    }
    if ($Name -ceq 'releases.win.json') { return 'release-feed' }
    if ($Name -ceq 'RELEASES') { return 'legacy-release-feed' }
    if ($Name -ceq 'SHA256SUMS.txt') { return 'release-sha256sums' }
    if ($Name -ceq 'binary-build-identity.json') { return 'binary-build-identity' }
    if ($Name -ceq 'build-inputs.manifest') { return 'binary-build-input-manifest' }
    if ($Name -ceq 'release-assets.manifest') { return 'release-asset-provenance-manifest' }
    if ($Name -ceq 'release-provenance.json') { return 'release-provenance' }
    if ($Name -ceq ('TarkovServerGuard-v' + $Version + '.zip')) { return 'review-zip' }
    if ($Name -ceq ('TarkovServerGuard-v' + $Version + '.zip.sha256.txt')) {
        return 'review-zip-sha256'
    }
    return $null
}

function Read-ExpectedManifest([string]$Path) {
    $text = Read-StrictUtf8File $Path $maximumManifestBytes 'expected-manifest-read-failed'
    $document = ConvertFrom-StrictJson $text 'expected-manifest-json-invalid'
    if (-not (Test-ExactProperties $document @(
        'schemaVersion', 'repository', 'version', 'tag', 'assets'))) {
        throw 'expected-manifest-schema-invalid'
    }
    if ([int]$document.schemaVersion -ne 1 -or
        [string]$document.repository -cne $repository -or
        [string]$document.version -notmatch '^\d+\.\d+\.\d+$' -or
        [string]$document.tag -cne ('v' + [string]$document.version)) {
        throw 'expected-manifest-identity-invalid'
    }

    $assets = @($document.assets)
    if ($assets.Count -lt 1 -or $assets.Count -gt $maximumAssetCount) {
        throw 'expected-manifest-asset-count-invalid'
    }
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $total = [long]0
    $parsed = New-Object 'System.Collections.Generic.List[object]'
    foreach ($asset in $assets) {
        if (-not (Test-ExactProperties $asset @('name', 'role', 'size', 'sha256'))) {
            throw 'expected-manifest-asset-schema-invalid'
        }
        $name = [string]$asset.name
        $role = [string]$asset.role
        $size = [long]$asset.size
        $sha256 = ([string]$asset.sha256).ToUpperInvariant()
        if ([string]::IsNullOrWhiteSpace($name) -or
            $name.Length -gt 200 -or
            [IO.Path]::GetFileName($name) -cne $name -or
            $name.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
            -not $seen.Add($name)) {
            throw 'expected-manifest-asset-name-invalid'
        }
        $expectedRole = Get-RoleForName $name ([string]$document.version)
        if ($null -eq $expectedRole -or $role -cne $expectedRole) {
            throw 'expected-manifest-asset-role-invalid'
        }
        if ($size -lt 1 -or $size -gt $maximumAssetBytes -or
            $sha256 -notmatch '^[0-9A-F]{64}$') {
            throw 'expected-manifest-asset-value-invalid'
        }
        $total += $size
        if ($total -gt $maximumTotalBytes) {
            throw 'expected-manifest-total-size-invalid'
        }
        $parsed.Add([PSCustomObject]@{
            Name = $name
            Role = $role
            Size = $size
            Sha256 = $sha256
        })
    }

    foreach ($requiredRole in @(
        'windows-setup',
        'windows-portable',
        'full-nupkg',
        'release-feed',
        'release-sha256sums',
        'binary-build-identity',
        'binary-build-input-manifest',
        'release-asset-provenance-manifest',
        'release-provenance',
        'review-zip',
        'review-zip-sha256')) {
        if (@($parsed | Where-Object { $_.Role -ceq $requiredRole }).Count -ne 1) {
            throw 'expected-manifest-required-role-invalid'
        }
    }
    return [PSCustomObject]@{
        Version = [string]$document.version
        Tag = [string]$document.tag
        Assets = @($parsed | Sort-Object Name)
        TotalBytes = $total
    }
}

function Read-CompletionMarker([string]$Path, [string]$ManifestPath) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw 'completion-marker-required'
    }
    $fullPath = [IO.Path]::GetFullPath($Path)
    $manifestFullPath = [IO.Path]::GetFullPath($ManifestPath)
    if ([string]::Equals(
        $fullPath,
        $manifestFullPath,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'completion-marker-path-invalid'
    }
    $text = Read-StrictUtf8File $fullPath 64KB 'completion-marker-read-failed'
    $document = ConvertFrom-StrictJson $text 'completion-marker-json-invalid'
    if (-not (Test-ExactProperties $document @(
        'schemaVersion',
        'repository',
        'version',
        'tag',
        'expectedManifestFile',
        'expectedManifestSha256'))) {
        throw 'completion-marker-schema-invalid'
    }
    $version = [string]$document.version
    $expectedManifestName = 'release-assets-v' + $version + '.expected.json'
    $expectedMarkerName = 'release-assets-v' + $version + '.ready.json'
    $expectedHash = ([string]$document.expectedManifestSha256).ToUpperInvariant()
    if ([int]$document.schemaVersion -ne 1 -or
        [string]$document.repository -cne $repository -or
        $version -notmatch '^\d+\.\d+\.\d+$' -or
        [string]$document.tag -cne ('v' + $version) -or
        [string]$document.expectedManifestFile -cne $expectedManifestName -or
        [IO.Path]::GetFileName($manifestFullPath) -cne $expectedManifestName -or
        [IO.Path]::GetFileName($fullPath) -cne $expectedMarkerName -or
        $expectedHash -notmatch '^[0-9A-F]{64}$') {
        throw 'completion-marker-identity-invalid'
    }
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $manifestFullPath).Hash.ToUpperInvariant()
    if ($actualHash -cne $expectedHash) {
        throw 'completion-marker-manifest-hash-mismatch'
    }
    return [PSCustomObject]@{
        Version = $version
        Tag = [string]$document.tag
        ExpectedManifestSha256 = $expectedHash
    }
}

function New-HttpClient {
    Add-Type -AssemblyName System.Net.Http
    [Net.ServicePointManager]::SecurityProtocol =
        [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $handler = New-Object Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $false
    $handler.AutomaticDecompression = [Net.DecompressionMethods]::GZip -bor
        [Net.DecompressionMethods]::Deflate
    $handler.UseDefaultCredentials = $false
    $client = New-Object Net.Http.HttpClient($handler, $true)
    $client.Timeout = [TimeSpan]::FromMinutes(5)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd('TarkovServerGuard-PublicReleaseVerifier/1.0')
    $null = $client.DefaultRequestHeaders.TryAddWithoutValidation(
        'X-GitHub-Api-Version',
        '2022-11-28')
    return $client
}

function Read-HttpContentBounded(
    [object]$Response,
    [long]$MaximumBytes,
    [string]$FailureCode) {
    $contentLength = $Response.Content.Headers.ContentLength
    if ($null -ne $contentLength -and [long]$contentLength -gt $MaximumBytes) {
        throw $FailureCode
    }
    $stream = $Response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
    try {
        $memory = New-Object IO.MemoryStream
        try {
            $buffer = New-Object byte[] 65536
            $total = [long]0
            while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $total += $read
                if ($total -gt $MaximumBytes) { throw $FailureCode }
                $memory.Write($buffer, 0, $read)
            }
            return $memory.ToArray()
        }
        finally {
            $memory.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-LiveReleaseJson([object]$Client, [string]$Tag) {
    $escapedTag = [Uri]::EscapeDataString($Tag)
    $uri = [Uri]($apiOrigin + '/repos/Spirit-Schema/tarkov-server-guard/releases/tags/' + $escapedTag)
    $request = New-Object Net.Http.HttpRequestMessage([Net.Http.HttpMethod]::Get, $uri)
    $null = $request.Headers.TryAddWithoutValidation('Accept', 'application/vnd.github+json')
    try {
        $response = $Client.SendAsync(
            $request,
            [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        try {
            if ([int]$response.StatusCode -ne 200) {
                throw ('release-api-http-' + [int]$response.StatusCode)
            }
            $bytes = Read-HttpContentBounded $response $maximumApiBytes 'release-api-response-too-large'
            $encoding = New-Object System.Text.UTF8Encoding($false, $true)
            try { return $encoding.GetString($bytes) }
            catch { throw 'release-api-utf8-invalid' }
        }
        finally {
            $response.Dispose()
        }
    }
    catch {
        $message = [string]$_.Exception.Message
        if ($message -match '^release-api-[a-z0-9-]+$') { throw }
        throw 'release-api-request-failed'
    }
    finally {
        $request.Dispose()
    }
}

function Test-BrowserDownloadBinding([string]$Url, [string]$Tag, [string]$Name) {
    try {
        $uri = [Uri]$Url
        if (-not $uri.IsAbsoluteUri -or
            $uri.Scheme -cne 'https' -or
            -not [string]::Equals($uri.Host, 'github.com', [StringComparison]::OrdinalIgnoreCase) -or
            -not $uri.IsDefaultPort -or
            -not [string]::IsNullOrEmpty($uri.UserInfo) -or
            -not [string]::IsNullOrEmpty($uri.Query) -or
            -not [string]::IsNullOrEmpty($uri.Fragment)) {
            return $false
        }
        $segments = @($uri.AbsolutePath.Trim('/').Split('/'))
        if ($segments.Count -ne 6 -or
            [Uri]::UnescapeDataString($segments[0]) -cne 'Spirit-Schema' -or
            [Uri]::UnescapeDataString($segments[1]) -cne 'tarkov-server-guard' -or
            $segments[2] -cne 'releases' -or
            $segments[3] -cne 'download' -or
            [Uri]::UnescapeDataString($segments[4]) -cne $Tag -or
            [Uri]::UnescapeDataString($segments[5]) -cne $Name) {
            return $false
        }
        return $true
    }
    catch {
        return $false
    }
}

function Test-AllowedDownloadUri([Uri]$Uri, [bool]$Initial) {
    if ($null -eq $Uri -or -not $Uri.IsAbsoluteUri -or
        $Uri.Scheme -cne 'https' -or -not $Uri.IsDefaultPort -or
        -not [string]::IsNullOrEmpty($Uri.UserInfo)) {
        return $false
    }
    if ($Initial) {
        return [string]::Equals($Uri.Host, 'github.com', [StringComparison]::OrdinalIgnoreCase)
    }
    foreach ($hostName in @(
        'github.com',
        'objects.githubusercontent.com',
        'release-assets.githubusercontent.com',
        'github-releases.githubusercontent.com')) {
        if ([string]::Equals($Uri.Host, $hostName, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $Uri.Host -match '^github-production-release-asset-[0-9a-z-]+\.s3\.amazonaws\.com$'
}

function Get-SanitizedFinalUrl([Uri]$Uri) {
    if ($null -eq $Uri) { return $null }
    return $Uri.GetLeftPart([UriPartial]::Path)
}

function Copy-StreamToBoundedFile(
    [IO.Stream]$InputStream,
    [string]$Destination,
    [long]$MaximumBytes) {
    $output = New-Object IO.FileStream(
        $Destination,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None,
        65536,
        [IO.FileOptions]::SequentialScan)
    try {
        $buffer = New-Object byte[] 65536
        $count = [long]0
        while ($true) {
            $assetAllowance = $MaximumBytes - $count + 1
            $totalAllowance = $maximumTotalBytes - $script:downloadedBytes + 1
            if ($assetAllowance -lt 1 -or $totalAllowance -lt 1) {
                throw 'asset-download-size-limit'
            }
            $readCapacity = [long]$buffer.Length
            if ($assetAllowance -lt $readCapacity) { $readCapacity = $assetAllowance }
            if ($totalAllowance -lt $readCapacity) { $readCapacity = $totalAllowance }
            $read = $InputStream.Read($buffer, 0, [int]$readCapacity)
            if ($read -le 0) { break }
            $count += $read
            $script:downloadedBytes += [long]$read
            if ($count -gt $MaximumBytes -or $script:downloadedBytes -gt $maximumTotalBytes) {
                throw 'asset-download-size-limit'
            }
            $output.Write($buffer, 0, $read)
        }
        $output.Flush($true)
        return $count
    }
    finally {
        $output.Dispose()
    }
}

function Copy-FixtureAsset(
    [string]$FixtureDirectory,
    [string]$Name,
    [string]$Destination,
    [long]$ExpectedSize) {
    $root = [IO.Path]::GetFullPath($FixtureDirectory).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $prefix = $root + [IO.Path]::DirectorySeparatorChar
    $source = [IO.Path]::GetFullPath((Join-Path $root $Name))
    if (-not $source.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($source) -cne $Name -or
        -not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw 'fixture-asset-missing'
    }
    $stream = New-Object IO.FileStream(
        $source,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read)
    try {
        return Copy-StreamToBoundedFile $stream $Destination $ExpectedSize
    }
    finally {
        $stream.Dispose()
    }
}

function Invoke-AssetDownload(
    [object]$Client,
    [string]$Url,
    [string]$Tag,
    [string]$Name,
    [string]$Destination,
    [long]$ExpectedSize,
    [bool]$FixtureMode,
    [string]$FixtureDirectory,
    [object[]]$FixtureRedirectChain) {
    $script:assetDownloadCount++
    $current = [Uri]$Url
    if (-not (Test-AllowedDownloadUri $current $true)) {
        throw 'asset-download-initial-url-rejected'
    }
    if ($FixtureMode) {
        $redirects = @($FixtureRedirectChain)
        if ($redirects.Count -gt $maximumRedirects) {
            throw 'asset-download-redirect-limit'
        }
        foreach ($redirectValue in $redirects) {
            try { $next = [Uri]([string]$redirectValue) }
            catch { throw 'asset-download-redirect-host-rejected' }
            if (-not (Test-AllowedDownloadUri $next $false)) {
                throw 'asset-download-redirect-host-rejected'
            }
            if ([string]::Equals(
                    $next.Host,
                    'github.com',
                    [StringComparison]::OrdinalIgnoreCase) -and
                -not (Test-BrowserDownloadBinding $next.AbsoluteUri $Tag $Name)) {
                throw 'asset-download-redirect-binding-rejected'
            }
            $current = $next
        }
        $size = Copy-FixtureAsset $FixtureDirectory $Name $Destination $ExpectedSize
        $script:completedDownloadCount++
        return [PSCustomObject]@{
            Size = $size
            RedirectCount = $redirects.Count
            FinalUrl = Get-SanitizedFinalUrl $current
        }
    }

    for ($redirectCount = 0; $redirectCount -le $maximumRedirects; $redirectCount++) {
        $request = New-Object Net.Http.HttpRequestMessage([Net.Http.HttpMethod]::Get, $current)
        $null = $request.Headers.TryAddWithoutValidation('Accept', 'application/octet-stream')
        try {
            $script:httpDownloadRequestCount++
            $response = $Client.SendAsync(
                $request,
                [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
            try {
                $status = [int]$response.StatusCode
                if ($status -in @(301, 302, 303, 307, 308)) {
                    if ($redirectCount -ge $maximumRedirects -or $null -eq $response.Headers.Location) {
                        throw 'asset-download-redirect-limit'
                    }
                    $next = if ($response.Headers.Location.IsAbsoluteUri) {
                        $response.Headers.Location
                    }
                    else {
                        New-Object Uri($current, $response.Headers.Location)
                    }
                    if (-not (Test-AllowedDownloadUri $next $false)) {
                        throw 'asset-download-redirect-host-rejected'
                    }
                    if ([string]::Equals(
                            $next.Host,
                            'github.com',
                            [StringComparison]::OrdinalIgnoreCase) -and
                        -not (Test-BrowserDownloadBinding $next.AbsoluteUri $Tag $Name)) {
                        throw 'asset-download-redirect-binding-rejected'
                    }
                    $current = $next
                    continue
                }
                if ($status -ne 200) { throw ('asset-download-http-' + $status) }
                $contentLength = $response.Content.Headers.ContentLength
                if ($null -ne $contentLength -and [long]$contentLength -gt $ExpectedSize) {
                    throw 'asset-download-size-limit'
                }
                $stream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                try {
                    $size = Copy-StreamToBoundedFile $stream $Destination $ExpectedSize
                }
                finally {
                    $stream.Dispose()
                }
                $script:completedDownloadCount++
                return [PSCustomObject]@{
                    Size = $size
                    RedirectCount = $redirectCount
                    FinalUrl = Get-SanitizedFinalUrl $current
                }
            }
            finally {
                $response.Dispose()
            }
        }
        catch {
            $message = [string]$_.Exception.Message
            if ($message -match '^asset-download-[a-z0-9-]+$') { throw }
            throw 'asset-download-request-failed'
        }
        finally {
            $request.Dispose()
        }
    }
    throw 'asset-download-redirect-limit'
}

function New-VerificationTemp([string]$ParentPath) {
    $parent = if ([string]::IsNullOrWhiteSpace($ParentPath)) {
        [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    }
    else {
        [IO.Path]::GetFullPath($ParentPath)
    }
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        throw 'temporary-parent-invalid'
    }
    $nonce = [Guid]::NewGuid().ToString('N')
    $path = Join-Path $parent ('TarkovServerGuard-ReleaseVerify-' + $nonce)
    New-Item -ItemType Directory -Path $path -ErrorAction Stop | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $path '.tsg-release-verifier-temp'),
        $nonce,
        (New-Object System.Text.UTF8Encoding($false, $true)))
    return [PSCustomObject]@{ Parent = $parent; Path = $path; Nonce = $nonce }
}

function Remove-VerificationTemp([object]$Temporary) {
    if ($null -eq $Temporary) { return $true }
    try {
        $parent = [IO.Path]::GetFullPath([string]$Temporary.Parent).TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar)
        $path = [IO.Path]::GetFullPath([string]$Temporary.Path).TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar)
        $leaf = [IO.Path]::GetFileName($path)
        $expectedLeaf = 'TarkovServerGuard-ReleaseVerify-' + [string]$Temporary.Nonce
        $marker = Join-Path $path '.tsg-release-verifier-temp'
        if ([IO.Path]::GetDirectoryName($path).TrimEnd(
                [IO.Path]::DirectorySeparatorChar,
                [IO.Path]::AltDirectorySeparatorChar) -cne $parent -or
            $leaf -cne $expectedLeaf -or
            -not (Test-Path -LiteralPath $marker -PathType Leaf) -or
            [IO.File]::ReadAllText($marker) -cne [string]$Temporary.Nonce) {
            return $false
        }
        Remove-Item -LiteralPath $path -Recurse -Force
        return -not (Test-Path -LiteralPath $path)
    }
    catch {
        return $false
    }
}

function Write-ReportAtomic([string]$Path, [object]$Report) {
    $fullPath = [IO.Path]::GetFullPath($Path)
    $parent = [IO.Path]::GetDirectoryName($fullPath)
    if ([string]::IsNullOrWhiteSpace($parent) -or
        -not (Test-Path -LiteralPath $parent -PathType Container)) {
        throw 'report-parent-invalid'
    }
    $temporary = Join-Path $parent (
        [IO.Path]::GetFileName($fullPath) + '.tmp.' + [Guid]::NewGuid().ToString('N'))
    $backup = Join-Path $parent (
        [IO.Path]::GetFileName($fullPath) + '.bak.' + [Guid]::NewGuid().ToString('N'))
    try {
        $json = ($Report | ConvertTo-Json -Depth 10) + "`n"
        $encoding = New-Object System.Text.UTF8Encoding($false, $true)
        $bytes = $encoding.GetBytes($json)
        $stream = New-Object IO.FileStream(
            $temporary,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None,
            4096,
            [IO.FileOptions]::WriteThrough)
        try {
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        }
        finally {
            $stream.Dispose()
        }
        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            [IO.File]::Replace($temporary, $fullPath, $backup, $true)
            Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
        }
        else {
            [IO.File]::Move($temporary, $fullPath)
        }
        return $fullPath
    }
    finally {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) {
            Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $backup -PathType Leaf) {
            Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
        }
    }
}

function Add-ErrorCode(
    [System.Collections.Generic.List[string]]$Errors,
    [string]$Code) {
    if (-not [string]::IsNullOrWhiteSpace($Code) -and -not $Errors.Contains($Code)) {
        $Errors.Add($Code)
    }
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = [IO.Path]::GetFullPath($ExpectedManifest) + '.verification.json'
}
foreach ($trustInput in @($ExpectedManifest, $CompletionMarker)) {
    if (-not [string]::IsNullOrWhiteSpace([string]$trustInput) -and
        [string]::Equals(
            [IO.Path]::GetFullPath($ReportPath),
            [IO.Path]::GetFullPath([string]$trustInput),
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'report-path-conflicts-with-trust-input'
    }
}
$fixtureMode = -not [string]::IsNullOrWhiteSpace($FixtureReleaseJson) -or
    -not [string]::IsNullOrWhiteSpace($FixtureAssetDirectory)
if ($fixtureMode -and
    ([string]::IsNullOrWhiteSpace($FixtureReleaseJson) -or
        [string]::IsNullOrWhiteSpace($FixtureAssetDirectory))) {
    throw 'fixture-mode-requires-json-and-assets'
}

$errors = New-Object 'System.Collections.Generic.List[string]'
$assetReports = New-Object 'System.Collections.Generic.List[object]'
$unexpectedReports = New-Object 'System.Collections.Generic.List[object]'
$temporary = $null
$client = $null
$manifest = $null
$completion = $null
$completionValidated = $false
$release = $null
$actualTag = $null
$actualAssetCount = 0
$missingCount = 0
$duplicateCount = 0
$unexpectedCount = 0
$nameMismatchCount = 0
$tempCleaned = $true
$success = $false

try {
    $completion = Read-CompletionMarker $CompletionMarker $ExpectedManifest
    $manifest = Read-ExpectedManifest $ExpectedManifest
    if ($completion.Version -cne $manifest.Version -or
        $completion.Tag -cne $manifest.Tag) {
        throw 'completion-marker-manifest-identity-mismatch'
    }
    $completionValidated = $true
    foreach ($expected in $manifest.Assets) {
        $assetReports.Add([PSCustomObject][ordered]@{
            name = $expected.Name
            role = $expected.Role
            expectedSize = [long]$expected.Size
            apiSize = $null
            downloadedSize = $null
            expectedSha256 = $expected.Sha256
            apiDigest = $null
            actualSha256 = $null
            apiSizeMatches = $false
            apiDigestPresent = $false
            apiDigestMatches = $null
            browserUrlTagBound = $false
            finalUrl = $null
            redirectCount = $null
            downloadAttempted = $false
            downloadCount = 0
            httpRequestCount = 0
            downloadCompleted = $false
            success = $false
            errors = @()
        })
    }

    $releaseText = if ($fixtureMode) {
        Read-StrictUtf8File $FixtureReleaseJson $maximumApiBytes 'fixture-release-read-failed'
    }
    else {
        $client = New-HttpClient
        Get-LiveReleaseJson $client $manifest.Tag
    }
    $release = ConvertFrom-StrictJson $releaseText 'release-json-invalid'
    $actualTag = [string]$release.tag_name
    if ($actualTag -cne $manifest.Tag) {
        Add-ErrorCode $errors 'release-tag-mismatch'
    }
    if ([bool]$release.draft) {
        Add-ErrorCode $errors 'release-is-draft'
    }
    if ([bool]$release.prerelease) {
        Add-ErrorCode $errors 'release-is-prerelease'
    }
    $expectedHtmlUrl = $githubOrigin + '/Spirit-Schema/tarkov-server-guard/releases/tag/' + $manifest.Tag
    if ([string]$release.html_url -cne $expectedHtmlUrl) {
        Add-ErrorCode $errors 'release-html-url-mismatch'
    }

    $actualAssets = @($release.assets)
    $actualAssetCount = $actualAssets.Count
    if ($actualAssets.Count -gt $maximumApiAssetCount) {
        Add-ErrorCode $errors 'release-api-asset-count-limit'
    }
    if ($errors.Contains('release-tag-mismatch') -or
        $errors.Contains('release-html-url-mismatch') -or
        $errors.Contains('release-is-draft') -or
        $errors.Contains('release-is-prerelease') -or
        $errors.Contains('release-api-asset-count-limit')) {
        foreach ($row in $assetReports) {
            $row.errors = @('release-binding-failed')
        }
    }
    else {
        $expectedExactNames = New-Object 'System.Collections.Generic.HashSet[string]' (
            [StringComparer]::Ordinal)
        foreach ($expected in $manifest.Assets) { $null = $expectedExactNames.Add($expected.Name) }
        foreach ($actual in $actualAssets) {
            $actualName = [string]$actual.name
            if (-not $expectedExactNames.Contains($actualName)) {
                $unexpectedCount++
                $safeSize = $null
                try { $safeSize = [long]$actual.size } catch { }
                $unexpectedReports.Add([PSCustomObject][ordered]@{
                    name = if ([string]::IsNullOrWhiteSpace($actualName)) {
                        '(invalid-name)'
                    } else { $actualName }
                    size = $safeSize
                })
            }
        }
        if ($unexpectedCount -gt 0) { Add-ErrorCode $errors 'unexpected-assets' }

        $temporary = New-VerificationTemp $TempRoot
        for ($index = 0; $index -lt $manifest.Assets.Count; $index++) {
            $expected = $manifest.Assets[$index]
            $row = $assetReports[$index]
            $rowErrors = New-Object 'System.Collections.Generic.List[string]'
            $caseMatches = @($actualAssets | Where-Object {
                [string]::Equals(
                    [string]$_.name,
                    $expected.Name,
                    [StringComparison]::OrdinalIgnoreCase)
            })
            $exactMatches = @($caseMatches | Where-Object { [string]$_.name -ceq $expected.Name })
            if ($exactMatches.Count -eq 0) {
                if ($caseMatches.Count -gt 0) {
                    $nameMismatchCount++
                    $rowErrors.Add('asset-name-case-mismatch')
                }
                else {
                    $missingCount++
                    $rowErrors.Add('asset-missing')
                }
                $row.errors = @($rowErrors)
                continue
            }
            if ($caseMatches.Count -ne 1 -or $exactMatches.Count -ne 1) {
                $duplicateCount++
                $rowErrors.Add('asset-duplicate')
                $row.errors = @($rowErrors)
                continue
            }

            $actual = $exactMatches[0]
            try { $row.apiSize = [long]$actual.size }
            catch { $rowErrors.Add('asset-api-size-invalid') }
            if ($null -ne $row.apiSize) {
                $row.apiSizeMatches = [long]$row.apiSize -eq [long]$row.expectedSize
                if (-not $row.apiSizeMatches) { $rowErrors.Add('asset-api-size-mismatch') }
            }
            if ([string]$actual.state -cne 'uploaded') {
                $rowErrors.Add('asset-state-not-uploaded')
            }

            $digest = [string]$actual.digest
            if (-not [string]::IsNullOrWhiteSpace($digest)) {
                $row.apiDigestPresent = $true
                if ($digest -notmatch '^sha256:(?<hash>[0-9A-Fa-f]{64})$') {
                    $row.apiDigest = '(invalid)'
                    $row.apiDigestMatches = $false
                    $rowErrors.Add('asset-api-digest-invalid')
                }
                else {
                    $row.apiDigest = 'sha256:' + ([string]$Matches['hash']).ToUpperInvariant()
                    $row.apiDigestMatches = [string]::Equals(
                        ([string]$Matches['hash']).ToUpperInvariant(),
                        $row.expectedSha256,
                        [StringComparison]::Ordinal)
                    if (-not $row.apiDigestMatches) {
                        $rowErrors.Add('asset-api-digest-mismatch')
                    }
                }
            }

            $downloadUrl = [string]$actual.browser_download_url
            $row.browserUrlTagBound = Test-BrowserDownloadBinding `
                $downloadUrl $manifest.Tag $expected.Name
            if (-not $row.browserUrlTagBound) {
                $rowErrors.Add('asset-browser-url-binding-mismatch')
            }
            else {
                $destination = Join-Path $temporary.Path ('asset-{0:D3}.download' -f $index)
                $fixtureRedirectChain = @()
                if ($fixtureMode -and
                    $null -ne $actual.PSObject.Properties['fixture_redirect_chain']) {
                    $fixtureRedirectChain = @($actual.fixture_redirect_chain)
                }
                $row.downloadAttempted = $true
                $row.downloadCount = 1
                $httpRequestsBefore = $script:httpDownloadRequestCount
                try {
                    $download = Invoke-AssetDownload `
                        $client `
                        $downloadUrl `
                        $manifest.Tag `
                        $expected.Name `
                        $destination `
                        ([long]$expected.Size) `
                        $fixtureMode `
                        $FixtureAssetDirectory `
                        $fixtureRedirectChain
                    $row.downloadCompleted = $true
                    $row.downloadedSize = [long]$download.Size
                    $row.redirectCount = [int]$download.RedirectCount
                    $row.finalUrl = [string]$download.FinalUrl
                    $row.actualSha256 = (Get-FileHash `
                        -Algorithm SHA256 `
                        -LiteralPath $destination).Hash.ToUpperInvariant()
                    if ([long]$row.downloadedSize -ne [long]$row.expectedSize) {
                        $rowErrors.Add('asset-downloaded-size-mismatch')
                    }
                    if ($row.actualSha256 -cne $row.expectedSha256) {
                        $rowErrors.Add('asset-downloaded-hash-mismatch')
                    }
                }
                catch {
                    $message = [string]$_.Exception.Message
                    if ($message -notmatch '^(asset|fixture)-[a-z0-9-]+$') {
                        $message = 'asset-download-internal-error'
                    }
                    $rowErrors.Add($message)
                }
                finally {
                    $row.httpRequestCount =
                        $script:httpDownloadRequestCount - $httpRequestsBefore
                    if (Test-Path -LiteralPath $destination -PathType Leaf) {
                        Remove-Item -LiteralPath $destination -Force -ErrorAction SilentlyContinue
                    }
                }
            }
            $row.errors = @($rowErrors)
            $row.success = $rowErrors.Count -eq 0
        }
        if ($missingCount -gt 0) { Add-ErrorCode $errors 'missing-assets' }
        if ($duplicateCount -gt 0) { Add-ErrorCode $errors 'duplicate-assets' }
        if ($nameMismatchCount -gt 0) { Add-ErrorCode $errors 'asset-name-mismatches' }
        if (@($assetReports | Where-Object { -not $_.success }).Count -gt 0) {
            Add-ErrorCode $errors 'asset-verification-failed'
        }
    }
    $success = $errors.Count -eq 0 -and
        @($assetReports | Where-Object { -not $_.success }).Count -eq 0
}
catch {
    $message = [string]$_.Exception.Message
    if ($message -notmatch '^[a-z0-9][a-z0-9-]*(?::[A-Za-z0-9_.-]+)?$') {
        $message = 'verifier-internal-error:' + $_.Exception.GetType().Name
    }
    Add-ErrorCode $errors $message
    $success = $false
}
finally {
    if ($null -ne $client) { $client.Dispose() }
    if ($null -ne $temporary) {
        $tempCleaned = Remove-VerificationTemp $temporary
        if (-not $tempCleaned) {
            Add-ErrorCode $errors 'temporary-cleanup-failed'
            $success = $false
        }
    }
}

$report = [PSCustomObject][ordered]@{
    schemaVersion = 1
    repository = $repository
    mode = if ($fixtureMode) { 'fixture' } else { 'live' }
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
    expectedTag = if ($null -eq $manifest) { $null } else { $manifest.Tag }
    actualTag = $actualTag
    success = [bool]$success
    summary = [PSCustomObject][ordered]@{
        expectedAssetCount = if ($null -eq $manifest) { 0 } else { $manifest.Assets.Count }
        actualAssetCount = $actualAssetCount
        missingCount = $missingCount
        duplicateCount = $duplicateCount
        unexpectedCount = $unexpectedCount
        nameMismatchCount = $nameMismatchCount
        assetDownloadCount = $script:assetDownloadCount
        httpDownloadRequestCount = $script:httpDownloadRequestCount
        completedDownloadCount = $script:completedDownloadCount
        downloadedBytes = $script:downloadedBytes
        completionMarkerValidated = [bool]$completionValidated
        temporaryDirectoryCleaned = [bool]$tempCleaned
    }
    assets = @($assetReports | ForEach-Object { $_ })
    unexpectedAssets = @($unexpectedReports | ForEach-Object { $_ })
    errors = @($errors | ForEach-Object { $_ })
}

$reportWriteSucceeded = $true
try {
    $writtenReport = Write-ReportAtomic $ReportPath $report
    Write-Host ('검증 보고서: ' + $writtenReport)
}
catch {
    $reportWriteSucceeded = $false
    Write-Error '검증 보고서를 안전하게 저장하지 못했습니다.'
}
if ($success -and $reportWriteSucceeded) {
    Write-Host ('공개 Release 자산 검증 성공: ' + $script:completedDownloadCount + '개 다운로드 완료')
    exit 0
}
Write-Error ('공개 Release 자산 검증 실패: ' + [string]::Join(', ', @($errors)))
exit 1
