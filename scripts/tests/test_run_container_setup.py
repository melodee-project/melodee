"""Tests for secure container setup environment-file creation."""

import contextlib
import importlib.util
import io
import os
import stat
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

SCRIPT_DIRECTORY = Path(__file__).resolve().parents[1]
if str(SCRIPT_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIRECTORY))
SCRIPT_PATH = SCRIPT_DIRECTORY / "run-container-setup.py"
MODULE_SPEC = importlib.util.spec_from_file_location(
    "run_container_setup",
    SCRIPT_PATH,
)
if MODULE_SPEC is None or MODULE_SPEC.loader is None:
    raise RuntimeError(f"Unable to load setup script from {SCRIPT_PATH}")
RUN_CONTAINER_SETUP = importlib.util.module_from_spec(MODULE_SPEC)
MODULE_SPEC.loader.exec_module(RUN_CONTAINER_SETUP)


class CreateEnvironmentFileTests(unittest.TestCase):
    """Verify secure environment-file handling in container setup."""

    def test_create_generates_private_secrets_without_logging_them(self):
        """Generated secrets are stored privately and omitted from output."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            output = io.StringIO()

            with mock.patch.object(
                RUN_CONTAINER_SETUP.secrets,
                "token_urlsafe",
                side_effect=["generated-db-secret", "generated-auth-secret"],
            ), contextlib.redirect_stdout(output):
                result = RUN_CONTAINER_SETUP.create_env_file(project_directory)

            env_path = project_directory / ".env"
            env_content = env_path.read_text(encoding="utf-8")

            self.assertTrue(result)
            self.assertIn("DB_PASSWORD=generated-db-secret\n", env_content)
            self.assertIn(
                "MELODEE_AUTH_TOKEN=generated-auth-secret\n",
                env_content,
            )
            self.assertNotIn("generated-db-secret", output.getvalue())
            self.assertNotIn("generated-auth-secret", output.getvalue())
            if os.name == "posix":
                self.assertEqual(
                    stat.S_IRUSR | stat.S_IWUSR,
                    stat.S_IMODE(env_path.stat().st_mode),
                )

    def test_create_preserves_an_existing_environment_file(self):
        """Declining replacement leaves an existing deployment untouched."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            env_path = project_directory / ".env"
            env_path.write_text("existing configuration\n", encoding="utf-8")

            with mock.patch(
                "builtins.input", return_value="n"
            ), contextlib.redirect_stdout(io.StringIO()):
                result = RUN_CONTAINER_SETUP.create_env_file(project_directory)

            self.assertTrue(result)
            self.assertEqual(
                "existing configuration\n",
                env_path.read_text(encoding="utf-8"),
            )

    @unittest.skipUnless(os.name == "posix", "POSIX file modes required")
    def test_create_tightens_a_legacy_environment_file_to_mode_0600(self):
        """Declining overwrite restricts a legacy file without rewriting it."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            env_path = project_directory / ".env"
            original_content = "DB_PASSWORD=existing-secret\n"
            env_path.write_text(original_content, encoding="utf-8")
            env_path.chmod(0o644)
            original_inode = env_path.stat().st_ino
            output = io.StringIO()

            with mock.patch(
                "builtins.input", return_value="n"
            ), contextlib.redirect_stdout(output):
                result = RUN_CONTAINER_SETUP.create_env_file(project_directory)

            self.assertTrue(result)
            self.assertEqual(original_content, env_path.read_text(encoding="utf-8"))
            self.assertEqual(original_inode, env_path.stat().st_ino)
            self.assertEqual(0o600, stat.S_IMODE(env_path.stat().st_mode))
            self.assertNotIn("existing-secret", output.getvalue())

    def test_create_rejects_an_existing_environment_directory(self):
        """A non-regular .env entry is rejected without prompting."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            env_path = project_directory / ".env"
            env_path.mkdir()

            with mock.patch("builtins.input") as input_mock, contextlib.redirect_stderr(
                io.StringIO()
            ):
                result = RUN_CONTAINER_SETUP.create_env_file(project_directory)

            self.assertFalse(result)
            input_mock.assert_not_called()
            self.assertTrue(env_path.is_dir())

    def test_create_securely_replaces_a_regular_file_when_confirmed(self):
        """An explicit replacement installs a fresh private regular file."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            env_path = project_directory / ".env"
            env_path.write_text("existing configuration\n", encoding="utf-8")

            with mock.patch("builtins.input", return_value="y"), mock.patch.object(
                RUN_CONTAINER_SETUP.secrets,
                "token_urlsafe",
                side_effect=["new-db-secret", "new-auth-secret"],
            ), contextlib.redirect_stdout(io.StringIO()):
                result = RUN_CONTAINER_SETUP.create_env_file(project_directory)

            env_content = env_path.read_text(encoding="utf-8")
            self.assertTrue(result)
            self.assertIn("DB_PASSWORD=new-db-secret\n", env_content)
            self.assertIn("MELODEE_AUTH_TOKEN=new-auth-secret\n", env_content)
            if os.name == "posix":
                self.assertEqual(
                    stat.S_IRUSR | stat.S_IWUSR,
                    stat.S_IMODE(env_path.stat().st_mode),
                )

    def test_force_securely_replaces_a_regular_file_without_prompting(self):
        """The force path replaces only a regular file and never prompts."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            env_path = project_directory / ".env"
            env_path.write_text("existing configuration\n", encoding="utf-8")

            with mock.patch("builtins.input") as input_mock, mock.patch.object(
                RUN_CONTAINER_SETUP.secrets,
                "token_urlsafe",
                side_effect=["forced-db-secret", "forced-auth-secret"],
            ), contextlib.redirect_stdout(io.StringIO()):
                result = RUN_CONTAINER_SETUP.create_env_file(
                    project_directory,
                    overwrite=True,
                )

            env_content = env_path.read_text(encoding="utf-8")
            self.assertTrue(result)
            input_mock.assert_not_called()
            self.assertIn("DB_PASSWORD=forced-db-secret\n", env_content)
            self.assertIn("MELODEE_AUTH_TOKEN=forced-auth-secret\n", env_content)

    @unittest.skipUnless(os.name == "posix", "POSIX symlink semantics required")
    def test_create_rejects_a_dangling_environment_symlink(self):
        """Container setup never follows or replaces a dangling .env link."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            symlink_target = project_directory / "outside.env"
            env_path = project_directory / ".env"
            env_path.symlink_to(symlink_target)

            with mock.patch("builtins.input") as input_mock, contextlib.redirect_stderr(
                io.StringIO()
            ):
                result = RUN_CONTAINER_SETUP.create_env_file(project_directory)

            self.assertFalse(result)
            input_mock.assert_not_called()
            self.assertTrue(env_path.is_symlink())
            self.assertFalse(symlink_target.exists())


if __name__ == "__main__":
    unittest.main()
