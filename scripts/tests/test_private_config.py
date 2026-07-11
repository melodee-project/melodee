"""Tests for shared secret-bearing configuration file helpers."""

import importlib.util
import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

HELPER_PATH = Path(__file__).resolve().parents[1] / "private_config.py"
MODULE_SPEC = importlib.util.spec_from_file_location(
    "private_config_under_test",
    HELPER_PATH,
)
if MODULE_SPEC is None or MODULE_SPEC.loader is None:
    raise RuntimeError(f"Unable to load private config helper from {HELPER_PATH}")
private_config = importlib.util.module_from_spec(MODULE_SPEC)
MODULE_SPEC.loader.exec_module(private_config)


class PrivateConfigTests(unittest.TestCase):
    """Verify atomic publication and failure cleanup."""

    def test_write_cleans_temporary_file_when_publication_fails(self):
        """A failed publication leaves no partial or temporary file."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            destination = project_directory / ".env"
            publication_method = "rename" if os.name == "nt" else "link"

            with mock.patch.object(
                private_config.os,
                publication_method,
                side_effect=OSError("simulated publication failure"),
            ), self.assertRaises(OSError):
                private_config.write_private_file(
                    destination,
                    "DB_PASSWORD=generated-secret\n",
                )

            self.assertFalse(destination.exists())
            self.assertEqual([], list(project_directory.iterdir()))

    def test_write_cleans_partial_content_when_sync_fails(self):
        """A write failure before publication removes the private temporary file."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            destination = project_directory / ".env"

            with mock.patch.object(
                private_config.os,
                "fsync",
                side_effect=OSError("simulated sync failure"),
            ), self.assertRaises(OSError):
                private_config.write_private_file(
                    destination,
                    "DB_PASSWORD=generated-secret\n",
                )

            self.assertFalse(destination.exists())
            self.assertEqual([], list(project_directory.iterdir()))

    @unittest.skipUnless(os.name == "posix", "POSIX fchmod required")
    def test_open_cleanup_closes_fd_and_removes_temp_when_fchmod_fails(self):
        """Post-open setup failure closes the descriptor and removes its path."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            destination = project_directory / ".env"
            opened_descriptors = []
            real_open = private_config.os.open

            def tracking_open(*args, **kwargs):
                file_descriptor = real_open(*args, **kwargs)
                opened_descriptors.append(file_descriptor)
                return file_descriptor

            with mock.patch.object(
                private_config.os,
                "open",
                side_effect=tracking_open,
            ), mock.patch.object(
                private_config.os,
                "fchmod",
                side_effect=OSError("simulated fchmod failure"),
            ), self.assertRaises(
                OSError
            ):
                private_config.write_private_file(
                    destination,
                    "DB_PASSWORD=generated-secret\n",
                )

            self.assertEqual(1, len(opened_descriptors))
            with self.assertRaises(OSError):
                private_config.os.fstat(opened_descriptors[0])
            self.assertEqual([], list(project_directory.iterdir()))

    @unittest.skipUnless(os.name == "posix", "POSIX fchmod required")
    def test_fchmod_happens_before_the_file_is_opened_for_text_writes(self):
        """Owner-only permissions are applied before any content write can occur."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            destination = Path(temporary_directory) / ".env"
            events = []
            real_fchmod = private_config.os.fchmod
            real_fdopen = private_config.os.fdopen

            def tracking_fchmod(*args, **kwargs):
                events.append("fchmod")
                return real_fchmod(*args, **kwargs)

            def tracking_fdopen(*args, **kwargs):
                events.append("fdopen")
                return real_fdopen(*args, **kwargs)

            with mock.patch.object(
                private_config.os,
                "fchmod",
                side_effect=tracking_fchmod,
            ), mock.patch.object(
                private_config.os,
                "fdopen",
                side_effect=tracking_fdopen,
            ):
                private_config.write_private_file(
                    destination,
                    "DB_PASSWORD=generated-secret\n",
                )

            self.assertLess(events.index("fchmod"), events.index("fdopen"))

    @unittest.skipUnless(os.name == "posix", "POSIX hard-link publication required")
    def test_destination_appearance_during_publication_is_preserved(self):
        """A raced-in destination blocks publication and is never overwritten."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            destination = project_directory / ".env"

            def race_destination(source, target, *, follow_symlinks):
                self.assertFalse(follow_symlinks)
                Path(target).write_text("raced destination\n", encoding="utf-8")
                raise FileExistsError(target)

            with mock.patch.object(
                private_config.os,
                "link",
                side_effect=race_destination,
            ), self.assertRaises(FileExistsError):
                private_config.write_private_file(
                    destination,
                    "DB_PASSWORD=generated-secret\n",
                )

            self.assertEqual(
                "raced destination\n",
                destination.read_text(encoding="utf-8"),
            )
            self.assertEqual([destination], list(project_directory.iterdir()))

    @unittest.skipUnless(os.name == "posix", "POSIX symlink semantics required")
    def test_temporary_source_swap_is_rejected_and_cleaned(self):
        """A swapped temporary path cannot publish a symbolic link."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            destination = project_directory / ".env"
            outside = project_directory / "outside.env"
            real_fsync = private_config.os.fsync

            def swap_temporary_path(file_descriptor):
                real_fsync(file_descriptor)
                temporary_paths = [
                    path
                    for path in project_directory.iterdir()
                    if path.name.endswith(".tmp")
                ]
                self.assertEqual(1, len(temporary_paths))
                temporary_paths[0].unlink()
                temporary_paths[0].symlink_to(outside)

            with mock.patch.object(
                private_config.os,
                "fsync",
                side_effect=swap_temporary_path,
            ), self.assertRaises(FileExistsError):
                private_config.write_private_file(
                    destination,
                    "DB_PASSWORD=generated-secret\n",
                )

            self.assertFalse(private_config.path_entry_exists(destination))
            self.assertFalse(outside.exists())
            self.assertEqual([], list(project_directory.iterdir()))

    def test_destination_swap_before_overwrite_is_rejected(self):
        """Overwrite aborts when the previously validated destination changes."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            destination = project_directory / ".env"
            replacement = project_directory / "replacement.env"
            destination.write_text("original content\n", encoding="utf-8")
            replacement.write_text("raced content\n", encoding="utf-8")
            real_fsync = private_config.os.fsync

            def swap_destination(file_descriptor):
                real_fsync(file_descriptor)
                private_config.os.replace(replacement, destination)

            with mock.patch.object(
                private_config.os,
                "fsync",
                side_effect=swap_destination,
            ), self.assertRaises(FileExistsError):
                private_config.write_private_file(
                    destination,
                    "replacement content\n",
                    overwrite=True,
                )

            self.assertEqual(
                "raced content\n",
                destination.read_text(encoding="utf-8"),
            )
            self.assertEqual([destination], list(project_directory.iterdir()))

    def test_replace_failure_preserves_existing_content(self):
        """A failed replacement leaves the original file untouched."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            destination = project_directory / ".env"
            destination.write_text("original content\n", encoding="utf-8")

            with mock.patch.object(
                private_config.os,
                "replace",
                side_effect=OSError("simulated replacement failure"),
            ), self.assertRaises(OSError):
                private_config.write_private_file(
                    destination,
                    "replacement content\n",
                    overwrite=True,
                )

            self.assertEqual(
                "original content\n",
                destination.read_text(encoding="utf-8"),
            )
            self.assertEqual([destination], list(project_directory.iterdir()))

    @unittest.skipUnless(os.name == "posix", "POSIX O_NOFOLLOW required")
    def test_existing_file_swap_before_open_is_rejected(self):
        """Descriptor validation rejects a raced-in regular file before chmod."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            project_directory = Path(temporary_directory)
            destination = project_directory / ".env"
            replacement = project_directory / "replacement.env"
            destination.write_text("original content\n", encoding="utf-8")
            replacement.write_text("raced content\n", encoding="utf-8")
            destination.chmod(0o644)
            replacement.chmod(0o644)
            real_open = private_config.os.open
            race_completed = False

            def swap_before_open(file_path, flags):
                nonlocal race_completed
                self.assertTrue(flags & private_config.os.O_NOFOLLOW)
                if not race_completed:
                    private_config.os.replace(replacement, destination)
                    race_completed = True
                return real_open(file_path, flags)

            with mock.patch.object(
                private_config.os,
                "open",
                side_effect=swap_before_open,
            ), self.assertRaises(FileExistsError):
                private_config.secure_existing_private_file(destination)

            self.assertEqual(
                "raced content\n",
                destination.read_text(encoding="utf-8"),
            )
            self.assertEqual(0o644, destination.stat().st_mode & 0o777)

    def test_write_refuses_to_replace_an_existing_regular_file(self):
        """Creation without overwrite permission preserves existing content."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            destination = Path(temporary_directory) / ".env"
            destination.write_text("existing configuration\n", encoding="utf-8")

            with self.assertRaises(FileExistsError):
                private_config.write_private_file(
                    destination,
                    "DB_PASSWORD=generated-secret\n",
                )

            self.assertEqual(
                "existing configuration\n",
                destination.read_text(encoding="utf-8"),
            )

    def test_windows_message_does_not_claim_owner_only_permissions(self):
        """The Windows status text accurately describes inherited ACLs."""
        with mock.patch.object(private_config.os, "name", "nt"):
            messages = [
                private_config.private_file_created_message(".env"),
                private_config.existing_private_file_message(".env"),
            ]

        for message in messages:
            self.assertIn("Windows ACL", message)
            self.assertNotIn("owner-only", message)


if __name__ == "__main__":
    unittest.main()
