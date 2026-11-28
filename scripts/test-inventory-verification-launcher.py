"""Offline checks for fixed-task launch restrictions and failed-task handling."""
import importlib.util
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('launcher', Path(__file__).with_name('run-inventory-verification.py'))
launcher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(launcher)


class VerificationLaunchTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.env = {'INVENTORY_VERIFICATION_TASK_DEFINITION': 'arn:aws:ecs:us-east-1:638500336727:task-definition/order-management-dev-inventory-verification:1',
                    'INVENTORY_VERIFICATION_SUBNET_IDS': 'subnet-11111111,subnet-22222222',
                    'INVENTORY_VERIFICATION_SECURITY_GROUP_ID': 'sg-11111111',
                    'GITHUB_RUN_ID': '123', 'GITHUB_RUN_ATTEMPT': '1',
                    'GITHUB_STEP_SUMMARY': str(Path(self.directory.name) / 'summary.md')}

    def report(self):
        after = {'quantity': 98, 'version': 1}
        return {'mode': 'aws-private', 'passed': True, 'runId': 'test', 'orderId': 1, 'totalAmount': '60',
                'checks': [{'passed': True} for _ in range(22)],
                'snapshots': {'before': {'quantity': 100, 'version': 0}, 'after_order': dict(after),
                              'after_replay': dict(after), 'after_conflict': dict(after), 'after_rejection': dict(after)}}

    def results(self, exit_code=0):
        import json
        return [{'Account': '638500336727'}, {'tasks': [{'taskArn': 'test-task'}]}, {},
                {'tasks': [{'lastStatus': 'STOPPED', 'containers': [{'name': 'inventory-verification', 'exitCode': exit_code}]}]},
                {'events': [{'message': json.dumps(self.report())}]}]

    def test_fixed_private_task_and_no_credential_overrides(self):
        with patch.dict(os.environ, self.env), patch.object(launcher, 'aws', side_effect=self.results()) as aws:
            launcher.main()
        launch = aws.call_args_list[1].args
        self.assertNotIn('--overrides', launch)
        self.assertIn(self.env['INVENTORY_VERIFICATION_TASK_DEFINITION'], launch)
        self.assertIn('"assignPublicIp": "DISABLED"', launch[-1])
        self.assertTrue(all('get-secret-value' not in call.args for call in aws.call_args_list))

    def test_wrong_task_family_rejected_before_aws_access(self):
        self.env['INVENTORY_VERIFICATION_TASK_DEFINITION'] = self.env['INVENTORY_VERIFICATION_TASK_DEFINITION'].replace('inventory-verification', 'database-setup')
        with patch.dict(os.environ, self.env), patch.object(launcher, 'aws') as aws:
            with self.assertRaises(ValueError):
                launcher.main()
            aws.assert_not_called()

    def test_wrong_account_rejected_before_launch(self):
        with patch.dict(os.environ, self.env), patch.object(launcher, 'aws', return_value={'Account': '000000000000'}) as aws:
            with self.assertRaises(ValueError):
                launcher.main()
            self.assertEqual(1, aws.call_count)

    def test_failed_task_is_not_reported_successful(self):
        with patch.dict(os.environ, self.env), patch.object(launcher, 'aws', side_effect=self.results(1)):
            with self.assertRaises(RuntimeError):
                launcher.main()
        self.assertFalse(Path(self.env['GITHUB_STEP_SUMMARY']).exists())

    def test_waiter_failure_still_inspects_task(self):
        results = self.results()
        results[2] = RuntimeError('waiter failed')
        with patch.dict(os.environ, self.env), patch.object(launcher, 'aws', side_effect=results) as aws:
            with self.assertRaises(RuntimeError):
                launcher.main()
            self.assertEqual(('ecs', 'describe-tasks'), aws.call_args.args[:2])
        self.assertFalse(Path(self.env['GITHUB_STEP_SUMMARY']).exists())

    def test_missing_private_evidence_is_rejected(self):
        with self.assertRaises(ValueError):
            launcher.validate_report({})

    def test_failed_assertion_is_rejected(self):
        report = self.report()
        report['checks'][0]['passed'] = False
        with self.assertRaises(ValueError):
            launcher.validate_report(report)

    def test_incorrect_version_increment_is_rejected(self):
        report = self.report()
        report['snapshots']['after_order']['version'] = 0
        with self.assertRaises(ValueError):
            launcher.validate_report(report)

    def test_replay_stock_change_is_rejected(self):
        report = self.report()
        report['snapshots']['after_replay']['quantity'] = 97
        with self.assertRaises(ValueError):
            launcher.validate_report(report)

    def test_local_report_cannot_pass_as_aws_evidence(self):
        report = self.report()
        report['mode'] = 'isolated-local'
        with self.assertRaises(ValueError):
            launcher.validate_report(report)


if __name__ == '__main__':
    unittest.main()
