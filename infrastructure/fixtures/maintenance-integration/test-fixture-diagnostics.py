#!/usr/bin/env python3
"""Exercise the actual pure diagnostic function, without systemd, root or SQL."""

import argparse
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parent
SOURCE = (ROOT / "run-integration-fixture.sh").read_text(encoding="utf-8")
BEGIN = "# BEGIN pure fixture diagnostics\n"
END = "# END pure fixture diagnostics"
assert SOURCE.count(BEGIN) == 1 and SOURCE.count(END) == 1
FUNCTION = SOURCE.split(BEGIN, 1)[1].split(END, 1)[0]
RUN_FIXTURE = "run_fixture() {" + SOURCE.split("run_fixture() {", 1)[1].split("\nstart_fault() {", 1)[0]
ERROR_TRAP = next(line for line in SOURCE.splitlines() if line.startswith("trap '") and line.endswith(" ERR"))
BASH = ""


class FixtureDiagnosticsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="gb-fixture-diagnostics-", dir=ROOT)
        self.directory = Path(self.temp.name).resolve()
        self.assertTrue(self.directory.is_relative_to(ROOT))

    def tearDown(self):
        self.assertTrue(self.directory.is_relative_to(ROOT))
        self.temp.cleanup()

    def run_bash(self, body, *arguments):
        script = self.directory / "diagnostic-harness.sh"
        script.write_text("set -Eeuo pipefail\n" + FUNCTION + "\n" + body + "\n", encoding="utf-8", newline="\n")
        return subprocess.run([BASH, script.as_posix(), *(str(arg).replace("\\", "/") for arg in arguments)],
                              text=True, capture_output=True, check=False)

    def test_logs_reach_stderr_instead_of_dev_null(self):
        log = self.directory / "commands.log"
        log.write_text("fixtureCode=maintenance_recovery_outcome_mismatch\n", encoding="utf-8")
        result = self.run_bash('print_fixture_diagnostics "$1"', log)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout, "")
        self.assertIn("integrationDiagnosticLog=commands.log", result.stderr)
        self.assertIn("fixtureCode=maintenance_recovery_outcome_mismatch", result.stderr)

    def test_log_output_is_bounded_to_sixty_lines(self):
        log = self.directory / "commands.log"
        log.write_text("".join(f"entry-{index:03d}\n" for index in range(100)), encoding="utf-8")
        result = self.run_bash('print_fixture_diagnostics "$1"', log)
        self.assertEqual(result.returncode, 0, result.stderr)
        lines = [line for line in result.stderr.splitlines() if line.startswith("entry-")]
        self.assertEqual(lines, [f"entry-{index:03d}" for index in range(40, 100)])

    def test_missing_files_and_directories_are_reported_without_masking_failure(self):
        missing = self.directory / "missing.log"
        directory = self.directory / "not-a-log"
        directory.mkdir()
        result = self.run_bash('print_fixture_diagnostics "$1" "$2"', missing, directory)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("integrationDiagnosticLogMissing=missing.log", result.stderr)
        self.assertIn("integrationDiagnosticLogMissing=not-a-log", result.stderr)

    def test_cleanup_diagnostic_does_not_replace_original_exit_status(self):
        log = self.directory / "commands.log"
        log.write_text("synthetic original failure\n", encoding="utf-8")
        result = self.run_bash('''
log=$1
cleanup() { local original=$?; trap - EXIT; print_fixture_diagnostics "$log"; exit "$original"; }
trap cleanup EXIT
exit 37
''', log)
        self.assertEqual(result.returncode, 37)
        self.assertIn("synthetic original failure", result.stderr)

    def test_error_trap_names_scenario_file_line_and_status(self):
        result = self.run_bash(ERROR_TRAP + "\nscenario=after-receipt\nfalse")
        self.assertEqual(result.returncode, 1)
        self.assertRegex(result.stderr, r"integrationError=exit:1 source:diagnostic-harness.sh line:[0-9]+ scenario:after-receipt")

    def test_command_progress_preserves_the_real_failure_exit_status(self):
        result = self.run_bash(RUN_FIXTURE + '''
counter=0
prefix=synthetic-fixture
units=()
properties=()
state=$1
fixture=synthetic-executable
scenario=after-receipt
systemd-run() { return 23; }
if run_fixture annual-recover; then exit 99; else code=$?; fi
[[ "$code" == 23 && "$counter" == 1 && ${#units[@]} == 1 ]]
exit "$code"
''', self.directory)
        self.assertEqual(result.returncode, 23, result.stderr)
        self.assertIn("integrationCommandStart=annual-recover scenario=after-receipt", result.stdout)
        self.assertIn("integrationCommandExit=annual-recover status=23", result.stdout)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bash", default=shutil.which("bash"), help="Bash executable; use Git Bash on Windows.")
    args = parser.parse_args()
    if not args.bash:
        parser.error("Bash is required; missing prerequisites are not a skipped test.")
    BASH = args.bash
    unittest.main(argv=[__file__], verbosity=2)
