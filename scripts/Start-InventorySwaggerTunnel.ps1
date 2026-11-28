param([string]$InstanceId, [string]$Profile = 'order-management-dev', [switch]$NoBrowser)
. (Join-Path $PSScriptRoot 'ssm-tunnel/common.ps1')
Initialize-TunnelClient -Profile $Profile
$InstanceId = Get-TunnelInstance -InstanceId $InstanceId
& docker build -t $script:ClientImage (Join-Path $PSScriptRoot 'ssm-tunnel')
if ($LASTEXITCODE -ne 0) { throw 'Tunnel client build failed.' }
$identity = Invoke-TunnelAws -Arguments @('sts','get-caller-identity')
if ($identity.Arn -notlike 'arn:aws:sts::638500336727:assumed-role/order-management-dev-inventory-tunnel-operator/*') { throw 'Unexpected operator identity.' }
if (!(Assert-TunnelContainer)) {
    if (Get-NetTCPConnection -State Listen -LocalPort 443 -ErrorAction SilentlyContinue) { throw 'Local port 443 is already in use.' }
    $instance = Invoke-TunnelAws -Arguments @('ec2','describe-instances','--instance-ids',$InstanceId)
    $state = $instance.Reservations[0].Instances[0].State.Name
    if ($state -eq 'stopped') { Invoke-TunnelAws -Arguments @('ec2','start-instances','--instance-ids',$InstanceId) | Out-Null }
    elseif ($state -notin @('running','pending')) { throw "Helper state is $state; wait and retry." }
    Invoke-TunnelAws -Arguments @('ec2','wait','instance-running','--instance-ids',$InstanceId) | Out-Null
    $online = $false
    for ($attempt=0; $attempt -lt 40; $attempt++) {
        $info = Invoke-TunnelAws -Arguments @('ssm','describe-instance-information','--filters',"Key=InstanceIds,Values=$InstanceId")
        if ($info.InstanceInformationList -and $info.InstanceInformationList[0].PingStatus -eq 'Online') { $online=$true; break }
        Start-Sleep -Seconds 5
    }
    if (!$online) { throw 'The helper has not connected to Session Manager. Check its role, agent and HTTPS route.' }
    & docker run --rm -d --name $script:ContainerName --label oms.inventory-tunnel=true -p '127.0.0.1:443:9443' @script:Mounts -e "TUNNEL_INSTANCE_ID=$InstanceId" $script:ClientImage
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the tunnel container.' }
}
$ready=$false
for ($attempt=0; $attempt -lt 30; $attempt++) {
    & curl.exe --fail --silent --show-error --max-time 5 --noproxy '*' --resolve "$($script:InventoryHost):443:127.0.0.1" "https://$($script:InventoryHost)/v3/api-docs" --output NUL 2>$null
    if ($LASTEXITCODE -eq 0) { $ready=$true; break }
    Start-Sleep -Seconds 2
}
if (!$ready) { throw 'HTTPS verification failed. Inspect docker logs oms-inventory-swagger-tunnel. Stop the helper when finished.' }
Write-Host 'Private Inventory HTTPS is ready; TLS certificate verification passed.'
if (!$NoBrowser) {
    $chrome = Join-Path $env:ProgramFiles 'Google/Chrome/Application/chrome.exe'
    if (!(Test-Path $chrome)) { throw 'Chrome was not found. Use -NoBrowser to verify the tunnel only.' }
    $browserProfile=Join-Path $script:State 'chrome-profile'
    Start-Process -FilePath $chrome -WindowStyle Normal -ArgumentList @("--user-data-dir=`"$browserProfile`"", "--host-resolver-rules=`"MAP $($script:InventoryHost) 127.0.0.1`"",'--no-proxy-server','--disable-quic',"https://$($script:InventoryHost)/swagger-ui/index.html")
}
Write-Host 'When finished, run scripts/Stop-InventorySwaggerTunnel.ps1 to stop the tunnel and helper.'
