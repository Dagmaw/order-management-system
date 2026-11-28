"""Exercise transient/permanent Maven bootstrap failures without network access."""
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class MavenBootstrapTests(unittest.TestCase):
    def scenario(self, mode, code, attempts, delays):
        with tempfile.TemporaryDirectory(prefix='oms-maven-bootstrap-') as temp:
            work = Path(temp)
            (work / 'bin').mkdir()
            (work / 'bootstrap-maven.sh').write_text((ROOT / 'services/inventory-service/bootstrap-maven.sh').read_text())
            (work / 'mvnw').write_text('''#!/bin/sh
[ "$1" = "--version" ] || exit 91
n=0
[ ! -f attempts ] || n=$(cat attempts)
n=$((n + 1))
printf '%s' "$n" > attempts
case "$TEST_MODE" in
 permanent) echo 'Checksum verification failed'; exit 3 ;;
 always429) echo 'Server returned HTTP response code: 429'; exit 1 ;;
 recover) if [ "$n" -lt 3 ]; then echo 'Server returned HTTP response code: 429'; exit 1; fi ;;
esac
echo 'Maven bootstrap ready'
''')
            (work / 'bin/sleep').write_text('#!/bin/sh\necho "$1" >> /fixture/delays\n')
            # Keep the shell fixture portable when tests run from Windows.
            for item in work.rglob('*'):
                if item.is_file():
                    item.write_bytes(item.read_text().encode('utf-8'))
            result = subprocess.run(['docker', 'run', '--rm', '--network', 'none', '-e', 'TEST_MODE=' + mode,
                                     '-v', work.as_posix() + ':/fixture', '-w', '/fixture',
                                     '--entrypoint', 'sh', 'eclipse-temurin:25-jdk', '-c',
                                     'chmod +x bin/sleep && PATH=/fixture/bin:$PATH sh ./bootstrap-maven.sh'],
                                    text=True, capture_output=True, timeout=30)
            self.assertEqual(code, result.returncode, result.stdout + result.stderr)
            self.assertEqual(str(attempts), (work / 'attempts').read_text())
            actual = (work / 'delays').read_text().splitlines() if (work / 'delays').exists() else []
            self.assertEqual(delays, actual)

    def test_rate_limit_recovers_with_bounded_delays(self):
        self.scenario('recover', 0, 3, ['10', '20'])

    def test_permanent_failure_is_not_retried(self):
        self.scenario('permanent', 3, 1, [])

    def test_rate_limit_stops_after_four_attempts(self):
        self.scenario('always429', 1, 4, ['10', '20', '40'])

    def test_cached_bootstrap_runs_once(self):
        self.scenario('ready', 0, 1, [])


if __name__ == '__main__':
    unittest.main()
