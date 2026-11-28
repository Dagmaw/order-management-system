param([string]$Profile = 'order-management-dev')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$image = 'public.ecr.aws/aws-cli/aws-cli@sha256:4e569d37c0b93893f660946f9b00a442b1172b1c4ba86338a3a1af3937ed62b5'
function Invoke-AwsJson {
    param([string[]]$Arguments)
    $output = & docker run --rm -e "AWS_PROFILE=$Profile" -e AWS_EC2_METADATA_DISABLED=true -v "${env:USERPROFILE}/.aws:/root/.aws:ro" -v "${repo}:/workspace:ro" $image @Arguments --output json
    if ($LASTEXITCODE -ne 0) { throw 'AWS permission setup failed; no broader fallback is attempted.' }
    return ($output | ConvertFrom-Json)
}
$identity = Invoke-AwsJson -Arguments @('sts','get-caller-identity')
if ($identity.Account -ne '638500336727') { throw 'The profile must target the project AWS account.' }
foreach ($kind in @('plan','apply')) {
    $name = "order-management-dev-inventory-tunnel-terraform-$kind"
    $arn = "arn:aws:iam::638500336727:policy/$name"
    $existing = Invoke-AwsJson -Arguments @('iam','list-policies','--scope','Local','--query',"Policies[?PolicyName=='$name'].Arn")
    if ($existing.Count -eq 0) {
        $created = Invoke-AwsJson -Arguments @('iam','create-policy','--policy-name',$name,'--policy-document',"file:///workspace/infra/iam/inventory-tunnel-terraform-$kind.json")
        if ($created.Policy.Arn -ne $arn) { throw 'Unexpected policy ARN.' }
    } else {
        # Fail closed on differing policy documents; review updates separately.
        $metadata = Invoke-AwsJson -Arguments @('iam','get-policy','--policy-arn',$arn)
        $version = Invoke-AwsJson -Arguments @('iam','get-policy-version','--policy-arn',$arn,'--version-id',$metadata.Policy.DefaultVersionId)
        $desired = Get-Content (Join-Path $repo "infra/iam/inventory-tunnel-terraform-$kind.json") -Raw | ConvertFrom-Json
        if (($version.PolicyVersion.Document | ConvertTo-Json -Depth 20 -Compress) -ne ($desired | ConvertTo-Json -Depth 20 -Compress)) {
            throw "Policy $name already exists with different contents; review it before updating."
        }
    }
    Invoke-AwsJson -Arguments @('iam','attach-role-policy','--role-name',"order-management-dev-terraform-$kind",'--policy-arn',$arn) | Out-Null
    Write-Host "Configured scoped Inventory tunnel permissions for Terraform $kind."
}


# Apply also needs the helper read policy. Existing Inventory role/network policy scopes are reused.
Invoke-AwsJson -Arguments @('iam','attach-role-policy','--role-name','order-management-dev-terraform-apply','--policy-arn','arn:aws:iam::638500336727:policy/order-management-dev-inventory-tunnel-terraform-plan') | Out-Null
