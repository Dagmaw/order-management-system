"""Disposable PostgreSQL + real .NET/Java HTTP integration and recovery checks."""
import base64
import concurrent.futures
import json
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import time
import urllib.error
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]
PROJECT = 'oms-e2e-' + uuid.uuid4().hex[:12]
WORK = ROOT / '.ci-integration' / PROJECT
ENV = os.environ.copy()
COMPOSE = ['docker', 'compose', '-p', PROJECT, '-f', str(ROOT / 'compose.e2e.yaml')]
RESULTS = []


def run(*args, capture=False, input=None):
    return subprocess.run(args, cwd=ROOT, env=ENV, check=True, text=True,
                          input=input, stdout=subprocess.PIPE if capture else None,
                          timeout=900).stdout


def dc(*args, **kwargs):
    return run(*COMPOSE, *args, **kwargs)


def request(base, path, method='GET', body=None, token=None, key=None):
    headers = {'Content-Type': 'application/json'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    if key:
        headers['Idempotency-Key'] = key
    req = urllib.request.Request(base + path, method=method, headers=headers,
                                 data=json.dumps(body).encode() if body is not None else None)
    try:
        response = urllib.request.urlopen(req, timeout=20)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        raw = response.read()
        try:
            body = json.loads(raw) if raw else None
        except ValueError:
            body = raw.decode('utf-8', errors='replace')
        return response.status, body


def expect(name, condition, details=None):
    if not condition:
        if details is not None:
            print('FAIL DETAILS: ' + json.dumps(details), flush=True)
        raise AssertionError(name)
    RESULTS.append(name)
    print('PASS: ' + name, flush=True)


def wait_ready(base, path):
    deadline = time.monotonic() + 120
    while time.monotonic() < deadline:
        try:
            if request(base, path)[0] == 200:
                return
        except (OSError, ValueError):
            pass
        time.sleep(2)
    raise RuntimeError('Service readiness timed out')


def base(service):
    address = dc('port', service, '8080', capture=True).strip()
    return 'http://' + address


def customer(api):
    body = {'name': 'Isolated CI Customer', 'email': uuid.uuid4().hex + '@example.com',
            'password': 'Ci!' + secrets.token_hex(16)}
    status, _ = request(api, '/api/auth/register', 'POST', body)
    expect('Customer registration', status == 201)
    status, login = request(api, '/api/auth/login', 'POST', body)
    expect('Customer login', status == 200)
    return login['accessToken']


def tests():
    api, inventory = base('api'), base('inventory')
    wait_ready(api, '/health')
    wait_ready(inventory, '/api/inventory/1')
    token = customer(api)
    other = customer(api)
    payload = {'items': [{'productId': 1, 'quantity': 2}]}
    key = str(uuid.uuid4())
    status, order = request(api, '/api/orders', 'POST', payload, token, key)
    expect('Order reserves stock and freezes price', status == 201 and order['totalAmount'] == 60,
           {'http_status': status, 'submission_state': order.get('state') if isinstance(order, dict) else None})
    status, replay = request(api, '/api/orders', 'POST', payload, token, key)
    expect('Replay returns the same order', status == 201 and replay['id'] == order['id'])
    expect('Replay does not reserve twice', request(inventory, '/api/inventory/1')[1]['availableQuantity'] == 98)
    changed = {'items': [{'productId': 1, 'quantity': 3}]}
    expect('Changed payload conflicts', request(api, '/api/orders', 'POST', changed, token, key)[0] == 409)
    expect('Anonymous order denied', request(api, '/api/orders/' + str(order['id']))[0] == 401)
    expect('Other customer cannot read order', request(api, '/api/orders/' + str(order['id']), token=other)[0] == 404)
    expect('Inventory mutation requires service token', request(inventory, '/api/inventory/reservations', 'POST', {})[0] == 401)
    insufficient = {'items': [{'productId': 1, 'quantity': 1000}]}
    status, rejected = request(api, '/api/orders', 'POST', insufficient, token, str(uuid.uuid4()))
    expect('Insufficient stock is compensated', status == 409 and rejected['state'] == 'Compensated')
    expect('Rejected order preserves stock', request(inventory, '/api/inventory/1')[1]['availableQuantity'] == 98)
    duplicate_key = str(uuid.uuid4())
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        replies = list(pool.map(lambda _: request(api, '/api/orders', 'POST', payload, token, duplicate_key), range(4)))
    expect('Concurrent replay accepted without server errors', all(s in (201, 202) for s, _ in replies))
    status, duplicate = request(api, '/api/orders', 'POST', payload, token, duplicate_key)
    expect('Concurrent replay completes one order', status == 201 and duplicate['id'] != order['id'])
    expect('Concurrent replay reserves once', request(inventory, '/api/inventory/1')[1]['availableQuantity'] == 96)
    dc('stop', 'inventory')
    recovery_key = str(uuid.uuid4())
    status, pending = request(api, '/api/orders', 'POST', payload, token, recovery_key)
    expect('Inventory outage leaves durable pending submission', status == 202 and pending['state'] == 'Pending')
    path = '/api/orders/submissions/' + pending['id']
    expect('Submission ownership enforced', request(api, path, token=other)[0] == 404)
    dc('restart', 'api')
    dc('start', 'inventory')
    api, inventory = base('api'), base('inventory')
    wait_ready(api, '/health')
    wait_ready(inventory, '/api/inventory/1')
    deadline = time.monotonic() + 100
    while time.monotonic() < deadline:
        status, submission = request(api, path, token=token)
        if status == 200 and submission['state'] == 'Completed':
            break
        time.sleep(2)
    else:
        raise AssertionError('Pending submission recovery timed out')
    status, recovered = request(api, '/api/orders', 'POST', payload, token, recovery_key)
    expect('Restart recovers pending submission into one order', status == 201 and recovered['id'] == submission['orderId'])
    expect('Recovery reserves stock exactly once', request(inventory, '/api/inventory/1')[1]['availableQuantity'] == 94)


def main():
    WORK.mkdir(parents=True)
    openssl = shutil.which('openssl') or 'C:/Program Files/Git/usr/bin/openssl.exe'
    private = WORK / 'private-key.pem'
    run(openssl, 'genpkey', '-algorithm', 'RSA', '-pkeyopt', 'rsa_keygen_bits:2048', '-out', str(private))
    private.chmod(0o644)  # Disposable development key must be readable by container UID.
    public = subprocess.run([openssl, 'pkey', '-in', str(private), '-pubout', '-outform', 'DER'],
                            check=True, stdout=subprocess.PIPE).stdout
    ENV.update(E2E_DB_PASSWORD=secrets.token_hex(24), E2E_USER_KEY=base64.b64encode(secrets.token_bytes(48)).decode(),
               E2E_PUBLIC_KEY=base64.b64encode(public).decode(), E2E_KEY_PATH=private.as_posix())
    ok = False
    try:
        dc('up', '-d', '--wait', 'order-db', 'inventory-db')
        dc('run', '--rm', 'migrate')
        sql = '''INSERT INTO "Products" ("Id","Name","Description","Price","CreatedAt") VALUES (1,'CI product','Disposable fixture',30,NOW());'''
        dc('exec', '-T', 'order-db', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'e2e', '-d', 'orders', input=sql)
        dc('up', '-d', '--build', 'api', 'inventory')
        tests()
        ok = True
    finally:
        try:
            dc('down', '--volumes', '--remove-orphans', '--timeout', '10')
        finally:
            private.unlink(missing_ok=True)
            report = ROOT / 'artifacts' / 'e2e-results.json'
            report.parent.mkdir(exist_ok=True)
            report.write_text(json.dumps({'passed': ok, 'checks': RESULTS}, indent=2) + '\n')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
