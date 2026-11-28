"""Launch only the reviewed private task; never fetch credentials."""
import json
import os
from pathlib import Path
import re
import subprocess
import time


def aws(*args):
    result = subprocess.run(['aws', '--region', 'us-east-1', *args, '--output', 'json'],
                            text=True, capture_output=True, timeout=660)
    if result.returncode:
        raise RuntimeError('AWS verification request failed: ' + ' '.join(args[:2]))
    return json.loads(result.stdout) if result.stdout.strip() else {}


def main():
    definition = os.environ['INVENTORY_VERIFICATION_TASK_DEFINITION']
    subnets = os.environ['INVENTORY_VERIFICATION_SUBNET_IDS'].split(',')
    group = os.environ['INVENTORY_VERIFICATION_SECURITY_GROUP_ID']
    if not re.fullmatch(r'arn:aws:ecs:us-east-1:638500336727:task-definition/order-management-dev-inventory-verification:[1-9][0-9]*', definition):
        raise ValueError('A reviewed Inventory verification task revision is required.')
    if len(subnets) != 2 or len(set(subnets)) != 2 or not all(re.fullmatch(r'subnet-[0-9a-f]{8,17}', item) for item in subnets):
        raise ValueError('Two distinct private app subnet IDs are required.')
    if not re.fullmatch(r'sg-[0-9a-f]{8,17}', group):
        raise ValueError('The Inventory task security group is required.')
    if aws('sts', 'get-caller-identity')['Account'] != '638500336727':
        raise ValueError('Unexpected AWS account.')
    network = {'awsvpcConfiguration': {'subnets': subnets, 'securityGroups': [group], 'assignPublicIp': 'DISABLED'}}
    response = aws('ecs', 'run-task', '--cluster', 'order-management-dev', '--task-definition', definition,
                   '--launch-type', 'FARGATE', '--platform-version', '1.4.0', '--count', '1',
                   '--client-token', 'inventory-verification-' + os.environ['GITHUB_RUN_ID'] + '-' + os.environ['GITHUB_RUN_ATTEMPT'],
                   '--network-configuration', json.dumps(network))
    if response.get('failures') or len(response.get('tasks', [])) != 1:
        raise RuntimeError('Inventory verification task could not be launched.')
    task = response['tasks'][0]['taskArn']
    print('Launched private Inventory verification:', task)
    try:
        aws('ecs', 'wait', 'tasks-stopped', '--cluster', 'order-management-dev', '--tasks', task)
    finally:
        result = aws('ecs', 'describe-tasks', '--cluster', 'order-management-dev', '--tasks', task)
        tasks = result.get('tasks', [])
        containers = tasks[0].get('containers', []) if len(tasks) == 1 else []
        summary = [{'name': item.get('name'), 'status': item.get('lastStatus'), 'exitCode': item.get('exitCode')} for item in containers]
        print(json.dumps({'task': task, 'containers': summary}))
    if result.get('failures') or len(tasks) != 1 or tasks[0]['lastStatus'] != 'STOPPED' or [item.get('exitCode') for item in containers if item.get('name') == 'inventory-verification'] != [0]:
        raise RuntimeError('Inventory verification did not complete successfully. Review the private task logs.')
    report = collect_report(task)
    safe = validate_report(report)
    print(json.dumps(safe))
    with Path(os.environ['GITHUB_STEP_SUMMARY']).open('a') as stream:
        stream.write('### Private stock verification passed\n\nTask: `' + task + '`\n\nOrder: ' + str(safe['orderId']) +
                     '\n\nAll stock-delta, replay, conflict, and rejection checks passed. No credentials retrieved.\n')


def validate_report(report):
    if report.get('mode') != 'aws-private' or report.get('passed') is not True:
        raise ValueError('A successful AWS-private report is required.')
    checks = report.get('checks', [])
    if len(checks) < 22 or not all(item.get('passed') is True for item in checks):
        raise ValueError('Verifier report has incomplete or failed checks.')
    snapshots = report.get('snapshots', {})
    before, after = snapshots['before'], snapshots['after_order']
    if after['quantity'] != before['quantity'] - 2 or after['version'] != before['version'] + 1:
        raise ValueError('Verifier stock delta is inconsistent.')
    if not all(snapshots[name] == after for name in ('after_replay', 'after_conflict', 'after_rejection')):
        raise ValueError('Stock changed after a replay, conflict, or rejection.')
    if type(report.get('orderId')) is not int or report['orderId'] <= 0:
        raise ValueError('An accepted verification order ID is required.')
    return {name: report[name] for name in ('runId', 'mode', 'orderId', 'totalAmount', 'passed', 'snapshots')}


def collect_report(task):
    stream = 'verification/inventory-verification/' + task.rsplit('/', 1)[-1]
    for attempt in range(6):
        try:
            logs = aws('logs', 'get-log-events', '--log-group-name', '/ecs/order-management-dev/inventory-verification',
                       '--log-stream-name', stream, '--start-from-head')
            for event in logs.get('events', []):
                try:
                    report = json.loads(event.get('message', ''))
                except json.JSONDecodeError:
                    continue
                if isinstance(report, dict) and report.get('mode') == 'aws-private':
                    return report
        except RuntimeError:
            if attempt == 5:
                raise
        if attempt < 5:
            time.sleep(2)
    raise RuntimeError('No structured verifier evidence found in the dedicated task log.')



if __name__ == '__main__':
    main()
