"""Tests for the unattended Melodee setup script."""

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
SCRIPT_PATH = SCRIPT_DIRECTORY / "setup_melodee.py"
MODULE_SPEC = importlib.util.spec_from_file_location(
    "setup_melodee",
    SCRIPT_PATH,
)
if MODULE_SPEC is None or MODULE_SPEC.loader is None:
    raise RuntimeError(f"Unable to load setup script from {SCRIPT_PATH}")
SETUP_MELODEE = importlib.util.module_from_spec(MODULE_SPEC)
MODULE_SPEC.loader.exec_module(SETUP_MELODEE)

EXAMPLE_ENV = """# Melodee Docker Configuration
DB_PASSWORD=replace_database_password
DB_MIN_POOL_SIZE=10
MELODEE_AUTH_TOKEN=replace_auth_token
MELODEE_PORT=8080
"""


class SetupEnvironmentConfigTests(unittest.TestCase):
    """Verify secure, unattended environment-file creation."""

    def test_setup_generates_required_secrets_without_logging_them(self):
        """Generated secrets are persisted privately but never printed."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            (project_directory / "example.env").write_text(
                EXAMPLE_ENV,
                encoding="utf-8",
            )
            output = io.StringIO()

            with mock.patch.object(
                SETUP_MELODEE.secrets,
                "token_urlsafe",
                side_effect=["generated-db-secret", "generated-auth-secret"],
            ), contextlib.redirect_stdout(output):
                result = SETUP_MELODEE.setup_environment_config(
                    temporary_directory,
                )

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
            if os.name != "nt":
                file_mode = stat.S_IMODE(env_path.stat().st_mode)
                self.assertEqual(SETUP_MELODEE.PRIVATE_FILE_MODE, file_mode)

    def test_setup_preserves_an_existing_environment_file(self):
        """An existing deployment configuration is never overwritten."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            env_path = project_directory / ".env"
            env_path.write_text("existing configuration\n", encoding="utf-8")

            with contextlib.redirect_stdout(io.StringIO()):
                result = SETUP_MELODEE.setup_environment_config(
                    temporary_directory,
                )

            self.assertTrue(result)
            self.assertEqual(
                "existing configuration\n",
                env_path.read_text(encoding="utf-8"),
            )

    @unittest.skipUnless(os.name == "posix", "POSIX file modes required")
    def test_setup_tightens_a_legacy_environment_file_to_mode_0600(self):
        """A preserved legacy .env is restricted without changing content."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            env_path = Path(temporary_directory) / ".env"
            original_content = "DB_PASSWORD=existing-secret\n"
            env_path.write_text(original_content, encoding="utf-8")
            env_path.chmod(0o644)
            original_inode = env_path.stat().st_ino
            output = io.StringIO()

            with contextlib.redirect_stdout(output):
                result = SETUP_MELODEE.setup_environment_config(
                    temporary_directory,
                )

            self.assertTrue(result)
            self.assertEqual(original_content, env_path.read_text(encoding="utf-8"))
            self.assertEqual(original_inode, env_path.stat().st_ino)
            self.assertEqual(0o600, stat.S_IMODE(env_path.stat().st_mode))
            self.assertNotIn("existing-secret", output.getvalue())

    def test_setup_rejects_an_existing_environment_directory(self):
        """A non-regular .env entry is rejected and left untouched."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            env_path = Path(temporary_directory) / ".env"
            env_path.mkdir()

            with contextlib.redirect_stdout(io.StringIO()):
                result = SETUP_MELODEE.setup_environment_config(
                    temporary_directory,
                )

            self.assertFalse(result)
            self.assertTrue(env_path.is_dir())

    @unittest.skipUnless(os.name == "posix", "POSIX symlink semantics required")
    def test_setup_rejects_a_dangling_environment_symlink(self):
        """The setup path rejects a dangling .env symlink without replacing it."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            symlink_target = project_directory / "outside.env"
            env_path = project_directory / ".env"
            env_path.symlink_to(symlink_target)

            with contextlib.redirect_stdout(io.StringIO()):
                result = SETUP_MELODEE.setup_environment_config(
                    temporary_directory,
                )

            self.assertFalse(result)
            self.assertTrue(env_path.is_symlink())
            self.assertFalse(symlink_target.exists())

    def test_setup_rejects_a_template_missing_a_required_secret(self):
        """A malformed template cannot produce an unusable deployment file."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            (project_directory / "example.env").write_text(
                "MELODEE_PORT=8080\n",
                encoding="utf-8",
            )

            with contextlib.redirect_stdout(io.StringIO()):
                result = SETUP_MELODEE.setup_environment_config(
                    temporary_directory,
                )

            self.assertFalse(result)
            self.assertFalse((project_directory / ".env").exists())

    def test_private_file_writer_refuses_to_overwrite_a_file(self):
        """Exclusive creation protects an existing configuration from races."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            env_path = Path(temporary_directory) / ".env"
            env_path.write_text("existing configuration\n", encoding="utf-8")

            with self.assertRaises(FileExistsError):
                SETUP_MELODEE.write_private_file(
                    str(env_path),
                    "replacement configuration\n",
                )

            self.assertEqual(
                "existing configuration\n",
                env_path.read_text(encoding="utf-8"),
            )

    @unittest.skipUnless(os.name == "posix", "POSIX symlink semantics required")
    def test_private_file_writer_refuses_a_dangling_symlink(self):
        """No-follow exclusive creation cannot write through a symlink."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            symlink_target = project_directory / "outside.env"
            env_path = project_directory / ".env"
            env_path.symlink_to(symlink_target)

            with self.assertRaises(OSError):
                SETUP_MELODEE.write_private_file(
                    str(env_path),
                    "generated configuration\n",
                )

            self.assertTrue(env_path.is_symlink())
            self.assertFalse(symlink_target.exists())


if __name__ == "__main__":
    unittest.main()
