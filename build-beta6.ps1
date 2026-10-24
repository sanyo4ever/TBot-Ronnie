param(
	[string]$ReleaseName = 'release-build-beta6-final',
	[string]$Version = '3.5.0-beta6',
	[string]$AssemblyVersion = '3.5.0.6'
)

$repoRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$releaseRoot = Join-Path $repoRoot $ReleaseName
$ogameRoot = Join-Path $repoRoot 'ogame'

if (Test-Path -LiteralPath $releaseRoot) {
	throw "Refusing to overwrite existing release directory: $releaseRoot"
}

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

$platforms = @(
	[pscustomobject]@{ Name='win64'; Runtime='win-x64'; GoOS='windows'; GoArch='amd64'; GoArm=$null; GoName='ogamed.exe' },
	[pscustomobject]@{ Name='win32'; Runtime='win-x86'; GoOS='windows'; GoArch='386'; GoArm=$null; GoName='ogamed.exe' },
	[pscustomobject]@{ Name='linux64'; Runtime='linux-x64'; GoOS='linux'; GoArch='amd64'; GoArm=$null; GoName='ogamed' },
	[pscustomobject]@{ Name='linuxarm'; Runtime='linux-arm'; GoOS='linux'; GoArch='arm'; GoArm='7'; GoName='ogamed' },
	[pscustomobject]@{ Name='linuxarm64'; Runtime='linux-arm64'; GoOS='linux'; GoArch='arm64'; GoArm=$null; GoName='ogamed' },
	[pscustomobject]@{ Name='osx64'; Runtime='osx-x64'; GoOS='darwin'; GoArch='amd64'; GoArm=$null; GoName='ogamed' },
	[pscustomobject]@{ Name='osx-arm64'; Runtime='osx-arm64'; GoOS='darwin'; GoArch='arm64'; GoArm=$null; GoName='ogamed' }
)

foreach ($platform in $platforms) {
	$packageRoot = Join-Path $releaseRoot $platform.Name
	Write-Output "=== Building $($platform.Name) ($($platform.Runtime)) ==="

	dotnet publish (Join-Path $repoRoot 'TBot') `
		-c Release `
		-p:Version=$Version `
		-p:AssemblyVersion=$AssemblyVersion `
		-p:FileVersion=$AssemblyVersion `
		-p:InformationalVersion=$Version `
		-r $platform.Runtime `
		-o $packageRoot `
		-p:PublishSingleFile=true --self-contained true
	if ($LASTEXITCODE -ne 0) {
		throw "dotnet publish failed for $($platform.Name)"
	}

	$goOutput = Join-Path $packageRoot $platform.GoName
	Push-Location $ogameRoot
	try {
		$env:GOOS = $platform.GoOS
		$env:GOARCH = $platform.GoArch
		if ($null -eq $platform.GoArm) {
			Remove-Item Env:GOARM -ErrorAction SilentlyContinue
		} else {
			$env:GOARM = $platform.GoArm
		}

		go build -o $goOutput ./cmd/ogamed-tbot
		if ($LASTEXITCODE -ne 0) {
			throw "ogamed build failed for $($platform.Name)"
		}
	} finally {
		Pop-Location
		Remove-Item Env:GOOS -ErrorAction SilentlyContinue
		Remove-Item Env:GOARCH -ErrorAction SilentlyContinue
		Remove-Item Env:GOARM -ErrorAction SilentlyContinue
	}

	Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') `
		-Destination (Join-Path $packageRoot 'README.md') -Force
	Copy-Item -LiteralPath (Join-Path $repoRoot 'CHANGELOG.md') `
		-Destination (Join-Path $packageRoot 'CHANGELOG.md') -Force
	$null = Get-Content -LiteralPath (Join-Path $packageRoot 'settings.json') -Raw | ConvertFrom-Json
	$instanceSettings = Get-Content -LiteralPath (Join-Path $packageRoot 'instance_settings.json') -Raw | ConvertFrom-Json
	if ($null -eq $instanceSettings.Brain.AutoFleetJumpGate.Target -or
		$instanceSettings.Brain.AutoFleetJumpGate.Target -is [array]) {
		throw "Invalid clean AutoFleetJumpGate.Target in $($platform.Name)"
	}

	$unsafeFiles = Get-ChildItem -LiteralPath $packageRoot -Recurse -Force -File |
		Where-Object { $_.Name -match '(^|\.)(db|sqlite|log)$|cookie|session' }
	if ($unsafeFiles) {
		throw "Session or database data found in $($platform.Name)"
	}

	$zipPath = Join-Path $releaseRoot "TBot-v$Version-$($platform.Name).zip"
	Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
	Write-Output "Packaged $zipPath"
}

Write-Output "All seven $Version platform packages built."
