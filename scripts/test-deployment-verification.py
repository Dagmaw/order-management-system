"""Offline deployment checks: do not claim success after rollback or lost auth."""
from pathlib import Path
import sys
import unittest
from unittest.mock import patch
sys.path.insert(0, str(Path(__file__).parent))
import verify_deployment as gate


class DeploymentVerificationTests(unittest.TestCase):
    def setUp(self):
        self.manifest = {'account_id': '638500336727', 'region': 'us-east-1', 'cluster': 'order-management-dev',
                         'public_base_url': 'https://api-dev.kaddigitalsolutions.com', 'services': []}
        self.services = []
        self.definitions = {}
        self.targets = {}
        self.http = {'/health': (200, 'Healthy'), '/api/products': (200, []), '/api/auth/me': (401, ''),
                     '/api/orders': (401, ''), '/openapi/v1.json': (200, {'openapi': '3.0.0', 'paths': {'/api/orders': {}, '/api/auth/login': {}}})}
        for label, repo in [('api', 'order-management-api'), ('inventory', 'inventory-service')]:
            name = 'order-management-dev-' + label
            arn = 'arn:aws:ecs:us-east-1:638500336727:task-definition/' + name + ':3'
            image = '638500336727.dkr.ecr.us-east-1.amazonaws.com/' + repo + '@sha256:' + 'a' * 64
            tg = 'test-target-' + label
            self.manifest['services'].append({'name': name, 'task_definition_arn': arn, 'container': label, 'image': image, 'target_group_arn': tg})
            self.services.append({'serviceName': name, 'status': 'ACTIVE', 'desiredCount': 1, 'runningCount': 1, 'pendingCount': 0,
                                  'taskDefinition': arn, 'deployments': [{'status': 'PRIMARY', 'rolloutState': 'COMPLETED', 'taskDefinition': arn}],
                                  'loadBalancers': [{'targetGroupArn': tg}]})
            self.definitions[arn] = {'taskDefinition': {'containerDefinitions': [{'name': label, 'image': image}]}}
            self.targets[tg] = {'TargetHealthDescriptions': [{'TargetHealth': {'State': 'healthy'}}]}

    def aws(self, region, *args):
        self.assertEqual('us-east-1', region)
        if args[:2] == ('sts', 'get-caller-identity'):
            return {'Account': '638500336727'}
        if args[:2] == ('ecs', 'describe-services'):
            return {'services': self.services}
        if args[:2] == ('ecs', 'describe-task-definition'):
            return self.definitions[args[-1]]
        if args[:2] == ('elbv2', 'describe-target-health'):
            return self.targets[args[-1]]
        self.fail('Unexpected non-read operation')

    def run_gate(self):
        report = {'checks': [], 'services': []}
        with patch.object(gate, 'aws', side_effect=self.aws), patch.object(gate, 'http_get', side_effect=lambda url: self.http[url.removeprefix(self.manifest['public_base_url'])]):
            gate.verify(self.manifest, report)
        return report

    def test_two_stable_services_and_security_contract_pass(self):
        self.assertEqual(17, len(self.run_gate()['checks']))

    def test_rolled_back_service_revision_fails(self):
        self.services[0]['taskDefinition'] += '-old'
        with self.assertRaises(AssertionError):
            self.run_gate()

    def test_incomplete_rollout_fails(self):
        self.services[0]['deployments'][0]['rolloutState'] = 'IN_PROGRESS'
        with self.assertRaises(AssertionError):
            self.run_gate()

    def test_wrong_image_fails(self):
        arn = self.manifest['services'][0]['task_definition_arn']
        self.definitions[arn]['taskDefinition']['containerDefinitions'][0]['image'] = 'wrong:latest'
        with self.assertRaises(AssertionError):
            self.run_gate()

    def test_unhealthy_target_fails(self):
        self.targets['test-target-inventory']['TargetHealthDescriptions'][0]['TargetHealth']['State'] = 'unhealthy'
        with self.assertRaises(AssertionError):
            self.run_gate()

    def test_empty_target_group_fails(self):
        self.targets['test-target-api']['TargetHealthDescriptions'] = []
        with self.assertRaises(AssertionError):
            self.run_gate()

    def test_anonymous_access_regression_fails(self):
        self.http['/api/orders'] = (200, [])
        with self.assertRaises(AssertionError):
            self.run_gate()

    def test_missing_openapi_contract_fails(self):
        self.http['/openapi/v1.json'] = (404, '')
        with self.assertRaises(AssertionError):
            self.run_gate()

    def test_wrong_account_manifest_fails_before_aws(self):
        self.manifest['account_id'] = '000000000000'
        with patch.object(gate, 'aws') as aws:
            with self.assertRaises(ValueError):
                gate.verify(self.manifest, {'checks': [], 'services': []})
            aws.assert_not_called()


if __name__ == '__main__':
    unittest.main()
