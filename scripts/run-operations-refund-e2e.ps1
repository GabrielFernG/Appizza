$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$web = Join-Path $repo 'src/Web/Appizza.Operations'
$runId = [Guid]::NewGuid().ToString('N')
$container = "appizza-e2e-$($runId.Substring(0,8))"
$pgPort = Get-Random -Minimum 15432 -Maximum 19432; $apiPort = Get-Random -Minimum 25000 -Maximum 29000; $webPort = Get-Random -Minimum 31000 -Maximum 35000
$db = "Host=127.0.0.1;Port=$pgPort;Database=appizza_e2e;Username=postgres;Password=postgres"
$temp = Join-Path ([IO.Path]::GetTempPath()) "appizza-e2e-$runId"; New-Item -ItemType Directory -Path $temp | Out-Null
$processes = [System.Collections.Generic.List[object]]::new(); $exitCode = 1
Write-Host "PowerShell: Version=$($PSVersionTable.PSVersion) Edition=$($PSVersionTable.PSEdition)"
$script:ArgumentListSupported = $false
$script:EnvironmentSupported = $false
$script:EnvironmentVariablesSupported = $false
function Quote-WindowsArgument([string]$value) {
  if ($null -eq $value -or $value.Length -eq 0) { return '""' }
  if ($value -notmatch '[\s"]') { return $value }
  $result = '"'; $slashes = 0
  foreach ($ch in $value.ToCharArray()) { if ($ch -eq '\\') { $slashes++; continue }; if ($ch -eq '"') { $result += ('\\' * ($slashes * 2 + 1)) + '"'; $slashes = 0; continue }; if ($slashes -gt 0) { $result += ('\\' * $slashes); $slashes = 0 }; $result += $ch }
  if ($slashes -gt 0) { $result += ('\\' * ($slashes * 2)) }; return $result + '"'
}
function Start-App([string]$name, [string]$file, [string[]]$arguments, [hashtable]$environment, [string]$workingDirectory = $repo) {
  if (-not (Test-Path -LiteralPath $workingDirectory -PathType Container)) { throw "Working directory does not exist for $name`: $workingDirectory" }
  $psi = [Diagnostics.ProcessStartInfo]::new(); $psi.FileName = $file; $psi.WorkingDirectory = $workingDirectory; $psi.UseShellExecute = $false; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
  $argumentList = $psi.PSObject.Properties['ArgumentList']; if ($null -ne $argumentList -and $null -ne $argumentList.Value) { foreach ($argument in $arguments) { [void]$argumentList.Value.Add($argument) }; $script:ArgumentListSupported = $true } else { $psi.Arguments = (($arguments | ForEach-Object { Quote-WindowsArgument $_ }) -join ' '); $script:ArgumentListSupported = $false }
  $environmentProperty = $psi.PSObject.Properties['Environment']; $environmentVariablesProperty = $psi.PSObject.Properties['EnvironmentVariables']; foreach ($key in $environment.Keys) { if ($null -ne $environmentProperty -and $null -ne $environmentProperty.Value) { $environmentProperty.Value[$key] = [string]$environment[$key]; $script:EnvironmentSupported = $true } elseif ($null -ne $environmentVariablesProperty -and $null -ne $environmentVariablesProperty.Value) { $environmentVariablesProperty.Value[$key] = [string]$environment[$key]; $script:EnvironmentVariablesSupported = $true } else { throw "ProcessStartInfo has no environment API on this runtime." } }
  $out = Join-Path $temp "$name.stdout.log"; $err = Join-Path $temp "$name.stderr.log"; New-Item -ItemType File -Path $out,$err -Force | Out-Null
  $p = [Diagnostics.Process]::new(); $p.StartInfo = $psi; $p.EnableRaisingEvents = $true
  Register-ObjectEvent $p OutputDataReceived -Action { if ($EventArgs.Data) { Add-Content -LiteralPath $Event.MessageData.Stdout -Value $EventArgs.Data } } -MessageData @{ Stdout = $out } | Out-Null
  Register-ObjectEvent $p ErrorDataReceived -Action { if ($EventArgs.Data) { Add-Content -LiteralPath $Event.MessageData.Stderr -Value $EventArgs.Data } } -MessageData @{ Stderr = $err } | Out-Null
  if (-not $p.Start()) { throw "Failed to start $name" }; $p.BeginOutputReadLine(); $p.BeginErrorReadLine()
  $record = [pscustomobject]@{ Name=$name; Process=$p; Stdout=$out; Stderr=$err; WorkingDirectory=$workingDirectory; Command=("$file " + (($arguments | ForEach-Object { Quote-WindowsArgument $_ }) -join ' ')); Pid=$p.Id }
  $processes.Add($record); return $record
}
$probe = [Diagnostics.ProcessStartInfo]::new()
$probeArgs = $probe.PSObject.Properties['ArgumentList']; $probeEnv = $probe.PSObject.Properties['Environment']; $probeEnvVars = $probe.PSObject.Properties['EnvironmentVariables']
Write-Host "ProcessStartInfo: ArgumentList supported=$([bool]($null -ne $probeArgs -and $null -ne $probeArgs.Value)) Environment supported=$([bool]($null -ne $probeEnv -and $null -ne $probeEnv.Value)) EnvironmentVariables supported=$([bool]($null -ne $probeEnvVars -and $null -ne $probeEnvVars.Value))"
function Wait-ProcessExit($record, [int]$seconds = 30) {
  if ($null -eq $record -or $null -eq $record.Process) { throw 'Process record is null.' }
  $process = $record.Process
  if (-not $process.WaitForExit($seconds * 1000)) { throw "$($record.Name) did not exit within $seconds seconds." }
  $process.WaitForExit()
  $stdout = Get-Content $record.Stdout -Raw -ErrorAction SilentlyContinue
  $stderr = Get-Content $record.Stderr -Raw -ErrorAction SilentlyContinue
  if ($process.ExitCode -ne 0) { throw "$($record.Name) exited with code $($process.ExitCode).`nPID: $($record.Pid)`nWorkingDirectory: $($record.WorkingDirectory)`nCommand: $($record.Command)`nSTDOUT:`n$stdout`nSTDERR:`n$stderr" }
  # A successful short-lived process is valid even when asynchronous capture
  # has not flushed bytes yet. Output remains available for diagnostics.
}
function Test-PortAvailable([int]$port) {
  $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
  try { $listener.Start(); return $true } catch { return $false } finally { if ($null -ne $listener) { $listener.Stop() } }
}
function Start-OperationsDirect([string]$workingDirectory, [int]$port, [string]$apiUrl) {
  $node = (Get-Command node.exe -ErrorAction Stop).Source
  $vitePackage = Join-Path $workingDirectory 'node_modules/vite/package.json'
  $viteCli = Join-Path $workingDirectory 'node_modules/vite/bin/vite.js'
  if (-not (Test-Path -LiteralPath $vitePackage -PathType Leaf) -or -not (Test-Path -LiteralPath $viteCli -PathType Leaf)) { throw "Local Vite installation not found under $workingDirectory\node_modules." }
  $viteVersion = ((Get-Content $vitePackage -Raw | ConvertFrom-Json).version)
  $out = Join-Path $temp 'operations.stdout.log'; $err = Join-Path $temp 'operations.stderr.log'
  New-Item -ItemType File -Path $out,$err -Force | Out-Null
  $oldApi = $env:VITE_APPIZZA_API_URL; $env:VITE_APPIZZA_API_URL = $apiUrl
  try { $p = Start-Process -FilePath $node -ArgumentList @($viteCli,'--host','127.0.0.1','--port',"$port") -WorkingDirectory $workingDirectory -RedirectStandardOutput $out -RedirectStandardError $err -PassThru } finally { if ($null -eq $oldApi) { Remove-Item Env:VITE_APPIZZA_API_URL -ErrorAction SilentlyContinue } else { $env:VITE_APPIZZA_API_URL = $oldApi } }
  if ($null -eq $p) { throw 'Failed to start direct local Vite process.' }
  return [pscustomobject]@{ Name='operations'; Process=$p; Stdout=$out; Stderr=$err; WorkingDirectory=$workingDirectory; Command="$node $viteCli --host 127.0.0.1 --port $port"; Pid=$p.Id; ViteVersion=$viteVersion; ViteCli=$viteCli }
}
function Wait-Ready($record, [string]$url, [int]$seconds = 60) {
  if ($null -eq $record -or $null -eq $record.Process) { throw 'Readiness process record is null.' }
  $process = $record.Process; $until = (Get-Date).AddSeconds($seconds); do { if ($process.HasExited) { $process.WaitForExit(); $stdout = Get-Content $record.Stdout -Raw -ErrorAction SilentlyContinue; $stderr = Get-Content $record.Stderr -Raw -ErrorAction SilentlyContinue; throw "$($record.Name) exited with code $($process.ExitCode).`nPID: $($record.Pid)`nWorkingDirectory: $($record.WorkingDirectory)`nCommand: $($record.Command)`nSTDOUT:`n$stdout`nSTDERR:`n$stderr" }; try { $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3; if ($r.StatusCode -lt 500) { return } } catch {}; Start-Sleep -Milliseconds 250 } while ((Get-Date) -lt $until)
  throw "$($record.Name) readiness timeout: $url"
}
try {
  $smoke = Start-App 'start-app-smoke' 'dotnet' @('--version') @{}
  Wait-ProcessExit $smoke
  $packageJson = Join-Path $web 'package.json'
  if (-not (Test-Path -LiteralPath $packageJson -PathType Leaf)) { throw "Operations package.json not found: $packageJson" }
  $package = Get-Content -LiteralPath $packageJson -Raw | ConvertFrom-Json
  if ($null -eq $package.scripts -or [string]::IsNullOrWhiteSpace([string]$package.scripts.dev)) { throw "Operations package.json does not define a dev script." }
  if (-not (Test-Path -LiteralPath (Join-Path $web 'node_modules') -PathType Container)) { throw 'Operations dependencies are not installed. Run npm ci in src/Web/Appizza.Operations.' }
  if (-not (Test-PortAvailable $webPort)) { throw "Selected Operations port is unavailable: $webPort" }
  Write-Host "Node: $(node --version)"
  Write-Host "Npm: $(npm.cmd --version)"
  docker run --rm -d --name $container -e POSTGRES_DB=appizza_e2e -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=postgres -p "127.0.0.1:${pgPort}:5432" postgres:18.4 | Out-Null
  $until = (Get-Date).AddSeconds(60); do { docker exec $container pg_isready -U postgres -d appizza_e2e 2>$null | Out-Null; if ($LASTEXITCODE -eq 0) { break }; Start-Sleep -Milliseconds 500 } while ((Get-Date) -lt $until); if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL readiness timeout.' }
  $fixtureLog = Join-Path $temp 'fixture.stdout.log'; dotnet run --project (Join-Path $repo 'tests/Appizza.E2EFixtures/Appizza.E2EFixtures.csproj') --no-restore -- $db *> $fixtureLog; if ($LASTEXITCODE -ne 0) { throw "Fixture failed. See $fixtureLog" }; $fixture = (Get-Content $fixtureLog | Select-Object -Last 1) | ConvertFrom-Json
  $common = @{ 'ConnectionStrings__Appizza'=$db; 'Phase1Security__SigningKey'='appizza-e2e-signing-key-32-characters-min'; 'Phase1Security__CpfEncryptionKey'=[Convert]::ToBase64String([byte[]]::new(32)); 'Phase1Security__CpfHmacKey'=[Convert]::ToBase64String([byte[]]::new(32)); 'ObjectStorage__Endpoint'='http://127.0.0.1:8333'; 'ObjectStorage__Bucket'='e2e'; 'ObjectStorage__AccessKey'='e2e'; 'ObjectStorage__SecretKey'='e2e'; 'DevelopmentSeed__Enabled'='true'; 'DevelopmentSeed__AdminPassword'='E2eAdmin!123'; 'ASPNETCORE_ENVIRONMENT'='Development'; 'APPIZZA_E2E'='true'; 'AppizzaE2E'='true'; 'Cors__AllowedOrigins__0'="http://127.0.0.1:$webPort"; 'Payments__Worker__PollSeconds'='1' }
  $api = Start-App 'api' 'dotnet' @('run','--project',(Join-Path $repo 'src/Backend/Appizza.Api/Appizza.Api.csproj'),'--no-restore','--urls',"http://127.0.0.1:$apiPort") $common; Wait-Ready $api "http://127.0.0.1:$apiPort/health/ready"
  $worker = Start-App 'worker' 'dotnet' @('run','--project',(Join-Path $repo 'src/Backend/Appizza.Worker/Appizza.Worker.csproj'),'--no-restore') $common
  if ($worker.Process.HasExited) { $worker.Process.WaitForExit(); throw "WORKER_STARTUP_FAILURE: exit code $($worker.Process.ExitCode).`nSTDOUT:`n$(Get-Content $worker.Stdout -Raw)`nSTDERR:`n$(Get-Content $worker.Stderr -Raw)" }
  $operations = Start-OperationsDirect $web $webPort "http://127.0.0.1:$apiPort"; $processes.Add($operations); Wait-Ready $operations "http://127.0.0.1:$webPort"
  $signIn = @{ establishmentCode='APPIZZA-DEV'; login='admin'; password='E2eAdmin!123' } | ConvertTo-Json; $admin = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$apiPort/api/v1/auth/sign-in" -ContentType 'application/json' -Body $signIn
  $noSignIn = @{ establishmentCode='APPIZZA-DEV'; login=$fixture.noRefundLogin; password=$fixture.noRefundPassword } | ConvertTo-Json; $noAccess = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:$apiPort/api/v1/auth/sign-in" -ContentType 'application/json' -Body $noSignIn
  $manifest = [pscustomobject]@{ operationsUrl="http://127.0.0.1:$webPort"; apiUrl="http://127.0.0.1:$apiPort"; auth=[pscustomobject]@{ refundToken=$admin.accessToken; noRefundToken=$noAccess.accessToken }; scenarios=$fixture.scenarios }
  $manifestPath = Join-Path $temp 'scenario-manifest.json'; $manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestPath
  $env:APPIZZA_E2E_MANIFEST = $manifestPath
  $env:APPIZZA_OPERATIONS_URL = "http://127.0.0.1:$webPort"
  Push-Location $web; try { npm run test:e2e:refunds; $exitCode = $LASTEXITCODE } finally { Pop-Location }
} catch { Write-Host $_.Exception.ToString(); Write-Host $_.ScriptStackTrace; Write-Host $_.InvocationInfo.PositionMessage; $exitCode = 1 } finally {
  foreach ($item in $processes) { if ($null -ne $item.Process -and !$item.Process.HasExited) { try { taskkill.exe /PID $item.Process.Id /T /F 2>$null | Out-Null } catch { } } }
  if (-not [string]::IsNullOrWhiteSpace($container)) { try { docker rm -f $container 2>$null | Out-Null } catch { Write-Host "Docker cleanup skipped: $($_.Exception.Message)" } }
  if ($exitCode -eq 0) { Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue } else { Write-Host "E2E diagnostics preserved at $temp" }
}
exit $exitCode
