<#
.SYNOPSIS
  Read the PackageVersion from Directory.Build.props,
  if it exists on NuGet.org, update the minor version,
  write it back to props, and return the test version string.
#>
param(
    [Parameter(Mandatory = $true)]
    [string] $PropsPath,

    [Parameter(Mandatory = $true)]
    [string] $RepresentativePackageId
)

function Test-NuGetVersionExists {
<#
.SYNOPSIS
  Check if a package exists on NuGet.org
#>
    param(
        [Parameter(Mandatory = $true)]
        [string] $PackageId,

        [Parameter(Mandatory = $true)]
        [string] $Version
    )

    $lowerId = $PackageId.ToLower()
    $url = "https://api.nuget.org/v3-flatcontainer/$lowerId/index.json"

    Write-Host "Check $PackageId version list"
    Write-Host "  -> $url"
    try {
        $response = Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 10
    }
    catch {
        Write-Host "TypeName: $($_.Exception.GetType().FullName)"
        Write-Host "Message:  $($_.Exception.Message)"
        Write-Error "Connection Failed: NuGet.org"
        exit 1
    }

    if ($null -eq $response.versions) {
        return $false
    }

    return $response.versions -contains $Version
}


function Increment-PackageVersion {
<#
.SYNOPSIS
  Increment minor version and set 0 to patch version
#>
    param(
        [Parameter(Mandatory = $true)]
        [string] $Version
    )

    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        Write-Error "Version Format Error: $Version"
        exit 1
    }

    $major, $minor, $patch = $Version.Split('.')

    $minor = [int]$minor + 1
    $patch = 0

    return "$major.$minor.$patch"
}


function Get-PackageVersionFromProps {
<#
.SYNOPSIS
  Read the PackageVersion from PropsPath
#>
    param(
        [Parameter(Mandatory = $true)]
        [string] $PropsPath
    )

    if (-not (Test-Path $PropsPath)) {
        Write-Error "File Not Exists: $PropsPath"
        exit 1
    }

    try {
        [xml]$xml = Get-Content $PropsPath -Encoding UTF8
    }
    catch {
        Write-Error "XML Format Error: $PropsPath"
        exit 1
    }

	$pvlist=$xml.GetElementsByTagName("PackageVersion")
	if ($pvlist -eq 0) {
        Write-Error "No PackageVersion"
        exit 1
	}
    $versionNode = $pvlist[0]

    return $versionNode.InnerText.Trim()
}


function Set-PackageVersionToProps {
<#
.SYNOPSIS
  Write the PackageVersion to PropsPath
#>
    param(
        [Parameter(Mandatory = $true)]
        [string] $PropsPath,

        [Parameter(Mandatory = $true)]
        [string] $NewVersion
    )

    if (-not (Test-Path $PropsPath)) {
        Write-Error "File Not Exists: $PropsPath"
        exit 1
    }

    try {
        [xml]$xml = Get-Content $PropsPath -Encoding UTF8
    }
    catch {
        Write-Error "XML Format Error: $PropsPath"
        exit 1
    }

	$pvlist=$xml.GetElementsByTagName("PackageVersion")
	if ($pvlist -eq 0) {
        Write-Error "No PackageVersion"
        exit 1
	}
    $versionNode = $pvlist[0]
    $versionNode.InnerText = $NewVersion

    try {
        $xml.Save($PropsPath)
    }
    catch {
        Write-Error "Save Failed: $PropsPath"
        exit 1
    }
}


Write-Host "=== Check the package version ==="

# Read package version
$originalVersion = Get-PackageVersionFromProps -PropsPath $PropsPath
Write-Host "PackageVersion: $originalVersion"

# Check and Increment version
$exists = Test-NuGetVersionExists -PackageId $RepresentativePackageId -Version $originalVersion
if ($exists) {
    Write-Host "$originalVersion exists on NuGet.org. So the minor version is incremented."
    $newVersion = Increment-PackageVersion -Version $originalVersion
    Set-PackageVersionToProps -PropsPath $PropsPath -NewVersion $newVersion
}
else {
    Write-Host "$originalVersion dose not exist on NuGet.org. So $originalVersion is reused"
    $newVersion = $originalVersion
}

# Creates test version
$timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddHHmm")
$preVersion = "$newVersion-pre$timestamp"
Write-Host "New test version: $preVersion"

Write-Output "$preVersion"
