"""Launch only the reviewed private task; never fetch database credentials."""
import json
import os
from pathlib import Path
import re
import subprocess


def aws(*args):
    result = subprocess.run(['aws', '--region', 'us-east-1', *args, '--output', 'json'],
                            text=True, capture_output=True, timeout=660)
    if result.returncode:
        raise RuntimeError('AWS bootstrap request failed: ' + ' '.join(args[:2]))
    return json.loads(result.stdout) if result.stdout.strip() else {}


def main():
    definition = os.environ['INVENTORY_BOOTSTRAP_TASK_DEFINITION']
    subnets = os.environ['INVENTORY_BOOTSTRAP_SUBNET_IDS'].split(',')
    group = os.environ['INVENTORY_BOOTSTRAP_SECURITY_GROUP_ID']
    if not re.fullmatch(r'arn:aws:ecs:us-east-1:638500336727:task-definition/order-management-dev-inventory-bootstrap:[1-9][0-9]*', definition):
        raise ValueError('A reviewed Inventory bootstrap task revision is required.')
    if len(subnets) != 2 or len(set(subnets)) != 2 or not all(re.fullmatch(r'subnet-[0-9a-f]{8,17}', item) for item in subnets):
        raise ValueError('Two distinct private app subnet IDs are required.')
    if not re.fullmatch(r'sg-[0-9a-f]{8,17}', group):
        raise ValueError('The Inventory task security group is required.')
    if aws('sts', 'get-caller-identity')['Account'] != '638500336727':
        raise ValueError('Unexpected AWS account.')
    network = {'awsvpcConfiguration': {'subnets': subnets, 'securityGroups': [group], 'assignPublicIp': 'DISABLED'}}
    response = aws('ecs', 'run-task', '--cluster', 'order-management-dev', '--task-definition', definition,
                   '--launch-type', 'FARGATE', '--platform-version', '1.4.0', '--count', '1',
                   '--client-token', 'inventory-bootstrap-' + os.environ['GITHUB_RUN_ID'] + '-' + os.environ['GITHUB_RUN_ATTEMPT'],
                   '--network-configuration', json.dumps(network))
    if response.get('failures') or len(response.get('tasks', [])) != 1:
        raise RuntimeError('Inventory bootstrap task could not be launched.')
    task = response['tasks'][0]['taskArn']
    print('Launched private Inventory bootstrap:', task)
    try:
        aws('ecs', 'wait', 'tasks-stopped', '--cluster', 'order-management-dev', '--tasks', task)
    finally:
        result = aws('ecs', 'describe-tasks', '--cluster', 'order-management-dev', '--tasks', task)
        tasks = result.get('tasks', [])
        containers = tasks[0].get('containers', []) if len(tasks) == 1 else []
        summary = [{'name': item.get('name'), 'status': item.get('lastStatus'), 'exitCode': item.get('exitCode')} for item in containers]
        print(json.dumps({'task': task, 'containers': summary}))
    if result.get('failures') or len(tasks) != 1 or tasks[0]['lastStatus'] != 'STOPPED' or [item.get('exitCode') for item in containers if item.get('name') == 'inventory-bootstrap'] != [0]:
        raise RuntimeError('Inventory bootstrap did not complete successfully. Review the private task logs.')
    print('Inventory database and dedicated login verified by the bootstrap task.')
    with Path(os.environ['GITHUB_STEP_SUMMARY']).open('a') as stream:
        stream.write('### Inventory database bootstrap succeeded\n\nTask: `' + task + '`\n\nDedicated database/login verified. Java service activation remains a separate step.\n')


if __name__ == '__main__':
    main()
