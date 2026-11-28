"""Bounded stock-delta verification; no machine secret or database access."""
import argparse
from datetime import datetime, timezone
from decimal import Decimal
import json
import secrets
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

API = 'https://api-dev.kaddigitalsolutions.com'
INVENTORY = 'https://inventory.dev.internal.kaddigitalsolutions.com'


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def validate_urls(api, inventory, local):
    if not local:
        if (api, inventory) != (API, INVENTORY):
            raise ValueError('AWS verification requires the fixed project HTTPS endpoints.')
        return
    for url in (api, inventory):
        parsed = urllib.parse.urlsplit(url)
        if parsed.scheme != 'http' or parsed.hostname not in ('127.0.0.1', 'localhost') or parsed.username or parsed.password or parsed.path or parsed.query or parsed.fragment:
            raise ValueError('Local verification requires bare loopback HTTP origins.')


def run_verification(api=API, inventory=INVENTORY, local=False):
    validate_urls(api, inventory, local)
    report = {'runId': uuid.uuid4().hex, 'startedAtUtc': datetime.now(timezone.utc).isoformat(),
              'mode': 'isolated-local' if local else 'aws-private', 'checks': [], 'snapshots': {}}
    opener = urllib.request.build_opener(NoRedirect())

    def request(base, path, method='GET', body=None, token=None, key=None):
        headers = {'Content-Type': 'application/json'}
        if token:
            headers['Authorization'] = 'Bearer ' + token
        if key:
            headers['Idempotency-Key'] = key
        req = urllib.request.Request(base + path, method=method, headers=headers,
                                     data=json.dumps(body).encode() if body is not None else None)
        try:
            response = opener.open(req, timeout=20)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            raw = response.read(1024 * 1024).decode()
            try:
                value = json.loads(raw, parse_float=Decimal)
            except json.JSONDecodeError:
                value = raw
            return response.status, value

    def expect(name, passed):
        report['checks'].append({'name': name, 'passed': bool(passed)})
        print(('PASS: ' if passed else 'FAIL: ') + name, flush=True)
        if not passed:
            raise AssertionError(name)

    def stock(label):
        status, result = request(inventory, '/api/inventory/1')
        expect('Private stock lookup ' + label, status == 200 and isinstance(result, dict)
               and isinstance(result.get('availableQuantity'), int) and isinstance(result.get('version'), int))
        snapshot = {'quantity': result['availableQuantity'], 'version': result['version']}
        report['snapshots'][label] = snapshot
        return snapshot

    try:
        expect('Public API healthy', request(api, '/health') == (200, 'Healthy'))
        status, product = request(api, '/api/products/1')
        expect('Orders product 1 exists', status == 200 and product['price'] > 0)
        initial = stock('initial')
        expect('Bounded development fixture available', 2 <= initial['quantity'] <= 999)
        credentials = {'name': 'Private Stock Verification ' + report['runId'][:8],
                       'email': 'private-stock-' + report['runId'] + '@example.com',
                       'password': 'Verify!' + secrets.token_hex(20)}
        expect('Register fresh Customer', request(api, '/api/auth/register', 'POST', credentials)[0] == 201)
        status, login = request(api, '/api/auth/login', 'POST', {'email': credentials['email'], 'password': credentials['password']})
        expect('Customer login', status == 200)
        token = login['accessToken']
        payload = {'items': [{'productId': 1, 'quantity': 2}]}
        expect('Integrated API requires idempotency key', request(api, '/api/orders', 'POST', payload, token)[0] == 400)
        before = stock('before')
        expect('Stock unchanged during setup', before == initial)
        key = str(uuid.uuid4())
        report['idempotencyKey'] = key
        status, order = request(api, '/api/orders', 'POST', payload, token, key)
        for _ in range(12):
            if status != 202:
                break
            time.sleep(5)
            status, order = request(api, '/api/orders', 'POST', payload, token, key)
        expect('Integrated order accepted at frozen price', status == 201 and order['totalAmount'] == product['price'] * 2)
        report['orderId'] = order['id']
        report['totalAmount'] = str(order['totalAmount'])
        after = stock('after_order')
        expect('Exactly two units reserved once', after['quantity'] == before['quantity'] - 2 and after['version'] == before['version'] + 1)
        status, replay = request(api, '/api/orders', 'POST', payload, token, key)
        expect('Replay returns same order', status == 201 and replay['id'] == order['id'] and replay['totalAmount'] == order['totalAmount'])
        expect('Replay makes no stock or version change', stock('after_replay') == after)
        changed = {'items': [{'productId': 1, 'quantity': 1}]}
        expect('Changed payload rejected', request(api, '/api/orders', 'POST', changed, token, key)[0] == 409)
        expect('Conflict makes no stock or version change', stock('after_conflict') == after)
        insufficient = {'items': [{'productId': 1, 'quantity': after['quantity'] + 1}]}
        status, rejected = request(api, '/api/orders', 'POST', insufficient, token, str(uuid.uuid4()))
        expect('Insufficient stock compensated', status == 409 and isinstance(rejected, dict) and rejected.get('state') == 'Compensated')
        expect('Rejection makes no stock or version change', stock('after_rejection') == after)
        status, orders = request(api, '/api/orders', token=token)
        expect('Exactly one accepted Customer order', status == 200 and len(orders) == 1 and orders[0]['id'] == order['id'])
        report['passed'] = True
    except Exception as error:
        report['passed'] = False
        report['errorType'] = type(error).__name__
        # Do not print raw HTTP exceptions, bodies, credentials, or access tokens.
    report['finishedAtUtc'] = datetime.now(timezone.utc).isoformat()
    print(json.dumps(report), flush=True)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', action='store_true', help='Create one test Customer and one two-unit order.')
    args = parser.parse_args()
    if not args.run:
        parser.error('--run is required for persistent development test records')
    return 0 if run_verification()['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
