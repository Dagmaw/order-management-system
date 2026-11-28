"""Offline checks of target/identity guards and the container loopback relay."""
import pathlib, subprocess, tempfile, socket, time, uuid
ROOT = pathlib.Path(__file__).resolve().parents[1]
IMAGE = "oms-inventory-ssm-client:local"
def run(*args, **kw):
    return subprocess.run(args, text=True, capture_output=True, **kw)
r = run("docker", "run", "--rm", "-e", "TUNNEL_INSTANCE_ID=bad", IMAGE)
assert r.returncode == 2 and "Invalid helper" in r.stderr
print("PASS invalid target rejected")
with tempfile.TemporaryDirectory(prefix="oms-tunnel-test-") as temporary:
    fixture = pathlib.Path(temporary)
    fake = fixture / "aws"
    fake.write_text('#!/bin/sh\nif [ "$1" = sts ]; then echo denied; exit 0; fi\nexit 99\n', encoding="utf-8", newline="\n")
    fake.chmod(0o755)
    mounts = ["-v", str(fixture)+":/fixture:ro", "-e", "PATH=/fixture:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin", "-e", "TUNNEL_INSTANCE_ID=i-0123456789abcdef0"]
    # Invoke sh explicitly so Windows bind-mount executable bits are irrelevant.
    entry = 'chmod +x /fixture/aws 2>/dev/null || true; /bin/sh /usr/local/bin/start-tunnel.sh'
    r = run("docker", "run", "--rm", *mounts, "--entrypoint", "sh", IMAGE, "-c", entry)
    assert r.returncode == 2 and "dedicated Inventory tunnel operator" in r.stderr, r.stderr
    print("PASS wrong identity rejected")
    fake.write_text('#!/bin/sh\nif [ "$1" = sts ]; then echo arn:aws:sts::638500336727:assumed-role/order-management-dev-inventory-tunnel-operator/test; exit 0; fi\n[ "$1 $2" = "ssm start-session" ] || exit 98\nexec socat TCP4-LISTEN:8443,bind=127.0.0.1,reuseaddr,fork EXEC:/bin/cat\n', encoding="utf-8", newline="\n")
    name = "oms-tunnel-test-"+uuid.uuid4().hex[:10]
    try:
        r = run("docker", "run", "--rm", "-d", "--name", name, "-p", "127.0.0.1::9443", *mounts, "--entrypoint", "sh", IMAGE, "-c", entry)
        assert r.returncode == 0, r.stderr
        port = run("docker", "port", name, "9443/tcp").stdout.strip()
        assert port.startswith("127.0.0.1:"), port
        for attempt in range(20):
            try:
                with socket.create_connection(("127.0.0.1", int(port.rsplit(":",1)[1])), timeout=2) as connection:
                    connection.sendall(b"private-inventory-relay")
                    assert connection.recv(128) == b"private-inventory-relay"
                    break
            except (OSError, AssertionError):
                if attempt == 19: raise
                time.sleep(.3)
        print("PASS loopback publication relays to SSM local port")
    finally:
        run("docker", "stop", "--time", "2", name)
print("All offline tunnel checks passed; no AWS sessions or stock mutations were used.")
