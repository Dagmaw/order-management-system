param([string]$InstanceId, [string]$Profile = 'order-management-dev', [switch]$KeepHelperRunning)
. (Join-Path $PSScriptRoot 'ssm-tunnel/common.ps1')
Initialize-TunnelClient -Profile $Profile
$InstanceId = Get-TunnelInstance -InstanceId $InstanceId
if (Assert-TunnelContainer) {
    & docker stop --time 10 $script:ContainerName
    if ($LASTEXITCODE -ne 0) { throw 'Could not stop the local tunnel.' }
}
if (!$KeepHelperRunning) {
    Invoke-TunnelAws -Arguments @('ec2','stop-instances','--instance-ids',$InstanceId) | Out-Null
    Write-Host 'Helper stop requested. API, Inventory, database and networking remain running.'
} else { Write-Host 'Local tunnel stopped; helper remains running and billed.' }
