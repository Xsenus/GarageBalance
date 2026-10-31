"""Exercise deployment identity and installed-release checks without a network."""
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
BASH = (r"C:\Program Files\Git\usr\bin\bash.exe" if os.name == "nt" else shutil.which("bash"))


def shell_path(path):
    value = str(path).replace("\\", "/")
    return "/" + value[0].lower() + value[2:] if os.name == "nt" else value


class StagingTargetTests(unittest.TestCase):
    def run_guard(self, address, name):
        with tempfile.TemporaryDirectory(prefix="garagebalance-target-") as directory:
            folder = Path(directory)
            commands = {
                "getent": '#!/usr/bin/env bash\nprintf "%s STREAM target\\n" "$TEST_ADDRESS"\n',
                "hostname": '#!/usr/bin/env bash\nprintf "%s\\n" "$TEST_NAME"\n',
                "ssh": '#!/usr/bin/env bash\ntouch "$TEST_SSH_MARKER"\nbash -c "${@: -1}"\n',
            }
            for command, contents in commands.items():
                path = folder / command
                path.write_text(contents, encoding="utf-8", newline="\n")
                path.chmod(0o700)
            marker = folder / "ssh-called"
            environment = os.environ.copy()
            environment.update(VPS_HOST="configured-private-host", TEST_ADDRESS=address, TEST_NAME=name,
                               TEST_SSH_MARKER=shell_path(marker), SSH_CONNECTION=f"192.0.2.1 12345 {address} 22")
            result = subprocess.run([BASH, "-c", 'export PATH="$1:/usr/bin:$PATH"; /usr/bin/bash "$2"', "guard-test",
                                     shell_path(folder), shell_path(ROOT / "infrastructure/scripts/verify-staging-target.sh")],
                                    env=environment, text=True, capture_output=True, timeout=20, check=False)
            return result, marker.exists()

    def test_expected_identity_succeeds(self):
        result, connected = self.run_guard("31.192.110.221", "41765")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(connected)
        self.assertIn("Confirmed staging target", result.stdout)
        self.assertNotIn("configured-private-host", result.stdout + result.stderr)

    def test_wrong_address_stops_before_ssh(self):
        result, connected = self.run_guard("192.0.2.9", "41765")
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(connected)

    def test_wrong_machine_name_stops(self):
        result, connected = self.run_guard("31.192.110.221", "AnotherProject")
        self.assertNotEqual(result.returncode, 0)
        self.assertTrue(connected)


class InstalledReleaseTests(unittest.TestCase):
    def run_check(self, **overrides):
        workflow = (ROOT / ".github/workflows/deploy-staging.yml").read_text(encoding="utf-8")
        start = workflow.index("          EXPECTED_VERSION=")
        end = workflow.index("          ssh -O exit", start)
        block = "set -euo pipefail\n" + "\n".join(line[10:] for line in workflow[start:end].splitlines()) + "\n"
        with tempfile.TemporaryDirectory(prefix="garagebalance-installed-") as directory:
            folder = Path(directory)
            artifacts = folder / "artifacts"
            artifacts.mkdir()
            api = b"synthetic-api"
            frontend = b"synthetic-index"
            for archive_name, files in {
                "api.tar.gz": {"./AppReleases/releases.json": json.dumps([{"version": "9.8.7"}]).encode(), "./GarageBalance.Api.dll": api},
                "frontend.tar.gz": {"./index.html": frontend},
            }.items():
                with tarfile.open(artifacts / archive_name, "w:gz") as archive:
                    for name, content in files.items():
                        entry = tarfile.TarInfo(name)
                        entry.size = len(content)
                        archive.addfile(entry, io.BytesIO(content))
            commands = {
                "python3": '#!/usr/bin/env bash\nexec "$TEST_PYTHON" "$@"\n',
                "ssh": '''#!/usr/bin/env bash
case "$*" in
  *"python3 -c"*) printf '%s\n' "$TEST_VERSION" ;;
  *"sha256sum /opt/garagebalance-staging/api/"*) printf '%s  api\n' "$TEST_API_HASH" ;;
  *"sha256sum /opt/garagebalance-staging/frontend/"*) printf '%s  frontend\n' "$TEST_FRONTEND_HASH" ;;
  *"systemctl is-active"*) exit "$TEST_SERVICE_EXIT" ;;
  *) exit 91 ;;
esac
''',
            }
            for name, contents in commands.items():
                path = folder / name
                path.write_text(contents, encoding="utf-8", newline="\n")
                path.chmod(0o700)
            script = folder / "verify-installed.sh"
            script.write_text(block, encoding="utf-8", newline="\n")
            environment = os.environ.copy()
            environment.update(TEST_PYTHON=shell_path(Path(sys.executable)), TEST_VERSION="9.8.7",
                               TEST_API_HASH=hashlib.sha256(api).hexdigest(), TEST_FRONTEND_HASH=hashlib.sha256(frontend).hexdigest(),
                               TEST_SERVICE_EXIT="0", GITHUB_SHA="a" * 40)
            environment.update(overrides)
            return subprocess.run([BASH, "-c", 'export PATH="$1:/usr/bin:$PATH"; /usr/bin/bash "$2"', "release-test",
                                   shell_path(folder), shell_path(script)], cwd=folder, env=environment,
                                  text=True, capture_output=True, timeout=20, check=False)

    def test_matching_version_payloads_and_running_service_succeed(self):
        result = self.run_check()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("installedReleaseStatus=verified; commit=" + "a" * 40 + "; version=9.8.7", result.stdout)

    def test_mismatched_version_payloads_or_stopped_service_fail(self):
        for override in ({"TEST_VERSION": "0.0.0"}, {"TEST_API_HASH": "0" * 64},
                         {"TEST_FRONTEND_HASH": "0" * 64}, {"TEST_SERVICE_EXIT": "3"}):
            with self.subTest(override=override):
                result = self.run_check(**override)
                self.assertNotEqual(result.returncode, 0)
                self.assertNotIn("installedReleaseStatus=verified", result.stdout)


if __name__ == "__main__":
    unittest.main()
