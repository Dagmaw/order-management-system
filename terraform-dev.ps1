$credentialsFile = Join-Path $env:USERPROFILE '.aws\credentials'
$terraformDir = Join-Path $PSScriptRoot 'infra\aws\dev'

if (-not (Test-Path -LiteralPath $credentialsFile -PathType Leaf)) {
    throw "AWS credentials file is missing at $credentialsFile"
}

$terraformArgs = @(
    'run', '--rm', '-it'
    '--mount', "type=bind,source=$terraformDir,target=/workspace"
    '--mount', "type=bind,source=$credentialsFile,target=/aws/credentials,readonly"
    '--workdir', '/workspace'
    '--env', 'AWS_SHARED_CREDENTIALS_FILE=/aws/credentials'
    '--env', 'AWS_PROFILE=order-management-dev'
    'hashicorp/terraform:1.17.2'
)

docker @terraformArgs @args
exit $LASTEXITCODE