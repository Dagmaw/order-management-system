"""Local development tokens only. Requires Python package cryptography.
Never deploy this helper or its private key as a public token issuer.
"""
from pathlib import Path
from datetime import datetime, timezone
import argparse, base64, json, secrets, time
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa, padding

parser=argparse.ArgumentParser()
parser.add_argument('profile',choices=['customer','stock-admin','order-service'])
args=parser.parse_args()
root=Path(__file__).resolve().parent.parent
directory=root/'.local-inventory-auth';directory.mkdir(exist_ok=True)
private_path=directory/'private-key.pem'
if not private_path.exists():
    key=rsa.generate_private_key(public_exponent=65537,key_size=2048)
    private_path.write_bytes(key.private_bytes(serialization.Encoding.PEM,serialization.PrivateFormat.PKCS8,serialization.NoEncryption()))
else:
    key=serialization.load_pem_private_key(private_path.read_bytes(),password=None)
public=base64.b64encode(key.public_key().public_bytes(serialization.Encoding.DER,serialization.PublicFormat.SubjectPublicKeyInfo)).decode()
env=root/'.env.inventory.local'
settings=env.read_text(encoding='utf-8-sig') if env.exists() else ''
lines=settings.splitlines();updated=[];found=False
for line in lines:
    if line.startswith('INVENTORY_JWT_PUBLIC_KEY_BASE64='):
        updated.append('INVENTORY_JWT_PUBLIC_KEY_BASE64='+public);found=True
    else:updated.append(line)
if not found:updated.append('INVENTORY_JWT_PUBLIC_KEY_BASE64='+public)
env.write_text('\n'.join(updated)+'\n',encoding='utf-8')
values={line.split('=',1)[0]:line.split('=',1)[1].strip().strip('"\'') for line in updated if '=' in line and not line.startswith('#')}
now=int(time.time());scope={'customer':'','stock-admin':'inventory.stock.write','order-service':'inventory.reserve'}[args.profile]
claims={'iss':values.get('INVENTORY_JWT_ISSUER','Inventory.LocalIssuer'),'aud':values.get('INVENTORY_JWT_AUDIENCE','Inventory.API'),
        'sub':args.profile,'iat':now,'nbf':now,'exp':now+900,'jti':secrets.token_hex(16),'scope':scope}
def encode(data):return base64.urlsafe_b64encode(data).rstrip(b'=').decode()
message=encode(json.dumps({'alg':'RS256','typ':'JWT'},separators=(',',':')).encode())+'.'+encode(json.dumps(claims,separators=(',',':')).encode())
token=message+'.'+encode(key.sign(message.encode(),padding.PKCS1v15(),hashes.SHA256()))
output=directory/(args.profile+'-token.txt');output.write_text(token,encoding='utf-8')
print(f'Token saved privately to {output}. Expires {datetime.fromtimestamp(now+900,timezone.utc).isoformat()}.')
print('The public verification key is configured in .env.inventory.local. Recreate Inventory after first setup or key rotation.')
