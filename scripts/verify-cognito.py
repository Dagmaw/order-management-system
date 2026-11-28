"""Run disposable Orders/Inventory tests using an existing Cognito client secret.

Requires AWS CLI credentials with read access to the dedicated secret. Never prints it.
"""
import json
import os
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
result = subprocess.run([
    'aws', 'secretsmanager', 'get-secret-value', '--region', 'us-east-1',
    '--secret-id', 'order-management/dev/orders-service-identity', '--output', 'json', '--no-cli-pager'
], capture_output=True, text=True)
if result.returncode:
    raise SystemExit('Cannot read the dedicated Cognito secret; verify the approved role and Terraform apply.')
try:
    settings = json.loads(json.loads(result.stdout)['SecretString'])
    env = os.environ.copy()
    env.update(E2E_AUTH_MODE='ClientCredentials', E2E_JWT_MODE='Cognito',
               E2E_TOKEN_ENDPOINT=settings['tokenEndpoint'], E2E_CLIENT_ID=settings['clientId'],
               E2E_CLIENT_SECRET=settings['clientSecret'], E2E_ISSUER=settings['issuer'],
               E2E_JWKS_URI=settings['jwksUri'], E2E_SCOPE=settings['scope'])
except (KeyError, ValueError, TypeError):
    raise SystemExit('The Cognito secret has an invalid settings schema.') from None
raise SystemExit(subprocess.run([sys.executable, str(root / 'scripts/e2e-orders.py')], cwd=root, env=env).returncode)
