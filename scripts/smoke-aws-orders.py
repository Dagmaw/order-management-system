"""Explicit, low-volume live smoke check for the project's AWS development API.

Creates two test Customers and one two-unit order. Replays reuse its key.
Does not fetch service secrets, reset stock, stop services, or test outages.
"""
import argparse
from datetime import datetime, timezone
from decimal import Decimal
import json
from pathlib import Path
import secrets
import time
import urllib.error
import urllib.request
import uuid

BASE = 'https://api-dev.kaddigitalsolutions.com'
RESULTS = []


def request(path, method='GET', body=None, token=None, key=None):
    headers = {'Content-Type': 'application/json'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    if key:
        headers['Idempotency-Key'] = key
    req = urllib.request.Request(BASE + path, method=method, headers=headers,
                                 data=json.dumps(body).encode() if body is not None else None)
    try:
        response = urllib.request.urlopen(req, timeout=45)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        raw = response.read(1024 * 1024).decode()
        try:
            data = json.loads(raw, parse_float=Decimal)
        except json.JSONDecodeError:
            data = raw
        return response.status, data


def expect(name, passed):
    RESULTS.append({'name': name, 'passed': bool(passed)})
    print(('PASS: ' if passed else 'FAIL: ') + name)
    if not passed:
        raise AssertionError(name)


def customer(run_id, suffix):
    body = {'name': 'AWS Integration Smoke ' + run_id[:8] + ' ' + suffix,
            'email': 'aws-smoke-' + run_id + '-' + suffix + '@example.com',
            'password': 'Aws!' + secrets.token_hex(20)}
    status, _ = request('/api/auth/register', 'POST', body)
    expect('Register test Customer ' + suffix, status == 201)
    status, login = request('/api/auth/login', 'POST', {'email': body['email'], 'password': body['password']})
    expect('Log in test Customer ' + suffix, status == 200)
    return login['accessToken'], login['userId']


def tests(report):
    run_id = report['runId']
    expect('Public API health', request('/health') == (200, 'Healthy'))
    status, product = request('/api/products/1')
    expect('Existing test product available', status == 200 and product['price'] > 0)
    token, user = customer(run_id, 'owner')
    other, _ = customer(run_id, 'other')
    report['ownerUserId'] = user
    expect('Orders require authentication', request('/api/orders')[0] == 401)
    expect('Customer cannot list users', request('/api/users', token=token)[0] == 403)
    payload = {'items': [{'productId': 1, 'quantity': 2}]}
    expect('Integrated orders require an idempotency key', request('/api/orders', 'POST', payload, token)[0] == 400)
    key = str(uuid.uuid4())
    status, order = request('/api/orders', 'POST', payload, token, key)
    # A timed-out dependency may legitimately leave a durable submission pending.
    for _ in range(12):
        if status != 202:
            break
        time.sleep(5)
        status, order = request('/api/orders', 'POST', payload, token, key)
    expect('Create integrated order with frozen price', status == 201 and order['totalAmount'] == product['price'] * 2)
    report['orderId'] = order['id']
    report['totalAmount'] = str(order['totalAmount'])
    report['idempotencyKey'] = key
    status, replay = request('/api/orders', 'POST', payload, token, key)
    expect('Replay returns the same order', status == 201 and replay['id'] == order['id'] and replay['totalAmount'] == order['totalAmount'])
    changed = {'items': [{'productId': 1, 'quantity': 1}]}
    expect('Changed payload under same key rejected', request('/api/orders', 'POST', changed, token, key)[0] == 409)
    status, fetched = request('/api/orders/' + str(order['id']), token=token)
    expect('Owner can retrieve the order', status == 200 and fetched['id'] == order['id'])
    expect('Other Customer cannot retrieve the order', request('/api/orders/' + str(order['id']), token=other)[0] == 404)
    invalid = {'items': [{'productId': 1, 'quantity': 0}]}
    expect('Zero quantity rejected', request('/api/orders', 'POST', invalid, token, str(uuid.uuid4()))[0] == 400)
    insufficient = {'items': [{'productId': 1, 'quantity': 1000}]}
    status, rejected = request('/api/orders', 'POST', insufficient, token, str(uuid.uuid4()))
    report['rejectionStatus'] = status
    report['submissionState'] = rejected.get('state') if isinstance(rejected, dict) else None
    expect('Insufficient stock rejected and compensated', status == 409 and report['submissionState'] == 'Compensated')
    status, orders = request('/api/orders', token=token)
    expect('Customer has exactly the one accepted order', status == 200 and len(orders) == 1 and orders[0]['id'] == order['id'])


def stock_check(report):
    expect('Public API health', request('/health') == (200, 'Healthy'))
    token, user = customer(report['runId'], 'stock-check')
    report['ownerUserId'] = user
    payload = {'items': [{'productId': 1, 'quantity': 1000}]}
    status, rejected = request('/api/orders', 'POST', payload, token, str(uuid.uuid4()))
    report['rejectionStatus'] = status
    report['submissionState'] = rejected.get('state') if isinstance(rejected, dict) else None
    expect('Insufficient stock rejected and compensated', status == 409 and report['submissionState'] == 'Compensated')
    status, orders = request('/api/orders', token=token)
    expect('Rejected submission creates no order', status == 200 and orders == [])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', action='store_true', help='Create the documented test records in AWS dev.')
    parser.add_argument('--report', type=Path, required=True, help='Sanitized JSON evidence destination.')
    parser.add_argument('--stock-check-only', action='store_true', help='Only register one Customer and verify a within-limit stock rejection; no accepted order.')
    args = parser.parse_args()
    if not args.run:
        parser.error('--run is required because this creates development test records')
    report = {'runId': uuid.uuid4().hex, 'startedAtUtc': datetime.now(timezone.utc).isoformat(),
              'baseUrl': BASE, 'privateStockVerification': 'not performed', 'results': RESULTS}
    try:
        if args.stock_check_only:
            stock_check(report)
        else:
            tests(report)
        report['passed'] = True
    finally:
        report['finishedAtUtc'] = datetime.now(timezone.utc).isoformat()
        report.setdefault('passed', False)
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, indent=2) + '\n')
    print('Selected public AWS Orders smoke checks passed. Private stock evidence remains separate.')


if __name__ == '__main__':
    main()
