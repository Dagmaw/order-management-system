$ErrorActionPreference = 'Stop'
$script:Repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$script:State = Join-Path $script:Repo '.local-inventory-tunnel'
$script:ClientImage = 'oms-inventory-ssm-client:local'
$script:ContainerName = 'oms-inventory-swagger-tunnel'
$script:InventoryHost = 'inventory.dev.internal.kaddigitalsolutions.com'
function Initialize-TunnelClient {
    param([string]$Profile)
    if ($Profile -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Invalid AWS profile name.' }
    $credentials = Join-Path $env:USERPROFILE '.aws/credentials'
    if (!(Test-Path $credentials)) { throw 'The AWS shared credentials file is required.' }
    New-Item -ItemType Directory -Force $script:State | Out-Null
    $config = "[profile $Profile]`nregion = us-east-1`n[profile inventory-swagger-tunnel]`nrole_arn = arn:aws:iam::638500336727:role/order-management-dev-inventory-tunnel-operator`nsource_profile = $Profile`nrole_session_name = inventory-swagger`nregion = us-east-1`n"
    [IO.File]::WriteAllText((Join-Path $script:State 'config'), $config)
    $script:Mounts = @('-e','AWS_PROFILE=inventory-swagger-tunnel','-e','AWS_CONFIG_FILE=/run/tunnel/config','-e','AWS_SHARED_CREDENTIALS_FILE=/run/aws/credentials','-e','AWS_EC2_METADATA_DISABLED=true','-v',"${credentials}:/run/aws/credentials:ro",'-v',"$($script:State):/run/tunnel:ro")
}
function Invoke-TunnelAws {
    param([string[]]$Arguments)
    $result = & docker run --rm @script:Mounts --entrypoint aws $script:ClientImage @Arguments --region us-east-1 --output json
    if ($LASTEXITCODE -ne 0) { throw 'The scoped AWS tunnel operation failed.' }
    if ($result) { return ($result | ConvertFrom-Json) }
}
function Get-TunnelInstance {
    param([string]$InstanceId)
    if (!$InstanceId) {
        $settings = Join-Path $script:State 'settings.json'
        if (!(Test-Path $settings)) { throw 'Pass -InstanceId using the inventory_swagger_tunnel Terraform output.' }
        $InstanceId = (Get-Content $settings -Raw | ConvertFrom-Json).instance_id
    }
    if ($InstanceId -notmatch '^i-[0-9a-f]{17}$') { throw 'Invalid helper instance ID.' }
    return $InstanceId
}
function Assert-TunnelContainer {
    $label = & docker inspect --format '{{ index .Config.Labels "oms.inventory-tunnel" }}' $script:ContainerName 2>$null
    if ($LASTEXITCODE -eq 0 -and $label -ne 'true') { throw 'A different container owns the tunnel name.' }
    return ($LASTEXITCODE -eq 0)
}
