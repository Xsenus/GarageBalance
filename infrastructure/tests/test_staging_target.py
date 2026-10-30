"""Exercise the deployment identity guard without a network or credentials."""
import os
from pathlib import Path
import shutil
import subprocess
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
        result, connected = self.run_guard("31.192.110.221", "QuestRoom")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(connected)
        self.assertIn("Confirmed staging target", result.stdout)
        self.assertNotIn("configured-private-host", result.stdout + result.stderr)

    def test_wrong_address_stops_before_ssh(self):
        result, connected = self.run_guard("192.0.2.9", "QuestRoom")
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(connected)

    def test_wrong_machine_name_stops(self):
        result, connected = self.run_guard("31.192.110.221", "AnotherProject")
        self.assertNotEqual(result.returncode, 0)
        self.assertTrue(connected)


if __name__ == "__main__":
    unittest.main()
