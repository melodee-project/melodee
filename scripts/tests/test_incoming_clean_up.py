"""Security tests for the destructive incoming-directory cleanup tool."""

import contextlib
import importlib.util
import io
import os
import stat
import subprocess
import sys
import tempfile
import unittest
import zipfile
import zlib
from pathlib import Path
from unittest import mock

SCRIPT_PATH = Path(__file__).resolve().parents[1] / "incoming_clean_up.py"
MODULE_SPEC = importlib.util.spec_from_file_location(
    "incoming_clean_up_under_test",
    SCRIPT_PATH,
)
if MODULE_SPEC is None or MODULE_SPEC.loader is None:
    raise RuntimeError(f"Unable to load cleanup script from {SCRIPT_PATH}")
cleanup = importlib.util.module_from_spec(MODULE_SPEC)
with contextlib.redirect_stdout(io.StringIO()):
    MODULE_SPEC.loader.exec_module(cleanup)


class CleanupPathGuardTests(unittest.TestCase):
    """Verify root validation and derived-path containment."""

    def test_root_inside_explicit_boundary_is_canonicalized(self):
        """An existing nested root is accepted and stored canonically."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            boundary = Path(temporary_directory)
            root = boundary / "incoming" / "music"
            root.mkdir(parents=True)

            guard = cleanup.CleanupPathGuard.from_cli(
                "incoming/music",
                boundary,
            )

            self.assertEqual(root.resolve(), guard.root)
            self.assertEqual(boundary.resolve(), guard.trusted_boundary)

    def test_absolute_root_outside_boundary_is_rejected(self):
        """An absolute target cannot expand the administrator's capability."""
        with tempfile.TemporaryDirectory() as boundary_directory:
            with tempfile.TemporaryDirectory() as outside_directory:
                with self.assertRaises(cleanup.UnsafeCleanupPathError):
                    cleanup.CleanupPathGuard.from_cli(
                        outside_directory,
                        boundary_directory,
                    )

    def test_sibling_with_boundary_prefix_is_rejected(self):
        """A shared string prefix is not a filesystem containment grant."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            parent = Path(temporary_directory)
            boundary = parent / "trusted"
            sibling = parent / "trusted-escape"
            boundary.mkdir()
            sibling.mkdir()

            with self.assertRaises(cleanup.UnsafeCleanupPathError):
                cleanup.CleanupPathGuard.from_cli(sibling, boundary)

    def test_boundary_itself_remains_a_valid_cleanup_root(self):
        """The default root-directory behavior may authorize the boundary itself."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            boundary = Path(temporary_directory)

            guard = cleanup.CleanupPathGuard.from_cli(boundary, boundary)

            self.assertEqual(boundary.resolve(), guard.root)

    def test_filesystem_root_cannot_be_trusted_boundary(self):
        """The CLI cannot authorize destructive access to a whole filesystem."""
        filesystem_root = Path(tempfile.gettempdir()).resolve().anchor

        with self.assertRaises(cleanup.UnsafeCleanupPathError):
            cleanup.CleanupPathGuard.from_cli(
                tempfile.gettempdir(),
                filesystem_root,
            )

    def test_missing_or_non_directory_root_is_rejected(self):
        """Cleanup roots must already exist and be directories."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            boundary = Path(temporary_directory)
            regular_file = boundary / "not-a-directory"
            regular_file.write_text("content", encoding="utf-8")

            with self.subTest("missing"):
                with self.assertRaises(cleanup.UnsafeCleanupPathError):
                    cleanup.CleanupPathGuard.from_cli("missing", boundary)

            with self.subTest("regular file"):
                with self.assertRaises(cleanup.UnsafeCleanupPathError):
                    cleanup.CleanupPathGuard.from_cli(regular_file, boundary)

    @unittest.skipUnless(os.name == "posix", "POSIX symlinks required")
    def test_root_and_derived_symlink_escapes_are_rejected(self):
        """Canonicalization catches both root-level and later symlink escapes."""
        with tempfile.TemporaryDirectory() as boundary_directory:
            with tempfile.TemporaryDirectory() as outside_directory:
                boundary = Path(boundary_directory)
                outside = Path(outside_directory)
                root_link = boundary / "root-link"
                root_link.symlink_to(outside, target_is_directory=True)

                with self.assertRaises(cleanup.UnsafeCleanupPathError):
                    cleanup.CleanupPathGuard.from_cli(root_link, boundary)

                root = boundary / "incoming"
                root.mkdir()
                file_link = root / "outside-file"
                outside_file = outside / "outside.txt"
                outside_file.write_text("outside", encoding="utf-8")
                file_link.symlink_to(outside_file)
                guard = cleanup.CleanupPathGuard.from_cli(root, boundary)

                with self.assertRaises(cleanup.UnsafeCleanupPathError):
                    guard.existing_file(file_link)
                with self.assertRaises(cleanup.UnsafeCleanupPathError):
                    guard.file_identity(file_link)

                self.assertEqual("outside", outside_file.read_text(encoding="utf-8"))

    def test_live_cli_withholds_canonical_root_and_trusted_boundary(self):
        """CLI output confirms validation without disclosing supplied paths."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            boundary = Path(temporary_directory)
            root = boundary / "incoming"
            root.mkdir()

            result = subprocess.run(
                [
                    sys.executable,
                    os.fspath(SCRIPT_PATH),
                    "--root-dir",
                    os.fspath(root),
                    "--trusted-boundary",
                    os.fspath(boundary),
                    "--pretend",
                    "false",
                    "--check-sfv",
                    "false",
                ],
                check=False,
                capture_output=True,
                text=True,
            )

            self.assertEqual(0, result.returncode, result.stderr)
            self.assertIn("Canonical Cleanup Root:", result.stdout)
            self.assertIn("Trusted Boundary:", result.stdout)
            self.assertEqual(2, result.stdout.count("[path withheld]"))
            self.assertNotIn(os.fspath(root.resolve()), result.stdout)
            self.assertNotIn(os.fspath(boundary.resolve()), result.stdout)

    @unittest.skipUnless(os.name == "posix", "POSIX descriptors required")
    def test_unlink_uses_pinned_parent_during_ancestor_swap(self):
        """A swapped lexical ancestor cannot redirect unlink outside root."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            boundary = Path(temporary_directory)
            root = boundary / "incoming"
            root.mkdir()
            parent = root / "album"
            parent.mkdir()
            victim = parent / "victim.txt"
            victim.write_text("inside", encoding="utf-8")
            outside = boundary / "outside"
            outside.mkdir()
            outside_victim = outside / "victim.txt"
            outside_victim.write_text("outside", encoding="utf-8")
            guard = cleanup.CleanupPathGuard.from_cli(root, boundary)
            identity = guard.file_identity(victim)
            original_rename = guard._rename_noreplace
            swapped = False

            def swap_then_rename(
                source_parent,
                source_name,
                destination_parent,
                destination_name,
            ):
                nonlocal swapped
                if not swapped and source_name == "victim.txt":
                    parent.rename(root / "album-original")
                    parent.symlink_to(outside, target_is_directory=True)
                    swapped = True
                return original_rename(
                    source_parent,
                    source_name,
                    destination_parent,
                    destination_name,
                )

            with mock.patch.object(
                guard,
                "_rename_noreplace",
                side_effect=swap_then_rename,
            ):
                guard.unlink_regular_file(victim, identity)

            self.assertTrue(swapped)
            self.assertEqual(
                "outside",
                outside_victim.read_text(encoding="utf-8"),
            )
            self.assertFalse((root / "album-original" / "victim.txt").exists())

    @unittest.skipUnless(os.name == "posix", "POSIX descriptors required")
    def test_rmtree_uses_pinned_parent_during_ancestor_swap(self):
        """A swapped lexical ancestor cannot redirect recursive deletion."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            boundary = Path(temporary_directory)
            root = boundary / "incoming"
            root.mkdir()
            parent = root / "album"
            parent.mkdir()
            target = parent / "Greatest Hits"
            target.mkdir()
            (target / "inside.txt").write_text("inside", encoding="utf-8")
            outside = boundary / "outside"
            outside.mkdir()
            outside_target = outside / "Greatest Hits"
            outside_target.mkdir()
            preserved = outside_target / "preserved.txt"
            preserved.write_text("outside", encoding="utf-8")
            guard = cleanup.CleanupPathGuard.from_cli(root, boundary)
            identity = guard.directory_identity(target)
            original_rename = guard._rename_noreplace
            swapped = False

            def swap_then_rename(
                source_parent,
                source_name,
                destination_parent,
                destination_name,
            ):
                nonlocal swapped
                if not swapped and source_name == "Greatest Hits":
                    parent.rename(root / "album-original")
                    parent.symlink_to(outside, target_is_directory=True)
                    swapped = True
                return original_rename(
                    source_parent,
                    source_name,
                    destination_parent,
                    destination_name,
                )

            with mock.patch.object(
                guard,
                "_rename_noreplace",
                side_effect=swap_then_rename,
            ):
                guard.remove_tree(target, identity)

            self.assertTrue(swapped)
            self.assertEqual("outside", preserved.read_text(encoding="utf-8"))
            self.assertFalse((root / "album-original" / "Greatest Hits").exists())

    @unittest.skipUnless(
        cleanup._RENAMEAT2 is not None,
        "Linux renameat2 required",
    )
    def test_final_file_replacement_is_restored_not_deleted(self):
        """A same-parent replacement survives the final deletion race."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            victim = root / "victim.txt"
            victim.write_text("expected", encoding="utf-8")
            expected_identity = cleanup.CleanupPathGuard.from_cli(
                root,
                root.parent,
            ).file_identity(victim)
            guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)
            original_rename = guard._rename_noreplace
            swapped = False

            def replace_then_rename(
                source_parent,
                source_name,
                destination_parent,
                destination_name,
            ):
                nonlocal swapped
                if not swapped and source_name == "victim.txt":
                    os.rename(
                        source_name,
                        "expected-aside.txt",
                        src_dir_fd=source_parent,
                        dst_dir_fd=source_parent,
                    )
                    descriptor = os.open(
                        source_name,
                        os.O_WRONLY | os.O_CREAT | os.O_EXCL,
                        0o600,
                        dir_fd=source_parent,
                    )
                    os.write(descriptor, b"replacement")
                    os.close(descriptor)
                    swapped = True
                return original_rename(
                    source_parent,
                    source_name,
                    destination_parent,
                    destination_name,
                )

            with mock.patch.object(
                guard,
                "_rename_noreplace",
                side_effect=replace_then_rename,
            ):
                with self.assertRaises(cleanup.UnsafeCleanupPathError):
                    guard.unlink_regular_file(victim, expected_identity)

            self.assertEqual("replacement", victim.read_text(encoding="utf-8"))
            self.assertEqual(
                "expected",
                (root / "expected-aside.txt").read_text(encoding="utf-8"),
            )
            self.assertFalse(
                any(
                    path.name.startswith(".melodee-cleanup-quarantine-")
                    for path in root.iterdir()
                )
            )

    @unittest.skipUnless(
        cleanup._RENAMEAT2 is not None,
        "Linux renameat2 required",
    )
    def test_final_directory_replacement_is_restored_not_deleted(self):
        """A same-parent directory replacement survives recursive deletion."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            target = root / "target"
            target.mkdir()
            (target / "expected.txt").write_text("expected", encoding="utf-8")
            guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)
            expected_identity = guard.directory_identity(target)
            original_rename = guard._rename_noreplace
            swapped = False

            def replace_then_rename(
                source_parent,
                source_name,
                destination_parent,
                destination_name,
            ):
                nonlocal swapped
                if not swapped and source_name == "target":
                    os.rename(
                        source_name,
                        "expected-aside",
                        src_dir_fd=source_parent,
                        dst_dir_fd=source_parent,
                    )
                    os.mkdir(source_name, mode=0o700, dir_fd=source_parent)
                    swapped = True
                return original_rename(
                    source_parent,
                    source_name,
                    destination_parent,
                    destination_name,
                )

            with mock.patch.object(
                guard,
                "_rename_noreplace",
                side_effect=replace_then_rename,
            ):
                with self.assertRaises(cleanup.UnsafeCleanupPathError):
                    guard.remove_tree(target, expected_identity)

            self.assertTrue(target.is_dir())
            self.assertEqual([], list(target.iterdir()))
            self.assertEqual(
                "expected",
                (root / "expected-aside" / "expected.txt").read_text(
                    encoding="utf-8"
                ),
            )

    def test_live_mutation_fails_closed_without_descriptor_primitives(self):
        """Unsupported platforms may inspect but never mutate the tree."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            (root / "Greatest Hits").mkdir()
            with mock.patch.object(
                cleanup.CleanupPathGuard,
                "_secure_mutation_primitives_available",
                return_value=False,
            ):
                guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)

            with self.assertRaises(cleanup.UnsafeCleanupPathError):
                cleanup.delete_matching_dirs(
                    guard,
                    pretend=False,
                    check_sfv=False,
                )

            with mock.patch.object(cleanup, "TQDM_AVAILABLE", False):
                with contextlib.redirect_stdout(io.StringIO()):
                    cleanup.delete_matching_dirs(
                        guard,
                        pretend=True,
                        check_sfv=False,
                    )
            self.assertTrue((root / "Greatest Hits").is_dir())


class ZipExtractionSecurityTests(unittest.TestCase):
    """Verify safe and malicious ZIP behavior in pretend and live modes."""

    def setUp(self):
        """Reset extraction state shared between archive tests."""
        cleanup.stats.clear()
        cleanup.shutdown_requested = False

    def test_normal_archive_respects_pretend_then_extracts_in_live_mode(self):
        """Pretend mode is read-only and live mode extracts an in-root archive."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            archive = root / "release.zip"
            with zipfile.ZipFile(archive, "w") as zip_file:
                zip_file.writestr("album/track.txt", "audio metadata")
            guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)

            with contextlib.redirect_stdout(io.StringIO()):
                cleanup.unzip_files_in_directory(root, guard, pretend=True)

            self.assertTrue(archive.exists())
            self.assertFalse((root / "album").exists())

            with contextlib.redirect_stdout(io.StringIO()):
                cleanup.unzip_files_in_directory(root, guard, pretend=False)

            self.assertFalse(archive.exists())
            self.assertEqual(
                "audio metadata",
                (root / "album" / "track.txt").read_text(encoding="utf-8"),
            )
            current_umask = os.umask(0)
            os.umask(current_umask)
            self.assertEqual(
                0o700 & ~current_umask,
                stat.S_IMODE((root / "album").stat().st_mode),
            )
            self.assertEqual(
                0o600 & ~current_umask,
                stat.S_IMODE((root / "album" / "track.txt").stat().st_mode),
            )

    @unittest.skipUnless(
        cleanup.CleanupPathGuard._secure_mutation_primitives_available(),
        "Secure descriptor traversal required",
    )
    def test_pretend_preflight_reports_an_existing_file_collision(self):
        """Pretend mode predicts a live extraction collision without mutation."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            archive = root / "release.zip"
            existing = root / "track.txt"
            existing.write_text("preserve", encoding="utf-8")
            with zipfile.ZipFile(archive, "w") as zip_file:
                zip_file.writestr("track.txt", "replacement")
            guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)

            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                cleanup.unzip_files_in_directory(root, guard, pretend=True)

            self.assertTrue(archive.exists())
            self.assertEqual("preserve", existing.read_text(encoding="utf-8"))
            self.assertIn("Error processing ZIP archive", output.getvalue())

    def test_zip_slip_is_rejected_before_any_member_is_extracted(self):
        """A traversal member prevents even earlier safe members from being written."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory) / "incoming"
            root.mkdir()
            archive = root / "malicious.zip"
            outside = root.parent / "escaped.txt"
            with zipfile.ZipFile(archive, "w") as zip_file:
                zip_file.writestr("safe.txt", "must not be extracted")
                zip_file.writestr("../escaped.txt", "escaped")
            guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)

            pretend_output = io.StringIO()
            with contextlib.redirect_stdout(pretend_output):
                cleanup.unzip_files_in_directory(root, guard, pretend=True)

            self.assertIn("Error processing ZIP archive", pretend_output.getvalue())
            self.assertNotIn("Would unzip ZIP archive", pretend_output.getvalue())
            with contextlib.redirect_stdout(io.StringIO()):
                cleanup.unzip_files_in_directory(root, guard, pretend=False)

            self.assertTrue(archive.exists())
            self.assertFalse((root / "safe.txt").exists())
            self.assertFalse(outside.exists())

    @unittest.skipUnless(os.name == "posix", "POSIX symlinks required")
    def test_preexisting_output_symlink_escape_is_rejected(self):
        """Extraction cannot write through an output directory symlink."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            boundary = Path(temporary_directory)
            root = boundary / "incoming"
            root.mkdir()
            outside = boundary / "outside"
            outside.mkdir()
            album_link = root / "album"
            album_link.symlink_to(outside, target_is_directory=True)
            archive = root / "release.zip"
            with zipfile.ZipFile(archive, "w") as zip_file:
                zip_file.writestr("album/track.txt", "must stay contained")
            guard = cleanup.CleanupPathGuard.from_cli(root, boundary)

            with contextlib.redirect_stdout(io.StringIO()):
                cleanup.unzip_files_in_directory(root, guard, pretend=False)

            self.assertTrue(archive.exists())
            self.assertTrue(album_link.is_symlink())
            self.assertEqual([], list(outside.iterdir()))

    def test_absolute_and_symlink_members_are_rejected(self):
        """ZIP entries cannot name absolute paths or create symbolic links."""
        malicious_members = ("/absolute.txt", "C:\\absolute.txt", "symlink")
        for member_name in malicious_members:
            with self.subTest(member=member_name):
                with tempfile.TemporaryDirectory() as temporary_directory:
                    root = Path(temporary_directory)
                    archive = root / "malicious.zip"
                    with zipfile.ZipFile(archive, "w") as zip_file:
                        if member_name == "symlink":
                            member = zipfile.ZipInfo(member_name)
                            member.create_system = 3
                            member.external_attr = (stat.S_IFLNK | stat.S_IRWXU) << 16
                            zip_file.writestr(member, "../outside")
                        else:
                            zip_file.writestr(member_name, "outside")
                    guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)

                    with contextlib.redirect_stdout(io.StringIO()):
                        cleanup.unzip_files_in_directory(
                            root,
                            guard,
                            pretend=False,
                        )

                    self.assertTrue(archive.exists())
                    self.assertEqual([archive], list(root.iterdir()))

    @unittest.skipUnless(os.name == "posix", "POSIX symlinks required")
    def test_zip_symlink_to_contained_archive_is_not_processed_or_removed(self):
        """A ZIP-named symlink cannot erase its contained target archive."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            target = root / "archive.data"
            with zipfile.ZipFile(target, "w") as zip_file:
                zip_file.writestr("track.txt", "content")
            linked_archive = root / "linked.zip"
            linked_archive.symlink_to(target)
            guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)

            with contextlib.redirect_stdout(io.StringIO()):
                cleanup.unzip_files_in_directory(root, guard, pretend=False)

            self.assertTrue(target.exists())
            self.assertTrue(linked_archive.is_symlink())
            self.assertFalse((root / "track.txt").exists())

    @unittest.skipUnless(os.name == "posix", "POSIX hardlinks required")
    def test_existing_hardlink_target_is_not_truncated(self):
        """O_EXCL extraction preserves content linked from outside the root."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            boundary = Path(temporary_directory)
            root = boundary / "incoming"
            root.mkdir()
            outside = boundary / "outside.txt"
            outside.write_text("preserve", encoding="utf-8")
            os.link(outside, root / "target.txt")
            archive = root / "payload.zip"
            with zipfile.ZipFile(archive, "w") as zip_file:
                zip_file.writestr("target.txt", "attacker content")
            guard = cleanup.CleanupPathGuard.from_cli(root, boundary)

            with contextlib.redirect_stdout(io.StringIO()):
                cleanup.unzip_files_in_directory(root, guard, pretend=False)

            self.assertTrue(archive.exists())
            self.assertEqual("preserve", outside.read_text(encoding="utf-8"))
            self.assertEqual(
                "preserve",
                (root / "target.txt").read_text(encoding="utf-8"),
            )

    def test_member_cannot_target_its_source_archive(self):
        """A self-named member cannot truncate the archive being consumed."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            archive = root / "release.zip"
            with zipfile.ZipFile(archive, "w") as zip_file:
                zip_file.writestr("release.zip", "replacement")
            original_archive = archive.read_bytes()
            guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)

            with contextlib.redirect_stdout(io.StringIO()):
                cleanup.unzip_files_in_directory(root, guard, pretend=False)

            self.assertEqual(original_archive, archive.read_bytes())
            with zipfile.ZipFile(archive, "r") as zip_file:
                self.assertEqual(["release.zip"], zip_file.namelist())

    def test_portable_duplicate_and_prefix_conflicts_are_atomic(self):
        """Case, Unicode, and file-prefix aliases reject the whole archive."""
        member_sets = (
            (("Track.txt", "one"), ("track.TXT", "two")),
            (("café.txt", "one"), ("cafe\u0301.TXT", "two")),
            (("album", "file"), ("album/track.txt", "child")),
        )
        for members in member_sets:
            with self.subTest(members=tuple(name for name, _ in members)):
                with tempfile.TemporaryDirectory() as temporary_directory:
                    root = Path(temporary_directory)
                    archive = root / "conflict.zip"
                    with zipfile.ZipFile(archive, "w") as zip_file:
                        for name, content in members:
                            zip_file.writestr(name, content)
                    guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)

                    with contextlib.redirect_stdout(io.StringIO()):
                        cleanup.unzip_files_in_directory(
                            root,
                            guard,
                            pretend=False,
                        )

                    self.assertTrue(archive.exists())
                    self.assertEqual([archive], list(root.iterdir()))

    def test_member_failure_rolls_back_all_created_output(self):
        """A later decompression failure removes all new archive output."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            archive = root / "partial.zip"
            with zipfile.ZipFile(archive, "w") as zip_file:
                zip_file.writestr("album/first.txt", "first")
                zip_file.writestr("album/second.txt", "second")
            guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)
            original_open = cleanup.zipfile.ZipFile.open

            def fail_second_member(zip_file, member, *args, **kwargs):
                member_name = (
                    member.filename
                    if isinstance(member, zipfile.ZipInfo)
                    else member
                )
                if member_name == "album/second.txt":
                    raise zipfile.BadZipFile("injected member failure")
                return original_open(zip_file, member, *args, **kwargs)

            with mock.patch.object(
                cleanup.zipfile.ZipFile,
                "open",
                new=fail_second_member,
            ):
                with contextlib.redirect_stdout(io.StringIO()):
                    cleanup.unzip_files_in_directory(
                        root,
                        guard,
                        pretend=False,
                    )

            self.assertTrue(archive.exists())
            self.assertFalse((root / "album").exists())

    def test_interrupted_copy_rolls_back_and_preserves_archive(self):
        """A shutdown request between chunks leaves no partial output."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            archive = root / "interrupt.zip"
            with zipfile.ZipFile(archive, "w") as zip_file:
                zip_file.writestr("album/track.txt", "chunked content")
            guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)
            original_open = cleanup.zipfile.ZipFile.open

            class InterruptingReader:
                """Set the shutdown flag after returning one data chunk."""

                def __init__(self, source):
                    self.source = source
                    self.interrupted = False

                def __enter__(self):
                    return self

                def __exit__(self, exc_type, exc_value, traceback):
                    self.source.close()

                def read(self, size=-1):
                    data = self.source.read(size)
                    if data and not self.interrupted:
                        cleanup.shutdown_requested = True
                        self.interrupted = True
                    return data

            def interrupting_open(zip_file, member, *args, **kwargs):
                return InterruptingReader(
                    original_open(zip_file, member, *args, **kwargs)
                )

            try:
                with mock.patch.object(
                    cleanup.zipfile.ZipFile,
                    "open",
                    new=interrupting_open,
                ):
                    with mock.patch.object(cleanup, "ZIP_COPY_CHUNK_BYTES", 2):
                        with contextlib.redirect_stdout(io.StringIO()):
                            cleanup.unzip_files_in_directory(
                                root,
                                guard,
                                pretend=False,
                            )
            finally:
                cleanup.shutdown_requested = False

            self.assertTrue(archive.exists())
            self.assertFalse((root / "album").exists())

    def test_member_count_and_size_quotas_reject_before_writes(self):
        """Configured archive bounds apply before any destination is created."""
        quota_overrides = (
            {"MAX_ZIP_MEMBERS": 1},
            {"MAX_ZIP_MEMBER_BYTES": 3},
            {"MAX_ZIP_TOTAL_BYTES": 6},
            {"MAX_ZIP_COMPRESSION_RATIO": 1},
        )
        for overrides in quota_overrides:
            with self.subTest(overrides=overrides):
                with tempfile.TemporaryDirectory() as temporary_directory:
                    root = Path(temporary_directory)
                    archive = root / "bounded.zip"
                    with zipfile.ZipFile(
                        archive,
                        "w",
                        compression=zipfile.ZIP_DEFLATED,
                    ) as zip_file:
                        zip_file.writestr("first.txt", "A" * 100)
                        zip_file.writestr("second.txt", "B" * 100)
                    guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)
                    patches = [
                        mock.patch.object(cleanup, name, value)
                        for name, value in overrides.items()
                    ]
                    for patcher in patches:
                        patcher.start()
                    try:
                        with contextlib.redirect_stdout(io.StringIO()):
                            cleanup.unzip_files_in_directory(
                                root,
                                guard,
                                pretend=False,
                            )
                    finally:
                        for patcher in reversed(patches):
                            patcher.stop()

                    self.assertTrue(archive.exists())
                    self.assertFalse((root / "first.txt").exists())
                    self.assertFalse((root / "second.txt").exists())

    def test_reserved_windows_names_are_rejected_portably(self):
        """Windows devices, streams, and normalized aliases are never targets."""
        reserved_names = (
            "CON",
            "NUL.txt",
            "file.txt:stream",
            "trailing.",
            "trailing ",
            "COM1 .txt",
            "wild?.txt",
        )
        for filename in reserved_names:
            with self.subTest(filename=filename):
                with self.assertRaises(cleanup.UnsafeCleanupPathError):
                    cleanup.validate_relative_member(filename, "ZIP member")

    def test_encrypted_archive_is_local_failure_and_next_archive_continues(self):
        """Encryption rejection leaves that archive but does not stop the batch."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            encrypted = root / "encrypted.zip"
            normal = root / "normal.zip"
            with zipfile.ZipFile(encrypted, "w") as zip_file:
                zip_file.writestr("secret.txt", "secret")
            with zipfile.ZipFile(normal, "w") as zip_file:
                zip_file.writestr("normal.txt", "normal")
            guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)
            original_infolist = cleanup.zipfile.ZipFile.infolist

            def mark_secret_encrypted(zip_file):
                members = original_infolist(zip_file)
                for member in members:
                    if member.filename == "secret.txt":
                        member.flag_bits |= 0x1
                return members

            with mock.patch.object(
                cleanup.zipfile.ZipFile,
                "infolist",
                new=mark_secret_encrypted,
            ):
                with contextlib.redirect_stdout(io.StringIO()):
                    cleanup.unzip_files_in_directory(
                        root,
                        guard,
                        pretend=False,
                    )

            self.assertTrue(encrypted.exists())
            self.assertFalse(normal.exists())
            self.assertFalse((root / "secret.txt").exists())
            self.assertEqual(
                "normal",
                (root / "normal.txt").read_text(encoding="utf-8"),
            )


class SfvPathSecurityTests(unittest.TestCase):
    """Verify SFV filenames cannot escape their search directory."""

    def setUp(self):
        """Reset checksum state shared between tests."""
        cleanup.shutdown_requested = False

    def test_normal_in_root_sfv_entry_verifies(self):
        """A normal relative SFV filename still passes CRC verification."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            track = root / "track.mp3"
            content = b"track content"
            track.write_bytes(content)
            checksum = f"{zlib.crc32(content) & 0xFFFFFFFF:08X}"
            sfv = root / "release.sfv"
            sfv.write_text(f"track.mp3 {checksum}\n", encoding="utf-8")
            guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)

            passed, results = cleanup.verify_sfv_file(sfv, guard)

            self.assertTrue(passed)
            self.assertEqual("PASS", results["track.mp3"]["status"])

    def test_parent_and_absolute_sfv_entries_are_rejected(self):
        """Traversal and absolute SFV filenames never reach checksum reads."""
        malicious_names = ("../outside.mp3", "/outside.mp3", "C:\\outside.mp3")
        for filename in malicious_names:
            with self.subTest(filename=filename):
                with tempfile.TemporaryDirectory() as temporary_directory:
                    root = Path(temporary_directory) / "incoming"
                    root.mkdir()
                    sfv = root / "release.sfv"
                    sfv.write_text(
                        f"{filename} 00000000\n",
                        encoding="utf-8",
                    )
                    guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)

                    passed, results = cleanup.verify_sfv_file(sfv, guard)

                    self.assertFalse(passed)
                    self.assertEqual(
                        "UNSAFE",
                        results["<unsafe-sfv-entry>"]["status"],
                    )

    def test_sfv_encoding_rename_respects_pretend_and_live_modes(self):
        """A valid encoding repair is reported in pretend mode and applied live."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            damaged_name = root / "track (invalid encoding).mp3"
            content = b"track content"
            damaged_name.write_bytes(content)
            checksum = f"{zlib.crc32(content) & 0xFFFFFFFF:08X}"
            (root / "release.sfv").write_text(
                f"track.mp3 {checksum}\n",
                encoding="utf-8",
            )
            guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)

            with contextlib.redirect_stdout(io.StringIO()):
                cleanup.check_sfv_integrity(root, guard, pretend=True)

            self.assertTrue(damaged_name.exists())
            self.assertFalse((root / "track.mp3").exists())

            with contextlib.redirect_stdout(io.StringIO()):
                cleanup.check_sfv_integrity(root, guard, pretend=False)

            self.assertFalse(damaged_name.exists())
            self.assertEqual(content, (root / "track.mp3").read_bytes())

    @unittest.skipUnless(os.name == "posix", "POSIX symlinks required")
    def test_encoding_repair_refuses_contained_file_symlink(self):
        """A corrupt-name symlink cannot rename its contained regular target."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            source = root / "preserve.bin"
            content = b"track content"
            source.write_bytes(content)
            linked_name = root / "track (invalid encoding).mp3"
            linked_name.symlink_to(source)
            checksum = f"{zlib.crc32(content) & 0xFFFFFFFF:08X}"
            (root / "release.sfv").write_text(
                f"track.mp3 {checksum}\n",
                encoding="utf-8",
            )
            guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)

            with contextlib.redirect_stdout(io.StringIO()):
                cleanup.check_sfv_integrity(root, guard, pretend=False)

            self.assertTrue(linked_name.is_symlink())
            self.assertEqual(content, source.read_bytes())
            self.assertFalse((root / "track.mp3").exists())

    def test_sfv_byte_and_line_limits_fail_closed(self):
        """Oversized checksum metadata is unsafe without an unbounded read."""
        cases = (
            ({"MAX_SFV_BYTES": 8}, "track.mp3 00000000\n"),
            ({"MAX_SFV_LINES": 1}, "; first\n; second\n"),
        )
        for overrides, content in cases:
            with self.subTest(overrides=overrides):
                with tempfile.TemporaryDirectory() as temporary_directory:
                    root = Path(temporary_directory)
                    sfv = root / "release.sfv"
                    sfv.write_text(content, encoding="utf-8")
                    guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)
                    patches = [
                        mock.patch.object(cleanup, name, value)
                        for name, value in overrides.items()
                    ]
                    for patcher in patches:
                        patcher.start()
                    try:
                        passed, results = cleanup.verify_sfv_file(sfv, guard)
                    finally:
                        for patcher in reversed(patches):
                            patcher.stop()

                    self.assertFalse(passed)
                    self.assertEqual(
                        "UNSAFE",
                        results["<unsafe-sfv-entry>"]["status"],
                    )

    @unittest.skipUnless(
        cleanup._RENAMEAT2 is not None,
        "Linux renameat2 required",
    )
    def test_encoding_rename_restores_a_final_source_replacement(self):
        """SFV repair never publishes or deletes a raced-in source file."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            source = root / "track (invalid encoding).mp3"
            destination = root / "track.mp3"
            source.write_text("expected", encoding="utf-8")
            guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)
            expected_identity = guard.file_identity(source)
            original_rename = guard._rename_noreplace
            swapped = False

            def replace_then_rename(
                source_parent,
                source_name,
                destination_parent,
                destination_name,
            ):
                nonlocal swapped
                if not swapped and source_name == source.name:
                    os.rename(
                        source_name,
                        "expected-aside.mp3",
                        src_dir_fd=source_parent,
                        dst_dir_fd=source_parent,
                    )
                    descriptor = os.open(
                        source_name,
                        os.O_WRONLY | os.O_CREAT | os.O_EXCL,
                        0o600,
                        dir_fd=source_parent,
                    )
                    os.write(descriptor, b"replacement")
                    os.close(descriptor)
                    swapped = True
                return original_rename(
                    source_parent,
                    source_name,
                    destination_parent,
                    destination_name,
                )

            with mock.patch.object(
                guard,
                "_rename_noreplace",
                side_effect=replace_then_rename,
            ):
                with self.assertRaises(cleanup.UnsafeCleanupPathError):
                    guard.rename_regular_file_no_replace(
                        source,
                        destination,
                        expected_identity,
                    )

            self.assertEqual("replacement", source.read_text(encoding="utf-8"))
            self.assertEqual(
                "expected",
                (root / "expected-aside.mp3").read_text(encoding="utf-8"),
            )
            self.assertFalse(destination.exists())


class DestructiveContainmentTests(unittest.TestCase):
    """Verify recursive cleanup cannot delete beyond its validated root."""

    def setUp(self):
        """Reset module-level state shared by cleanup runs."""
        cleanup.stats.clear()
        cleanup.shutdown_requested = False

    @unittest.skipUnless(os.name == "posix", "POSIX symlinks required")
    def test_live_cleanup_deletes_in_root_match_but_not_symlink_escape(self):
        """Live rmtree is allowed in-root and denied through an outside symlink."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            boundary = Path(temporary_directory)
            root = boundary / "incoming"
            root.mkdir()
            normal_match = root / "Greatest Hits"
            normal_match.mkdir()
            (normal_match / "track.txt").write_text("track", encoding="utf-8")
            outside = boundary / "outside"
            outside.mkdir()
            outside_file = outside / "preserve.txt"
            outside_file.write_text("preserve", encoding="utf-8")
            escape = root / "Greatest Hits Escape"
            escape.symlink_to(outside, target_is_directory=True)
            guard = cleanup.CleanupPathGuard.from_cli(root, boundary)

            with mock.patch.object(cleanup, "TQDM_AVAILABLE", False):
                with contextlib.redirect_stdout(io.StringIO()):
                    cleanup.delete_matching_dirs(
                        guard,
                        pretend=False,
                        check_sfv=False,
                    )

            self.assertFalse(normal_match.exists())
            self.assertTrue(escape.is_symlink())
            self.assertEqual("preserve", outside_file.read_text(encoding="utf-8"))

    @unittest.skipUnless(os.name == "posix", "POSIX symlinks required")
    def test_contained_symlink_aliases_cannot_delete_their_targets(self):
        """Policy names on links never transfer deletion to contained targets."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            preserved_directory = root / "Studio Album"
            preserved_directory.mkdir()
            preserved_track = preserved_directory / "track.txt"
            preserved_track.write_text("preserve", encoding="utf-8")
            directory_link = root / "Greatest Hits"
            directory_link.symlink_to(
                preserved_directory,
                target_is_directory=True,
            )
            preserved_image = root / "cover.bin"
            preserved_image.write_bytes(b"preserve image")
            image_link = root / "proof.jpg"
            image_link.symlink_to(preserved_image)
            guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)

            with mock.patch.object(cleanup, "TQDM_AVAILABLE", False):
                with contextlib.redirect_stdout(io.StringIO()):
                    cleanup.delete_matching_dirs(
                        guard,
                        pretend=False,
                        check_sfv=False,
                    )

            self.assertTrue(directory_link.is_symlink())
            self.assertEqual(
                "preserve",
                preserved_track.read_text(encoding="utf-8"),
            )
            self.assertTrue(image_link.is_symlink())
            self.assertEqual(b"preserve image", preserved_image.read_bytes())

    def test_pretend_cleanup_does_not_delete_matching_directory(self):
        """Pretend mode reports a match without mutation or name disclosure."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            sensitive_name = "private-token-Greatest Hits"
            matching_directory = root / sensitive_name
            matching_directory.mkdir()
            guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)

            with mock.patch.object(cleanup, "TQDM_AVAILABLE", False):
                output = io.StringIO()
                with contextlib.redirect_stdout(output):
                    deleted = cleanup.delete_matching_dirs(
                        guard,
                        pretend=True,
                        check_sfv=False,
                    )

            self.assertEqual(1, deleted)
            self.assertTrue(matching_directory.is_dir())
            self.assertIn("Would delete directory matched by policy", output.getvalue())
            self.assertNotIn(sensitive_name, output.getvalue())

    def test_extension_statistics_reject_log_forging_characters(self):
        """Untrusted extensions are bucketed without terminal/log injection."""
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            malicious_fragment = "\x1b[31mLEAK\r\nINJECTED"
            (root / f"track.{malicious_fragment}").write_text(
                "content",
                encoding="utf-8",
            )
            guard = cleanup.CleanupPathGuard.from_cli(root, root.parent)
            output = io.StringIO()

            with mock.patch.object(cleanup, "TQDM_AVAILABLE", False):
                with contextlib.redirect_stdout(output):
                    cleanup.delete_matching_dirs(
                        guard,
                        pretend=True,
                        check_sfv=False,
                    )
                    cleanup.print_statistics()

            self.assertEqual(1, cleanup.stats["files_other"])
            self.assertNotIn("\x1b[31mLEAK", output.getvalue())
            self.assertNotIn("\r\nINJECTED", output.getvalue())
            self.assertIn(".other: 1", output.getvalue())


if __name__ == "__main__":
    unittest.main()
