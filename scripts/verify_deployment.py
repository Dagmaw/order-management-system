"""Read-only deployment gate. No Customer creation, credentials, or stock writes."""
import argparse
import json
import os
import re
from pathlib import Path
import subprocess
import urllib.error
import urllib.request


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def aws(region, *args):
    result = subprocess.run(['aws', '--region', region, *args, '--output', 'json'],
                            text=True, capture_output=True, timeout=45)
    if result.returncode:
        raise RuntimeError('AWS metadata request failed: ' + ' '.join(args[:2]))
    return json.loads(result.stdout)


def http_get(url):
    opener = urllib.request.build_opener(NoRedirect())
    try:
        response = opener.open(urllib.request.Request(url, method='GET'), timeout=20)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        body = response.read(4 * 1024 * 1024).decode()
        try:
            body = json.loads(body)
        except json.JSONDecodeError:
            pass
        return response.status, body


def verify(manifest, report):
    def expect(name, condition):
        report['checks'].append({'name': name, 'passed': bool(condition)})
        print(('PASS: ' if condition else 'FAIL: ') + name, flush=True)
        if not condition:
            raise AssertionError(name)

    if manifest['account_id'] != '638500336727' or manifest['region'] != 'us-east-1' or manifest['cluster'] != 'order-management-dev' or manifest['public_base_url'] != 'https://api-dev.kaddigitalsolutions.com':
        raise ValueError('Deployment expectations must target the fixed project account and HTTPS API.')
    expected = manifest['services']
    names = [item['name'] for item in expected]
    if not names or len(names) != len(set(names)) or not set(names) <= {'order-management-dev-api', 'order-management-dev-inventory'} or 'order-management-dev-api' not in names:
        raise ValueError('Only the project application services may be verified.')
    region = manifest['region']
    expect('AWS account matches expectations', aws(region, 'sts', 'get-caller-identity')['Account'] == manifest['account_id'])
    result = aws(region, 'ecs', 'describe-services', '--cluster', manifest['cluster'], '--services', *names)
    expect('All expected services exist', not result.get('failures') and {item['serviceName'] for item in result['services']} == set(names))
    actual = {item['serviceName']: item for item in result['services']}
    for item in expected:
        service = actual[item['name']]
        prefix = item['name'] + ': '
        primary = [deployment for deployment in service.get('deployments', []) if deployment['status'] == 'PRIMARY']
        expect(prefix + 'stable desired task count', service['status'] == 'ACTIVE' and service['desiredCount'] > 0
               and service['runningCount'] == service['desiredCount'] and service['pendingCount'] == 0
               and len(primary) == 1 and primary[0].get('rolloutState') == 'COMPLETED')
        expect(prefix + 'reviewed task revision', service['taskDefinition'] == item['task_definition_arn']
               and primary[0]['taskDefinition'] == item['task_definition_arn'])
        definition = aws(region, 'ecs', 'describe-task-definition', '--task-definition', item['task_definition_arn'])['taskDefinition']
        containers = [container for container in definition['containerDefinitions'] if container['name'] == item['container']]
        expect(prefix + 'immutable image pin matches', re.fullmatch(r'638500336727\.dkr\.ecr\.us-east-1\.amazonaws\.com/(order-management-api|inventory-service)@sha256:[a-f0-9]{64}', item['image']) is not None and len(containers) == 1 and containers[0]['image'] == item['image'])
        expect(prefix + 'expected target group attached', item['target_group_arn'] in [entry['targetGroupArn'] for entry in service.get('loadBalancers', [])])
        targets = aws(region, 'elbv2', 'describe-target-health', '--target-group-arn', item['target_group_arn'])['TargetHealthDescriptions']
        active = [entry['TargetHealth']['State'] for entry in targets if entry['TargetHealth']['State'] not in ('draining', 'unused')]
        expect(prefix + 'load-balancer readiness healthy', len(active) >= service['desiredCount'] and all(state == 'healthy' for state in active))
        report['services'].append({'name': item['name'], 'taskDefinition': item['task_definition_arn'], 'image': item['image'], 'healthyTargets': len(active)})
    base = manifest['public_base_url']
    expect('Public API health', http_get(base + '/health') == (200, 'Healthy'))
    status, products = http_get(base + '/api/products')
    expect('Public product lookup', status == 200 and isinstance(products, list))
    expect('Anonymous identity request rejected', http_get(base + '/api/auth/me')[0] == 401)
    expect('Anonymous orders request rejected', http_get(base + '/api/orders')[0] == 401)
    status, schema = http_get(base + '/openapi/v1.json')
    expect('OpenAPI contract available', status == 200 and isinstance(schema, dict) and str(schema.get('openapi', '')).startswith('3.')
           and '/api/orders' in schema.get('paths', {}) and '/api/auth/login' in schema.get('paths', {}))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--manifest', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args()
    report = {'passed': False, 'checks': [], 'services': []}
    try:
        verify(json.loads(args.manifest.read_text()), report)
        report['passed'] = True
    except Exception as error:
        report['errorType'] = type(error).__name__
        print('Deployment verification failed; inspect the failed check or metadata access.', flush=True)
    finally:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, indent=2) + '\n')
        if os.environ.get('GITHUB_STEP_SUMMARY'):
            with Path(os.environ['GITHUB_STEP_SUMMARY']).open('a') as summary:
                summary.write('### Read-only deployment verification: ' + ('passed' if report['passed'] else 'failed') + '\n\n'
                              + str(sum(check['passed'] for check in report['checks'])) + ' checks passed. No accounts, orders, or reservations created.\n')
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
