import importlib.util
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest

SCRIPT = pathlib.Path(__file__).resolve().parents[1] / "release_version.py"
SPEC = importlib.util.spec_from_file_location("release_version", SCRIPT)
versioning = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(versioning)


class ReleaseVersionTests(unittest.TestCase):
    def test_author_version_and_letter_are_user_facing_label_and_tag(self):
        result = versioning.derive_version("3.4.6", "a", requested="3.4.6a")
        self.assertEqual(result, {"label": "3.4.6a", "version": "3.4.6-a",
                                  "assembly": "3.4.6", "tag": "3.4.6a"})

    def test_next_letter_retains_author_version(self):
        self.assertEqual(versioning.derive_version("3.4.6", "b", requested="3.4.6b"),
                         {"label": "3.4.6b", "version": "3.4.6-b",
                          "assembly": "3.4.6", "tag": "3.4.6b"})

    def test_beta_author_release_preserves_prerelease_and_fork_letter(self):
        result = versioning.derive_version("3.5.1-beta2", "a", requested="3.5.1-beta2a")
        self.assertEqual(result, {"label": "3.5.1-beta2a", "version": "3.5.1-beta2.a",
                                  "assembly": "3.5.1", "tag": "3.5.1-beta2a"})

    def test_dotted_prerelease_keeps_all_author_identifiers(self):
        self.assertEqual(versioning.derive_version("3.5.1-rc.2", "b"),
                         {"label": "3.5.1-rc.2b", "version": "3.5.1-rc.2.b",
                          "assembly": "3.5.1", "tag": "3.5.1-rc.2b"})

    def test_beta_ci_build_keeps_prerelease_and_numeric_assembly(self):
        self.assertEqual(versioning.derive_version("3.5.1-beta2", "a", ci_run="42"),
                         {"label": "3.5.1-beta2a-ci.42", "version": "3.5.1-beta2.a.ci.42",
                          "assembly": "3.5.1", "tag": ""})

    def test_beta_release_rejects_malformed_or_contradictory_requested_label(self):
        for requested in ("3.5.1a", "3.5.1-beta1a", "3.5.1-beta2b", "v3.5.1-beta2a",
                          "3.5.1-beta2.a", "3.5.1-beta2a+metadata", "3.5.1-beta2a\n", ""):
            with self.subTest(requested=requested), self.assertRaisesRegex(ValueError, "reviewed release version"):
                versioning.derive_version("3.5.1-beta2", "a", requested=requested)

    def test_cli_writes_beta_release_metadata_for_workflow_consumers(self):
        with tempfile.TemporaryDirectory() as directory:
            config = pathlib.Path(directory) / "fork-version.json"
            output = pathlib.Path(directory) / "github-output"
            config.write_text(json.dumps({"upstream_release": "3.5.1-beta2", "revision": "a"}), encoding="utf-8")
            result = subprocess.run([sys.executable, str(SCRIPT), "--config", str(config),
                                     "--requested", "3.5.1-beta2a", "--github-output", str(output)],
                                    capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertEqual(json.loads(result.stdout),
                             {"label": "3.5.1-beta2a", "version": "3.5.1-beta2.a",
                              "assembly": "3.5.1", "tag": "3.5.1-beta2a"})
            self.assertEqual(output.read_text(encoding="utf-8"),
                             "label=3.5.1-beta2a\nversion=3.5.1-beta2.a\nassembly=3.5.1\ntag=3.5.1-beta2a\n")

    def test_cli_rejects_contradictory_label_before_writing_workflow_metadata(self):
        with tempfile.TemporaryDirectory() as directory:
            config = pathlib.Path(directory) / "fork-version.json"
            output = pathlib.Path(directory) / "github-output"
            config.write_text(json.dumps({"upstream_release": "3.5.1-beta2", "revision": "a"}), encoding="utf-8")
            result = subprocess.run([sys.executable, str(SCRIPT), "--config", str(config),
                                     "--requested", "3.5.1-beta2b", "--github-output", str(output)],
                                    capture_output=True, text=True)
            self.assertEqual(result.returncode, 2)
            self.assertIn("reviewed release version is 3.5.1-beta2a", result.stderr)
            self.assertEqual(result.stdout, "")
            self.assertFalse(output.exists())

    def test_new_author_release_can_restart_at_a(self):
        self.assertEqual(versioning.derive_version("3.4.7", "a")["label"], "3.4.7a")

    def test_ci_build_is_identifiable_without_reserving_a_release_tag(self):
        result = versioning.derive_version("3.4.6", "a", ci_run="42")
        self.assertEqual(result["label"], "3.4.6a-ci.42")
        self.assertEqual(result["version"], "3.4.6-a.ci.42")
        self.assertEqual(result["assembly"], "3.4.6")
        self.assertEqual(result["tag"], "")

    def test_request_cannot_publish_an_unreviewed_author_version_or_letter(self):
        for requested in ("3.4.7a", "3.4.6b", "v3.4.6a", "3.4.6-a", "3.4.6a\nassembly=9.9.9", ""):
            with self.subTest(requested=requested), self.assertRaises(ValueError):
                versioning.derive_version("3.4.6", "a", requested=requested)

    def test_invalid_author_versions_fail(self):
        for upstream in ("3.4", "v3.4.6", "03.4.6", "3.4.6\n", "3.5.1-", "3.5.1-beta..2",
                         "3.5.1-01", "3.5.1-beta.02", "3.5.1-beta_2", "3.5.1-beta2\n",
                         "3.5.1+build", "3.5.1-beta2+build"):
            with self.subTest(upstream=upstream), self.assertRaises(ValueError):
                versioning.derive_version(upstream, "a")

    def test_revision_is_one_lowercase_letter(self):
        for revision in ("", "aa", "A", "1", "a\n"):
            with self.subTest(revision=revision), self.assertRaises(ValueError):
                versioning.derive_version("3.4.6", revision)

    def test_invalid_ci_runs_fail(self):
        for run in ("0", "-1", "1.2", "x", "1\n"):
            with self.subTest(run=run), self.assertRaises(ValueError):
                versioning.derive_version("3.4.6", "a", ci_run=run)

    def test_release_and_ci_modes_cannot_be_mixed(self):
        with self.assertRaises(ValueError):
            versioning.derive_version("3.4.6", "a", requested="3.4.6a", ci_run="42")


if __name__ == "__main__":
    unittest.main()
