"""Real TLS PostgreSQL checks for the isolated Inventory bootstrap operation."""
import concurrent.futures
import json
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import uuid

ROOT = Path(__file__).resolve().parents[1]
PROJECT = 'oms-bootstrap-' + uuid.uuid4().hex[:10]
WORK = ROOT / '.ci-integration' / PROJECT
COMPOSE = ['docker', 'compose', '-p', PROJECT, '-f', str(WORK / 'compose.json')]


def run(*args, input=None, check=True):
    result = subprocess.run(args, cwd=ROOT, input=input, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=900)
    if check and result.returncode:
        # Container output is deliberately withheld: SQL errors can contain credentials.
        raise AssertionError('Command failed: ' + ' '.join(args[:3]))
    return result


def dc(*args, **kwargs):
    return run(*COMPOSE, *args, **kwargs)


def sql(statement, database='postgres'):
    return dc('exec', '-T', 'db', 'psql', '-U', 'bootstrap_admin', '-d', database,
              '-v', 'ON_ERROR_STOP=1', '-At', input=statement).stdout.strip()


def bootstrap(success=True, **overrides):
    args = ['run', '--rm', '--no-deps']
    for name, value in overrides.items():
        args += ['-e', name + '=' + value]
    result = dc(*args, 'bootstrap', check=False)
    assert (result.returncode == 0) == success, 'Unexpected bootstrap outcome'


def main():
    WORK.mkdir(parents=True)
    openssl = shutil.which('openssl') or 'C:/Program Files/Git/usr/bin/openssl.exe'
    run(openssl, 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
        '-subj', '/CN=inventory-db', '-addext', 'subjectAltName=DNS:inventory-db',
        '-keyout', str(WORK / 'server.key'), '-out', str(WORK / 'server.crt'))
    (WORK / 'Dockerfile').write_text('''FROM postgres:18
COPY --chown=postgres:postgres server.crt server.key /etc/postgresql/
RUN chmod 600 /etc/postgresql/server.key && chmod 644 /etc/postgresql/server.crt
''')
    config = {'services': {
        'db': {'build': {'context': str(WORK)},
               'environment': {'POSTGRES_USER': 'bootstrap_admin', 'POSTGRES_PASSWORD': secrets.token_urlsafe(36), 'POSTGRES_DB': 'postgres'},
               'command': ['postgres', '-c', 'ssl=on', '-c', 'ssl_cert_file=/etc/postgresql/server.crt', '-c', 'ssl_key_file=/etc/postgresql/server.key'],
               'networks': {'default': {'aliases': ['inventory-db']}},
               'healthcheck': {'test': ['CMD-SHELL', 'pg_isready -U bootstrap_admin -d postgres'], 'interval': '2s', 'timeout': '2s', 'retries': 30}},
        'bootstrap': {'build': {'context': str(ROOT), 'dockerfile': 'src/OrderManagement.DatabaseSetup/Dockerfile'},
                      'environment': {'PGHOST': 'inventory-db', 'PGDATABASE': 'postgres', 'PGUSER': 'bootstrap_admin',
                                      'PGAPPUSER': 'inventory_app', 'PGAPPPASSWORD': secrets.token_urlsafe(36),
                                      'PGSSLROOTCERT': '/run/test-ca/server.crt', 'DB_SETUP_OPERATION': 'initialize-inventory'},
                      'volumes': [str(WORK / 'server.crt').replace('\\', '/') + ':/run/test-ca/server.crt:ro']}}}
    config['services']['bootstrap']['environment']['PGPASSWORD'] = config['services']['db']['environment']['POSTGRES_PASSWORD']
    (WORK / 'compose.json').write_text(json.dumps(config))
    try:
        dc('build')
        dc('up', '-d', '--wait', 'db')
        # Exercise the non-superuser CREATEROLE/CREATEDB path used by RDS.
        sql('CREATE ROLE setup_operator LOGIN CREATEDB CREATEROLE;')
        password = config['services']['bootstrap']['environment']['PGPASSWORD']
        sql("ALTER ROLE setup_operator PASSWORD '" + password + "';")
        def setup(success=True, **overrides):
            bootstrap(success, PGUSER='setup_operator', **overrides)
        sql('CREATE DATABASE ordermanagement; CREATE ROLE order_app LOGIN;')
        sql('CREATE TABLE sentinel(id int PRIMARY KEY); INSERT INTO sentinel VALUES (42);', 'ordermanagement')
        setup()
        assert sql("SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname='inventory'") == 'inventory_app'
        sql('SET ROLE inventory_app; CREATE TABLE sentinel(id int); INSERT INTO sentinel VALUES (7);', 'inventory')
        setup()
        assert sql('SELECT id FROM sentinel', 'inventory') == '7'
        assert sql('SELECT id FROM sentinel', 'ordermanagement') == '42'
        assert sql("SELECT has_database_privilege('order_app','inventory','CONNECT')") == 'f'
        denied = dc('exec', '-T', 'db', 'psql', '-U', 'bootstrap_admin', '-d', 'ordermanagement', '-v', 'ON_ERROR_STOP=1',
                    input='SET ROLE inventory_app; SELECT * FROM sentinel;', check=False)
        assert denied.returncode != 0
        setup(False, PGAPPUSER='order_app')
        setup(False, PGDATABASE='ordermanagement')
        setup(False, PGAPPPASSWORD='short')
        setup(False, PGSSLROOTCERT='/missing.pem')
        setup(False, PGHOST='db')  # Resolves, but mismatches the certificate SAN.
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            list(pool.map(lambda _: setup(), range(2)))
        sql("COMMENT ON ROLE inventory_app IS 'foreign';")
        setup(False)
        assert sql("SELECT shobj_description(oid,'pg_authid') FROM pg_roles WHERE rolname='inventory_app'") == 'foreign'
        sql("COMMENT ON ROLE inventory_app IS 'ManagedBy=order-management-system;Service=inventory'; ALTER DATABASE inventory OWNER TO bootstrap_admin;")
        setup(False)
        assert sql("SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname='inventory'") == 'bootstrap_admin'
        print('PASS: TLS, restricted operator, idempotency, isolation, concurrency, configuration and takeover protection.')
    finally:
        dc('down', '--volumes', '--remove-orphans', '--rmi', 'local', check=False)
        shutil.rmtree(WORK)


if __name__ == '__main__':
    main()
