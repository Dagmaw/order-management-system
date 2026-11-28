param([string]$Profile = 'order-management-dev')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$image = 'public.ecr.aws/aws-cli/aws-cli@sha256:4e569d37c0b93893f660946f9b00a442b1172b1c4ba86338a3a1af3937ed62b5'
function Invoke-AwsJson {
    param([string[]]$Arguments)
    $output = & docker run --rm -e "AWS_PROFILE=$Profile" -e AWS_EC2_METADATA_DISABLED=true -v "${env:USERPROFILE}/.aws:/root/.aws:ro" -v "${repo}:/workspace:ro" $image @Arguments --output json
    if ($LASTEXITCODE -ne 0) { throw 'Inventory deployment observation permission setup failed.' }
    return ($output | ConvertFrom-Json)
}
$identity = Invoke-AwsJson -Arguments @('sts','get-caller-identity')
if ($identity.Account -ne '638500336727') { throw 'Unexpected AWS account.' }
$name = 'order-management-dev-inventory-deployment-read'
$arn = "arn:aws:iam::638500336727:policy/$name"
$existing = Invoke-AwsJson -Arguments @('iam','list-policies','--scope','Local','--query',"Policies[?PolicyName=='$name'].Arn")
if ($existing.Count -eq 0) {
    $created = Invoke-AwsJson -Arguments @('iam','create-policy','--policy-name',$name,'--policy-document','file:///workspace/infra/iam/inventory-deployment-read.json')
    if ($created.Policy.Arn -ne $arn) { throw 'Unexpected policy ARN.' }
} else {
    $metadata = Invoke-AwsJson -Arguments @('iam','get-policy','--policy-arn',$arn)
    $version = Invoke-AwsJson -Arguments @('iam','get-policy-version','--policy-arn',$arn,'--version-id',$metadata.Policy.DefaultVersionId)
    $desired = Get-Content (Join-Path $repo 'infra/iam/inventory-deployment-read.json') -Raw | ConvertFrom-Json
    if (($version.PolicyVersion.Document | ConvertTo-Json -Depth 20 -Compress) -ne ($desired | ConvertTo-Json -Depth 20 -Compress)) { throw 'Existing observation policy differs; review required.' }
}
Invoke-AwsJson -Arguments @('iam','attach-role-policy','--role-name','order-management-dev-terraform-apply','--policy-arn',$arn) | Out-Null
Write-Host 'Configured read-only Inventory deployment observation for Terraform apply.'
