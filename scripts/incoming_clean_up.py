#!/usr/bin/python3
import os
import shutil
import re
import argparse
import contextlib
import ctypes
import errno
import inspect
import secrets
import sys
import zipfile
import stat
from collections import defaultdict
import time
import math
import signal
import unicodedata
import zlib
from pathlib import Path, PurePosixPath, PureWindowsPath

# Import tqdm for progress indication
try:
    from tqdm import tqdm
    TQDM_AVAILABLE = True
except ImportError:
    TQDM_AVAILABLE = False
    print("Warning: tqdm not available. Install with 'pip install tqdm' for progress bars.")

# Import PIL for image processing
try:
    from PIL import Image
    PIL_AVAILABLE = True
except ImportError:
    PIL_AVAILABLE = False
    print("Warning: PIL (Pillow) not available. Install with 'pip install Pillow' for image processing.")

print("DEBUG: Script loaded successfully")

# List of keywords to search for in directory names (case-insensitive)
KEYWORDS = ["Live", "VA", "Greatest", "Hits", "Show", "Radio", "Single", "Billboard", "Top", "Charts", "Compilation", "Collection", "Best Of", "DJ Mix", "Live Mix"]

# Pre-compiled regex patterns for better performance
DATE_PATTERN_1 = re.compile(r'\b\d{2}-\d{2}-\d{4}\b|\b\d{4}-\d{2}-\d{2}\b|\b\d{2}-\d{2}-\d{2}\b', re.IGNORECASE)
WEEKDAYS = r'(MON|TUE|WED|THU|FRI|SAT|SUN)'
DATE_PATTERN_2 = re.compile(rf'\b\d{{2}}-\d{{2}}-{WEEKDAYS}\b', re.IGNORECASE)
DATE_PATTERN_3 = re.compile(rf'\b{WEEKDAYS}-\d{{2}}-\d{{2}}\b', re.IGNORECASE)
COMBINED_DATE_PATTERN = re.compile(rf'({DATE_PATTERN_1.pattern})|({DATE_PATTERN_2.pattern})|({DATE_PATTERN_3.pattern})', re.IGNORECASE)
START_DATE_PATTERN = re.compile(rf'^({DATE_PATTERN_1.pattern}|{DATE_PATTERN_2.pattern}|{DATE_PATTERN_3.pattern})\/', re.IGNORECASE)

# Pre-compiled keyword patterns
KEYWORD_PATTERNS = {
    keyword: re.compile(rf'(?<![a-zA-Z0-9]){re.escape(keyword)}(?![a-zA-Z0-9])', re.IGNORECASE) 
    for keyword in KEYWORDS
}

# Image file extensions to check
IMAGE_EXTENSIONS = {'.jpg', '.jpeg', '.png', '.gif', '.bmp', '.tiff', '.webp'}

# Pre-compiled pattern for "proof" in filenames
PROOF_PATTERN = re.compile(r'proof', re.IGNORECASE)

# Pre-compiled pattern for SFV files
SFV_PATTERN = re.compile(r'\.sfv$', re.IGNORECASE)

# SFV file extensions to check
SFV_EXTENSIONS = {'.sfv'}

# Global flag for graceful shutdown
shutdown_requested = False

# Archive and checksum policy limits prevent untrusted incoming metadata from
# consuming unbounded memory, CPU, or disk. Tests override these constants with
# deliberately small values when exercising the boundaries.
MAX_ZIP_MEMBERS = 10_000
MAX_ZIP_MEMBER_BYTES = 16 * 1024 * 1024 * 1024
MAX_ZIP_TOTAL_BYTES = 128 * 1024 * 1024 * 1024
MAX_ZIP_COMPRESSION_RATIO = 1_000
ZIP_COPY_CHUNK_BYTES = 1024 * 1024
MAX_SFV_BYTES = 8 * 1024 * 1024
MAX_SFV_LINES = 100_000

SUPPORTED_ZIP_COMPRESSION = {
    zipfile.ZIP_STORED,
    zipfile.ZIP_DEFLATED,
    zipfile.ZIP_BZIP2,
    zipfile.ZIP_LZMA,
}
if hasattr(zipfile, "ZIP_ZSTANDARD"):
    SUPPORTED_ZIP_COMPRESSION.add(zipfile.ZIP_ZSTANDARD)

RENAME_NOREPLACE = 1


def _load_renameat2():
    """Load Linux's atomic no-replace rename primitive when available."""
    if os.name != "posix" or not sys.platform.startswith("linux"):
        return None
    try:
        renameat2 = ctypes.CDLL(None, use_errno=True).renameat2
    except (AttributeError, OSError):
        return None
    renameat2.argtypes = (
        ctypes.c_int,
        ctypes.c_char_p,
        ctypes.c_int,
        ctypes.c_char_p,
        ctypes.c_uint,
    )
    renameat2.restype = ctypes.c_int
    return renameat2


_RENAMEAT2 = _load_renameat2()


class UnsafeCleanupPathError(ValueError):
    """Raised when a cleanup path escapes its authorized root."""


class UnsafeArchiveError(ValueError):
    """Raised when an archive violates cleanup extraction policy."""


class CleanupInterruptedError(RuntimeError):
    """Raised after a requested shutdown interrupts archive extraction."""


class CleanupPathGuard:
    """Contain reads and perform live mutations relative to a pinned root."""

    def __init__(self, trusted_boundary, root):
        """Store validated paths and pin the cleanup root when supported."""
        self.trusted_boundary = Path(trusted_boundary)
        self.root = Path(root)
        self._root_descriptor = None
        self._root_identity = None
        self._quarantine_descriptor = None
        self._quarantine_name = None

        if self._secure_mutation_primitives_available():
            flags = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW
            descriptor = os.open(self.root, flags)
            root_stat = os.fstat(descriptor)
            if not stat.S_ISDIR(root_stat.st_mode):
                os.close(descriptor)
                raise UnsafeCleanupPathError(
                    "Cleanup root is not a stable directory."
                )
            self._root_descriptor = descriptor
            self._root_identity = (root_stat.st_dev, root_stat.st_ino)

    def __del__(self):
        """Release the pinned root descriptor when the guard is collected."""
        quarantine_descriptor = getattr(
            self,
            "_quarantine_descriptor",
            None,
        )
        root_descriptor = getattr(self, "_root_descriptor", None)
        quarantine_name = getattr(self, "_quarantine_name", None)
        if quarantine_descriptor is not None:
            try:
                os.close(quarantine_descriptor)
            except OSError:
                pass
            self._quarantine_descriptor = None
            if root_descriptor is not None and quarantine_name is not None:
                try:
                    os.rmdir(quarantine_name, dir_fd=root_descriptor)
                except OSError:
                    pass
        descriptor = getattr(self, "_root_descriptor", None)
        if descriptor is not None:
            try:
                os.close(descriptor)
            except OSError:
                pass
            self._root_descriptor = None

    @staticmethod
    def _secure_mutation_primitives_available():
        """Return whether descriptor-relative, no-follow mutation is safe."""
        required_dir_fd = {
            os.open,
            os.stat,
            os.mkdir,
            os.unlink,
            os.rmdir,
        }
        return (
            os.name == "posix"
            and hasattr(os, "O_DIRECTORY")
            and hasattr(os, "O_NOFOLLOW")
            and required_dir_fd.issubset(os.supports_dir_fd)
            and os.stat in os.supports_follow_symlinks
            and shutil.rmtree.avoids_symlink_attacks
            and "dir_fd" in inspect.signature(shutil.rmtree).parameters
            and _RENAMEAT2 is not None
        )

    @property
    def secure_mutation_supported(self):
        """Return whether this guard has a pinned secure root descriptor."""
        return self._root_descriptor is not None

    def require_secure_mutation(self):
        """Fail closed when live mutation cannot use stable descriptors."""
        if not self.secure_mutation_supported:
            raise UnsafeCleanupPathError(
                "Live cleanup requires POSIX descriptor-relative no-follow "
                "filesystem operations; use pretend mode on this platform."
            )

    @classmethod
    def from_cli(cls, root_dir, trusted_boundary=None):
        """Validate the CLI root beneath an explicit trusted boundary."""
        boundary_input = trusted_boundary or os.getcwd()
        boundary = os.path.normcase(
            os.path.realpath(os.path.expanduser(boundary_input))
        )

        if Path(boundary).parent == Path(boundary):
            raise UnsafeCleanupPathError(
                "The filesystem root cannot be used as the trusted boundary."
            )

        expanded_root = os.path.expanduser(root_dir)
        if not os.path.isabs(expanded_root):
            expanded_root = os.path.join(boundary, expanded_root)

        root = os.path.normcase(os.path.realpath(expanded_root))
        if not root.startswith(boundary):
            raise UnsafeCleanupPathError(
                "Cleanup root must be inside the trusted boundary."
            )

        root_marker = cls._directory_marker(root)
        boundary_marker = cls._directory_marker(boundary)
        try:
            common_path = os.path.commonpath((boundary, root))
        except ValueError as error:
            raise UnsafeCleanupPathError(
                "Cleanup root and trusted boundary must share a filesystem."
            ) from error
        if (
            common_path != boundary
            or not root_marker.startswith(boundary_marker)
        ):
            raise UnsafeCleanupPathError(
                "Cleanup root must be inside the trusted boundary."
            )

        if not os.path.isdir(root):
            raise UnsafeCleanupPathError(
                "Cleanup root must be an existing directory."
            )

        # An existing contained root proves its canonical boundary is also an
        # existing directory; no unchecked access to the CLI boundary is needed.

        return cls(boundary, root)

    @staticmethod
    def _directory_marker(path):
        """Return an absolute path with a trailing native separator."""
        return path if path.endswith(os.sep) else path + os.sep

    def _lexical_within_root(self, candidate):
        """Normalize a lexical path and ensure it is rooted beneath cleanup."""
        lexical = os.path.abspath(os.path.normpath(os.fspath(candidate)))
        lexical_marker = self._directory_marker(lexical)
        root_marker = self._directory_marker(os.fspath(self.root))
        try:
            common_path = os.path.commonpath((os.fspath(self.root), lexical))
        except ValueError as error:
            raise UnsafeCleanupPathError(
                "Path and cleanup root must share a filesystem."
            ) from error
        if (
            os.path.normcase(common_path)
            != os.path.normcase(os.fspath(self.root))
            or not os.path.normcase(lexical_marker).startswith(
                os.path.normcase(root_marker)
            )
        ):
            raise UnsafeCleanupPathError(
                "Path escapes the cleanup root."
            )
        return Path(lexical)

    def relative_parts(self, candidate):
        """Return lexical path components relative to the cleanup root."""
        lexical = self._lexical_within_root(candidate)
        relative = os.path.relpath(lexical, self.root)
        if relative == os.curdir:
            return ()
        parts = Path(relative).parts
        if any(part in (os.curdir, os.pardir) for part in parts):
            raise UnsafeCleanupPathError("Path contains unsafe relative parts.")
        return parts

    def _reject_symlink_components(self, candidate):
        """Portably reject symlinks when stable descriptors are unavailable."""
        lexical = self._lexical_within_root(candidate)
        relative_parts = lexical.relative_to(self.root).parts
        current = self.root
        for part in relative_parts:
            current /= part
            if current.is_symlink():
                raise UnsafeCleanupPathError(
                    f"Refusing mutation through symlink: {current}"
                )
            if not current.exists():
                break
        return lexical

    @contextlib.contextmanager
    def _open_directory_descriptor(
        self,
        candidate,
        create=False,
        created_entries=None,
    ):
        """Open a contained directory by walking pinned no-follow handles."""
        self.require_secure_mutation()
        descriptor = os.dup(self._root_descriptor)
        traversed = []
        try:
            for part in self.relative_parts(candidate):
                traversed.append(part)
                created = False
                try:
                    before = os.stat(
                        part,
                        dir_fd=descriptor,
                        follow_symlinks=False,
                    )
                except FileNotFoundError:
                    if not create:
                        raise UnsafeCleanupPathError(
                            "Required directory no longer exists."
                        ) from None
                    os.mkdir(part, mode=0o700, dir_fd=descriptor)
                    before = os.stat(
                        part,
                        dir_fd=descriptor,
                        follow_symlinks=False,
                    )
                    created = True

                if not stat.S_ISDIR(before.st_mode):
                    raise UnsafeCleanupPathError(
                        "Refusing a non-directory or symbolic-link component."
                    )

                child = os.open(
                    part,
                    os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW,
                    dir_fd=descriptor,
                )
                after = os.fstat(child)
                if (before.st_dev, before.st_ino) != (
                    after.st_dev,
                    after.st_ino,
                ):
                    os.close(child)
                    raise UnsafeCleanupPathError(
                        "Directory changed during secure traversal."
                    )

                os.close(descriptor)
                descriptor = child
                if created and created_entries is not None:
                    created_entries.append(
                        (
                            "directory",
                            self.root.joinpath(*traversed),
                            (after.st_dev, after.st_ino),
                        )
                    )
            yield descriptor
        finally:
            os.close(descriptor)

    @contextlib.contextmanager
    def _open_parent_descriptor(
        self,
        candidate,
        create=False,
        created_entries=None,
    ):
        """Open a candidate's parent and yield its final component."""
        lexical = self._lexical_within_root(candidate)
        parts = self.relative_parts(lexical)
        if not parts:
            raise UnsafeCleanupPathError(
                "The cleanup root cannot be used as a mutation target."
            )
        parent = self.root.joinpath(*parts[:-1])
        with self._open_directory_descriptor(
            parent,
            create=create,
            created_entries=created_entries,
        ) as descriptor:
            yield descriptor, parts[-1]

    def _portable_candidate(self, candidate, expected_type, allow_root=False):
        """Validate a pretend-mode candidate without following symlinks."""
        lexical = self._reject_symlink_components(candidate)
        if lexical == self.root:
            if not allow_root:
                raise UnsafeCleanupPathError(
                    "The cleanup root cannot be used as a mutation target."
                )
            try:
                candidate_stat = lexical.lstat()
            except FileNotFoundError:
                raise UnsafeCleanupPathError(
                    "Required path no longer exists."
                ) from None
        else:
            try:
                candidate_stat = lexical.lstat()
            except FileNotFoundError:
                raise UnsafeCleanupPathError(
                    "Required path no longer exists."
                ) from None
        if not expected_type(candidate_stat.st_mode):
            raise UnsafeCleanupPathError("Unexpected filesystem object type.")
        return lexical, candidate_stat

    def _descriptor_candidate(self, candidate, expected_type, allow_root=False):
        """Validate a candidate relative to the pinned cleanup root."""
        lexical = self._lexical_within_root(candidate)
        if lexical == self.root:
            if not allow_root:
                raise UnsafeCleanupPathError(
                    "The cleanup root cannot be used as a mutation target."
                )
            candidate_stat = os.fstat(self._root_descriptor)
        else:
            with self._open_parent_descriptor(lexical) as (parent, name):
                try:
                    candidate_stat = os.stat(
                        name,
                        dir_fd=parent,
                        follow_symlinks=False,
                    )
                except FileNotFoundError:
                    raise UnsafeCleanupPathError(
                        "Required path no longer exists."
                    ) from None
        if not expected_type(candidate_stat.st_mode):
            raise UnsafeCleanupPathError(
                "Refusing a symbolic link or unexpected filesystem object."
            )
        return lexical, candidate_stat

    def _candidate(self, candidate, expected_type, allow_root=False):
        """Validate a path without erasing the lexical mutation identity."""
        if self.secure_mutation_supported:
            return self._descriptor_candidate(
                candidate,
                expected_type,
                allow_root=allow_root,
            )
        return self._portable_candidate(
            candidate,
            expected_type,
            allow_root=allow_root,
        )

    def existing_directory(self, candidate, allow_root=True):
        """Return an existing lexical directory after rejecting symlinks."""
        lexical, _ = self._candidate(
            candidate,
            stat.S_ISDIR,
            allow_root=allow_root,
        )
        return lexical

    def directory_details(self, candidate, allow_root=False):
        """Return a lexical directory and its stable stat metadata."""
        return self._candidate(
            candidate,
            stat.S_ISDIR,
            allow_root=allow_root,
        )

    def existing_file(self, candidate):
        """Return an existing lexical regular file after rejecting symlinks."""
        lexical, _ = self._candidate(candidate, stat.S_ISREG)
        return lexical

    def file_details(self, candidate):
        """Return a lexical regular file and its stable stat metadata."""
        return self._candidate(candidate, stat.S_ISREG)

    def file_identity(self, candidate):
        """Return a contained regular file's stable device/inode identity."""
        _, candidate_stat = self._candidate(candidate, stat.S_ISREG)
        return candidate_stat.st_dev, candidate_stat.st_ino

    def directory_identity(self, candidate, allow_root=False):
        """Return a contained directory's stable device/inode identity."""
        _, candidate_stat = self._candidate(
            candidate,
            stat.S_ISDIR,
            allow_root=allow_root,
        )
        return candidate_stat.st_dev, candidate_stat.st_ino

    @contextlib.contextmanager
    def open_regular_file(self, candidate):
        """Open a regular file without following any path symlinks."""
        lexical = self._lexical_within_root(candidate)
        if self.secure_mutation_supported:
            with self._open_parent_descriptor(lexical) as (parent, name):
                descriptor = os.open(
                    name,
                    os.O_RDONLY | os.O_NOFOLLOW,
                    dir_fd=parent,
                )
            candidate_stat = os.fstat(descriptor)
            if not stat.S_ISREG(candidate_stat.st_mode):
                os.close(descriptor)
                raise UnsafeCleanupPathError("Expected a regular file.")
            with os.fdopen(descriptor, "rb") as file_handle:
                yield file_handle
            return

        lexical, before = self._portable_candidate(
            lexical,
            stat.S_ISREG,
        )
        flags = os.O_RDONLY
        if hasattr(os, "O_NOFOLLOW"):
            flags |= os.O_NOFOLLOW
        descriptor = os.open(lexical, flags)
        after = os.fstat(descriptor)
        if (
            not stat.S_ISREG(after.st_mode)
            or self._identity(after) != self._identity(before)
        ):
            os.close(descriptor)
            raise UnsafeCleanupPathError(
                "File changed during portable no-follow validation."
            )
        with os.fdopen(descriptor, "rb") as file_handle:
            yield file_handle

    @staticmethod
    def _identity(candidate_stat):
        """Return the comparison identity for a stat result."""
        return candidate_stat.st_dev, candidate_stat.st_ino

    @staticmethod
    def _rename_noreplace(
        source_parent,
        source_name,
        destination_parent,
        destination_name,
    ):
        """Atomically rename one entry without replacing the destination."""
        if _RENAMEAT2 is None:
            raise UnsafeCleanupPathError(
                "Atomic no-replace rename is unavailable."
            )
        result = _RENAMEAT2(
            source_parent,
            os.fsencode(source_name),
            destination_parent,
            os.fsencode(destination_name),
            RENAME_NOREPLACE,
        )
        if result != 0:
            error_number = ctypes.get_errno()
            raise OSError(error_number, os.strerror(error_number))

    def _ensure_quarantine(self):
        """Create and pin a private root-local quarantine directory."""
        self.require_secure_mutation()
        if self._quarantine_descriptor is not None:
            return self._quarantine_descriptor

        for _ in range(10):
            quarantine_name = (
                ".melodee-cleanup-quarantine-" + secrets.token_hex(16)
            )
            try:
                os.mkdir(
                    quarantine_name,
                    mode=0o700,
                    dir_fd=self._root_descriptor,
                )
            except FileExistsError:
                continue
            before = os.stat(
                quarantine_name,
                dir_fd=self._root_descriptor,
                follow_symlinks=False,
            )
            descriptor = os.open(
                quarantine_name,
                os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW,
                dir_fd=self._root_descriptor,
            )
            after = os.fstat(descriptor)
            if (
                not stat.S_ISDIR(after.st_mode)
                or self._identity(before) != self._identity(after)
            ):
                os.close(descriptor)
                raise UnsafeCleanupPathError(
                    "Quarantine changed while it was being opened."
                )
            self._quarantine_name = quarantine_name
            self._quarantine_descriptor = descriptor
            return descriptor
        raise UnsafeCleanupPathError("Unable to create a private quarantine.")

    def _release_empty_quarantine(self):
        """Remove the private quarantine as soon as it becomes empty."""
        descriptor = self._quarantine_descriptor
        quarantine_name = self._quarantine_name
        if descriptor is None or quarantine_name is None:
            return
        try:
            if os.listdir(descriptor):
                return
            os.rmdir(quarantine_name, dir_fd=self._root_descriptor)
        except OSError:
            return
        os.close(descriptor)
        self._quarantine_descriptor = None
        self._quarantine_name = None

    def _new_quarantine_slot(self):
        """Return an unpredictable quarantine entry name."""
        return "entry-" + secrets.token_hex(16)

    def _restore_quarantined_entry(
        self,
        quarantine_slot,
        destination_parent,
        destination_name,
    ):
        """Restore an entry if its original name remains unoccupied."""
        try:
            self._rename_noreplace(
                self._quarantine_descriptor,
                quarantine_slot,
                destination_parent,
                destination_name,
            )
        except OSError as error:
            if error.errno != errno.EEXIST:
                raise
            return False
        return True

    def _move_expected_entry_to_quarantine(
        self,
        source_parent,
        source_name,
        expected_type,
        expected_identity=None,
    ):
        """Atomically isolate an entry, then verify its type and identity."""
        source_stat = os.stat(
            source_name,
            dir_fd=source_parent,
            follow_symlinks=False,
        )
        if not expected_type(source_stat.st_mode):
            raise UnsafeCleanupPathError("Unexpected mutation target type.")
        identity = self._identity(source_stat)
        if expected_identity is not None and identity != tuple(expected_identity):
            raise UnsafeCleanupPathError(
                "Mutation target changed before quarantine."
            )

        quarantine = self._ensure_quarantine()
        quarantine_slot = self._new_quarantine_slot()
        self._rename_noreplace(
            source_parent,
            source_name,
            quarantine,
            quarantine_slot,
        )
        quarantined_stat = os.stat(
            quarantine_slot,
            dir_fd=quarantine,
            follow_symlinks=False,
        )
        if (
            not expected_type(quarantined_stat.st_mode)
            or self._identity(quarantined_stat) != identity
        ):
            self._restore_quarantined_entry(
                quarantine_slot,
                source_parent,
                source_name,
            )
            self._release_empty_quarantine()
            raise UnsafeCleanupPathError(
                "Mutation target changed during quarantine."
            )
        return quarantine, quarantine_slot

    def unlink_regular_file(self, candidate, expected_identity=None):
        """Quarantine and unlink exactly the expected regular file."""
        self.require_secure_mutation()
        with self._open_parent_descriptor(candidate) as (parent, name):
            quarantine, quarantine_slot = self._move_expected_entry_to_quarantine(
                parent,
                name,
                stat.S_ISREG,
                expected_identity,
            )
            try:
                os.unlink(quarantine_slot, dir_fd=quarantine)
            except OSError:
                self._restore_quarantined_entry(
                    quarantine_slot,
                    parent,
                    name,
                )
                raise
            finally:
                self._release_empty_quarantine()

    def remove_empty_directory(self, candidate, expected_identity=None):
        """Quarantine and remove exactly the expected empty directory."""
        self.require_secure_mutation()
        with self._open_parent_descriptor(candidate) as (parent, name):
            quarantine, quarantine_slot = self._move_expected_entry_to_quarantine(
                parent,
                name,
                stat.S_ISDIR,
                expected_identity,
            )
            try:
                os.rmdir(quarantine_slot, dir_fd=quarantine)
            except OSError:
                self._restore_quarantined_entry(
                    quarantine_slot,
                    parent,
                    name,
                )
                raise
            finally:
                self._release_empty_quarantine()

    def remove_tree(self, candidate, expected_identity=None):
        """Quarantine and recursively remove exactly one expected tree."""
        self.require_secure_mutation()
        with self._open_parent_descriptor(candidate) as (parent, name):
            quarantine, quarantine_slot = self._move_expected_entry_to_quarantine(
                parent,
                name,
                stat.S_ISDIR,
                expected_identity,
            )
            try:
                shutil.rmtree(quarantine_slot, dir_fd=quarantine)
            except OSError:
                self._restore_quarantined_entry(
                    quarantine_slot,
                    parent,
                    name,
                )
                raise
            finally:
                self._release_empty_quarantine()

    def rename_regular_file_no_replace(
        self,
        source,
        destination,
        expected_identity=None,
    ):
        """Atomically move exactly one expected file without replacement."""
        self.require_secure_mutation()
        with self._open_parent_descriptor(source) as (source_parent, source_name):
            source_stat = os.stat(
                source_name,
                dir_fd=source_parent,
                follow_symlinks=False,
            )
            if not stat.S_ISREG(source_stat.st_mode):
                raise UnsafeCleanupPathError("Refusing to rename a non-file.")
            source_identity = self._identity(source_stat)
            if expected_identity is not None and source_identity != tuple(
                expected_identity
            ):
                raise UnsafeCleanupPathError(
                    "Source file changed before it could be renamed."
                )

            with self._open_parent_descriptor(destination) as (
                destination_parent,
                destination_name,
            ):
                self._rename_noreplace(
                    source_parent,
                    source_name,
                    destination_parent,
                    destination_name,
                )
                destination_stat = os.stat(
                    destination_name,
                    dir_fd=destination_parent,
                    follow_symlinks=False,
                )
                if (
                    not stat.S_ISREG(destination_stat.st_mode)
                    or self._identity(destination_stat) != source_identity
                ):
                    try:
                        self._rename_noreplace(
                            destination_parent,
                            destination_name,
                            source_parent,
                            source_name,
                        )
                    except OSError as error:
                        if error.errno != errno.EEXIST:
                            raise
                        quarantine = self._ensure_quarantine()
                        self._rename_noreplace(
                            destination_parent,
                            destination_name,
                            quarantine,
                            self._new_quarantine_slot(),
                        )
                    raise UnsafeCleanupPathError(
                        "Rename source changed during publication."
                    )

    def check_zip_target_available(self, candidate, is_directory):
        """Reject symlinked parents and existing archive file targets."""
        self.require_secure_mutation()
        parts = self.relative_parts(candidate)
        descriptor = os.dup(self._root_descriptor)
        try:
            for index, part in enumerate(parts):
                final = index == len(parts) - 1
                try:
                    candidate_stat = os.stat(
                        part,
                        dir_fd=descriptor,
                        follow_symlinks=False,
                    )
                except FileNotFoundError:
                    return
                if not stat.S_ISDIR(candidate_stat.st_mode):
                    if final and is_directory:
                        raise UnsafeArchiveError(
                            "Archive directory conflicts with an existing object."
                        )
                    if final:
                        raise UnsafeArchiveError(
                            "Archive file target already exists."
                        )
                    raise UnsafeArchiveError(
                        "Archive target has a non-directory parent."
                    )
                if final:
                    if not is_directory:
                        raise UnsafeArchiveError(
                            "Archive file target already exists."
                        )
                    return
                child = os.open(
                    part,
                    os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW,
                    dir_fd=descriptor,
                )
                os.close(descriptor)
                descriptor = child
        finally:
            os.close(descriptor)

    def ensure_directory(self, candidate, created_entries):
        """Create missing directory components with no-follow traversal."""
        with self._open_directory_descriptor(
            candidate,
            create=True,
            created_entries=created_entries,
        ):
            pass

    def create_regular_file_exclusive(self, candidate, created_entries):
        """Create a new archive output without following or truncating links."""
        self.require_secure_mutation()
        with self._open_parent_descriptor(
            candidate,
            create=True,
            created_entries=created_entries,
        ) as (parent, name):
            descriptor = os.open(
                name,
                os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW,
                0o600,
                dir_fd=parent,
            )
        candidate_stat = os.fstat(descriptor)
        if not stat.S_ISREG(candidate_stat.st_mode):
            os.close(descriptor)
            raise UnsafeArchiveError("Archive output is not a regular file.")
        created_entries.append(
            (
                "file",
                self._lexical_within_root(candidate),
                self._identity(candidate_stat),
            )
        )
        return descriptor

    def rollback_created_entries(self, created_entries):
        """Remove only archive outputs whose identities still match."""
        for entry_type, candidate, expected_identity in reversed(created_entries):
            try:
                if entry_type == "file":
                    self.unlink_regular_file(candidate, expected_identity)
                else:
                    self.remove_empty_directory(candidate, expected_identity)
            except (OSError, UnsafeCleanupPathError):
                continue


def validate_relative_member(filename, source_name):
    """Validate a ZIP or SFV filename as a portable relative path."""
    if not filename or "\x00" in filename:
        raise UnsafeCleanupPathError("Invalid empty relative filename.")

    windows_path = PureWindowsPath(filename)
    normalized = filename.replace("\\", "/")
    posix_path = PurePosixPath(normalized)
    if windows_path.drive or windows_path.is_absolute() or posix_path.is_absolute():
        raise UnsafeCleanupPathError(
            "Absolute archive or checksum filename is not allowed."
        )

    raw_parts = [part for part in normalized.split("/") if part not in ("", ".")]
    if not raw_parts or ".." in raw_parts:
        raise UnsafeCleanupPathError(
            "Parent-traversing filename is not allowed."
        )

    reserved_devices = {
        "AUX",
        "CON",
        "CONIN$",
        "CONOUT$",
        "NUL",
        "PRN",
        *(f"COM{number}" for number in range(1, 10)),
        *(f"LPT{number}" for number in range(1, 10)),
        "COM¹",
        "COM²",
        "COM³",
        "LPT¹",
        "LPT²",
        "LPT³",
    }
    reserved_characters = set('<>:"|?*')
    for part in raw_parts:
        base_name = part.split(".", 1)[0].rstrip(" .").upper()
        if (
            part.startswith(" ")
            or part.endswith((" ", "."))
            or any(ord(character) < 32 for character in part)
            or any(character in reserved_characters for character in part)
            or base_name in reserved_devices
        ):
            raise UnsafeCleanupPathError(
                "Windows-reserved filename is not allowed."
            )
    return Path(*raw_parts)

# ANSI color codes for modern output
class Colors:
    RED = '\033[91m'
    GREEN = '\033[92m'
    YELLOW = '\033[93m'
    BLUE = '\033[94m'
    MAGENTA = '\033[95m'
    CYAN = '\033[96m'
    WHITE = '\033[97m'
    BOLD = '\033[1m'
    UNDERLINE = '\033[4m'
    RESET = '\033[0m'
    
    # Background colors
    BG_RED = '\033[101m'
    BG_GREEN = '\033[102m'
    BG_BLUE = '\033[104m'
    BG_YELLOW = '\033[103m'

# Statistics tracking
stats = defaultdict(int)
start_time = time.time()

def _portable_path_key(path_guard, candidate):
    """Return a case-insensitive portable key for a contained path."""
    return tuple(
        unicodedata.normalize("NFC", part).casefold()
        for part in path_guard.relative_parts(candidate)
    )


def validated_zip_members(
    zip_ref,
    destination,
    path_guard,
    source_archive=None,
    protected_archives=(),
    check_filesystem_targets=True,
):
    """Preflight all archive members before writing archive content."""
    if check_filesystem_targets:
        path_guard.require_secure_mutation()
    members = zip_ref.infolist()
    if len(members) > MAX_ZIP_MEMBERS:
        raise UnsafeArchiveError("Archive contains too many members.")

    validated_members = []
    known_paths = {}
    total_size = 0
    protected_keys = {
        _portable_path_key(path_guard, archive)
        for archive in protected_archives
    }
    if source_archive is not None:
        protected_keys.add(_portable_path_key(path_guard, source_archive))

    for member in members:
        relative_path = validate_relative_member(member.filename, "ZIP member")
        unix_mode = member.external_attr >> 16
        if stat.S_ISLNK(unix_mode):
            raise UnsafeArchiveError("Symbolic-link ZIP members are not allowed.")
        if member.flag_bits & 0x1:
            raise UnsafeArchiveError("Encrypted ZIP members are not supported.")
        if member.compress_type not in SUPPORTED_ZIP_COMPRESSION:
            raise UnsafeArchiveError(
                "ZIP member uses an unsupported compression method."
            )
        if member.file_size < 0 or member.file_size > MAX_ZIP_MEMBER_BYTES:
            raise UnsafeArchiveError("ZIP member exceeds the expanded-size limit.")
        total_size += member.file_size
        if total_size > MAX_ZIP_TOTAL_BYTES:
            raise UnsafeArchiveError("ZIP archive exceeds the total-size limit.")
        if member.file_size:
            compression_ratio = member.file_size / max(member.compress_size, 1)
            if compression_ratio > MAX_ZIP_COMPRESSION_RATIO:
                raise UnsafeArchiveError(
                    "ZIP member exceeds the compression-ratio limit."
                )

        target = path_guard._lexical_within_root(destination / relative_path)
        target_key = _portable_path_key(path_guard, target)
        if target_key in protected_keys:
            raise UnsafeArchiveError(
                "ZIP member conflicts with a protected archive source."
            )
        if target_key in known_paths:
            raise UnsafeArchiveError(
                "ZIP members have duplicate portable destinations."
            )

        for index in range(1, len(target_key)):
            parent_key = target_key[:index]
            if known_paths.get(parent_key) == "file":
                raise UnsafeArchiveError(
                    "ZIP member conflicts with an archive file parent."
                )
        is_directory = member.is_dir()
        if not is_directory and any(
            existing_key[: len(target_key)] == target_key
            for existing_key in known_paths
            if len(existing_key) > len(target_key)
        ):
            raise UnsafeArchiveError(
                "ZIP file conflicts with an archive directory prefix."
            )

        if check_filesystem_targets:
            path_guard.check_zip_target_available(target, is_directory)
        known_paths[target_key] = "directory" if is_directory else "file"
        validated_members.append((member, target, is_directory))
    return validated_members


def extract_validated_zip(zip_ref, validated_members, path_guard):
    """Extract preflighted ZIP members and roll back failed output."""
    path_guard.require_secure_mutation()
    created_entries = []
    try:
        for member, target, is_directory in validated_members:
            if shutdown_requested:
                raise CleanupInterruptedError("Archive extraction interrupted.")
            if is_directory:
                path_guard.ensure_directory(target, created_entries)
                continue

            descriptor = path_guard.create_regular_file_exclusive(
                target,
                created_entries,
            )
            copied_bytes = 0
            try:
                with zip_ref.open(member, "r") as source:
                    with os.fdopen(descriptor, "wb") as output:
                        descriptor = -1
                        while True:
                            if shutdown_requested:
                                raise CleanupInterruptedError(
                                    "Archive extraction interrupted."
                                )
                            chunk = source.read(ZIP_COPY_CHUNK_BYTES)
                            if not chunk:
                                break
                            copied_bytes += len(chunk)
                            if (
                                copied_bytes > member.file_size
                                or copied_bytes > MAX_ZIP_MEMBER_BYTES
                            ):
                                raise UnsafeArchiveError(
                                    "ZIP member expanded beyond its declared limit."
                                )
                            output.write(chunk)
                if copied_bytes != member.file_size:
                    raise UnsafeArchiveError(
                        "ZIP member size did not match its declaration."
                    )
            finally:
                if descriptor >= 0:
                    os.close(descriptor)
    except BaseException:
        path_guard.rollback_created_entries(created_entries)
        raise


def unzip_files_in_directory(dir_path, path_guard, pretend=True):
    """Safely unzip ZIP files found in a contained directory."""
    if not pretend:
        path_guard.require_secure_mutation()
    directory = path_guard.existing_directory(dir_path)
    zip_files = []
    protected_archives = [
        item
        for item in directory.iterdir()
        if item.name.lower().endswith(".zip")
    ]
    for item in protected_archives:
        if not item.name.lower().endswith(".zip"):
            continue
        try:
            zip_files.append(path_guard.existing_file(item))
        except (OSError, UnsafeCleanupPathError):
            print(
                f"  {Colors.RED}❌ Skipping unsafe ZIP path{Colors.RESET}"
            )

    for zip_file in sorted(zip_files, key=lambda path: path.name.casefold()):
        try:
            with path_guard.open_regular_file(zip_file) as archive_handle:
                archive_stat = os.fstat(archive_handle.fileno())
                archive_identity = (archive_stat.st_dev, archive_stat.st_ino)
                with zipfile.ZipFile(archive_handle, "r") as zip_ref:
                    if pretend:
                        validated_zip_members(
                            zip_ref,
                            directory,
                            path_guard,
                            source_archive=zip_file,
                            protected_archives=protected_archives,
                            check_filesystem_targets=(
                                path_guard.secure_mutation_supported
                            ),
                        )
                        print(
                            f"  {Colors.YELLOW}📦 Would unzip ZIP archive"
                            f"{Colors.RESET}"
                        )
                    else:
                        members = validated_zip_members(
                            zip_ref,
                            directory,
                            path_guard,
                            source_archive=zip_file,
                            protected_archives=protected_archives,
                        )
                        print(
                            f"  {Colors.GREEN}📦 Unzipping ZIP archive"
                            f"{Colors.RESET}"
                        )
                        extract_validated_zip(zip_ref, members, path_guard)

            if not pretend:
                path_guard.unlink_regular_file(zip_file, archive_identity)
            stats["zip_files_processed"] += 1
        except (
            CleanupInterruptedError,
            NotImplementedError,
            OSError,
            RuntimeError,
            UnsafeArchiveError,
            UnsafeCleanupPathError,
            ValueError,
            zipfile.BadZipFile,
        ) as error:
            print(
                f"  {Colors.RED}❌ Error processing ZIP archive "
                f"({type(error).__name__}){Colors.RESET}"
            )


def is_directory_empty(dir_path, path_guard):
    """Check if directory is empty (no files or subdirectories)."""
    try:
        directory = path_guard.existing_directory(dir_path)
        return next(directory.iterdir(), None) is None
    except (OSError, UnsafeCleanupPathError):
        return False

def format_size(size_bytes):
    """Convert bytes to human readable format."""
    if size_bytes == 0:
        return "0 B"
    size_names = ["B", "KB", "MB", "GB", "TB"]
    i = int(math.floor(math.log(size_bytes, 1024)))
    p = math.pow(1024, i)
    s = round(size_bytes / p, 2)
    return f"{s} {size_names[i]}"


def safe_extension_bucket(filename):
    """Return a bounded terminal-safe statistics bucket for a filename."""
    extension = os.path.splitext(filename)[1].lower().removeprefix(".")
    if not extension:
        return "no_extension"
    if (
        len(extension) <= 16
        and extension.isascii()
        and extension.isalnum()
    ):
        return extension
    return "other"

def get_directory_size(dir_path, path_guard, chunk_size=1000):
    """
    Calculate total size of directory with memory-efficient chunked processing.
    
    Args:
        dir_path: Path to directory
        chunk_size: Number of files to process in each chunk
    
    Returns:
        Total size in bytes
    """
    global shutdown_requested
    total_size = 0
    file_count = 0
    
    try:
        contained_directory = path_guard.existing_directory(dir_path)
        for dirpath, dirnames, filenames in os.walk(contained_directory):
            if shutdown_requested:
                break

            try:
                path_guard.existing_directory(dirpath)
            except UnsafeCleanupPathError:
                dirnames.clear()
                continue
                
            # Process files in chunks to manage memory usage
            for i in range(0, len(filenames), chunk_size):
                if shutdown_requested:
                    break
                    
                chunk = filenames[i:i + chunk_size]
                for filename in chunk:
                    try:
                        _, file_stat = path_guard.file_details(
                            Path(dirpath) / filename
                        )
                        total_size += file_stat.st_size
                        file_count += 1
                    except (OSError, UnsafeCleanupPathError):
                        pass
                
                # Small delay every chunk to allow for interruption
                if file_count % (chunk_size * 10) == 0:
                    time.sleep(0.001)
                    
    except (OSError, UnsafeCleanupPathError):
        pass
    return total_size

def should_delete(dirname, delete_dash_one=True):
    """Check if any of the keywords are in the directory name, if it contains a date, or ends with '-1'."""
    keyword_match = any(pattern.search(dirname) for pattern in KEYWORD_PATTERNS.values())
    dash_one_match = delete_dash_one and dirname.strip().endswith('-1')
    return keyword_match or contains_embedded_date(dirname) or dash_one_match

def highlight_deletion_reason(dirname, delete_dash_one=True):
    """
    Highlight the reason why a directory would be deleted.
    Returns the directory name with highlighted keywords/dates and reason.
    """
    highlighted_name = dirname
    reasons = []
    # Check for keywords and highlight them using pre-compiled patterns
    for keyword, pattern in KEYWORD_PATTERNS.items():
        if pattern.search(dirname):
            highlighted_name = pattern.sub(f"{Colors.BG_RED}{Colors.WHITE}{keyword.upper()}{Colors.RESET}", highlighted_name)
            reasons.append(f"keyword '{Colors.BOLD}{keyword}{Colors.RESET}'")
    # Check for embedded dates and highlight them
    if contains_embedded_date(dirname):
        matches = list(COMBINED_DATE_PATTERN.finditer(dirname))
        if matches:
            for match in reversed(matches):
                date_text = match.group()
                highlighted_date = f"{Colors.BG_YELLOW}{Colors.BOLD}{date_text}{Colors.RESET}"
                highlighted_name = highlighted_name[:match.start()] + highlighted_date + highlighted_name[match.end():]
            reasons.append("embedded date pattern")
    # Check for '-1' suffix
    if delete_dash_one and dirname.strip().endswith('-1'):
        highlighted_name = re.sub(r'(-1)$', f"{Colors.BG_RED}{Colors.WHITE}-1{Colors.RESET}", highlighted_name)
        reasons.append("ends with '-1' (likely duplicate)")
    if reasons:
        reason_str = f" {Colors.CYAN}[Reason: {', '.join(reasons)}]{Colors.RESET}"
    else:
        reason_str = ""
    return highlighted_name + reason_str

def contains_embedded_date(s: str) -> bool:
    """
    Returns True if the string contains a date in any of the following formats,
    but only if the string contains other text besides the date itself.
    Returns False if the string starts with a date followed by a '/' (e.g., '2025-07-10/...').
    Returns True if the string starts with a date + '/' but the rest ALSO contains a date.
    """
    # Check for date at the very start followed by '/' using pre-compiled pattern
    if START_DATE_PATTERN.match(s):
        # If there is another date after the '/', return True; else False
        rest = s.split('/', 1)[1]
        if COMBINED_DATE_PATTERN.search(rest):
            # Check that it's embedded, not just another date alone
            matches = list(COMBINED_DATE_PATTERN.finditer(rest))
            for match in matches:
                before = rest[:match.start()]
                after = rest[match.end():]
                if before.strip() or after.strip():
                    return True
            return False
        return False

    # Main logic for other cases using pre-compiled pattern
    matches = list(COMBINED_DATE_PATTERN.finditer(s))
    if not matches:
        return False
    for match in matches:
        before = s[:match.start()]
        after = s[match.end():]
        if before.strip() or after.strip():
            return True
    return False

def delete_matching_dirs(
    path_guard, pretend=True, check_sfv=True, delete_dash_one=True
):
    """Recursively process directories under root_dir with optimized single-pass processing."""
    global shutdown_requested
    if not pretend:
        path_guard.require_secure_mutation()
    deleted = 0
    root_dir = path_guard.existing_directory(path_guard.root)
    
    print(f"\n{Colors.CYAN}🔍 Scanning directories...{Colors.RESET}")
    
    # First, count total directories for progress indication
    total_dirs = 0
    total_files = 0
    if TQDM_AVAILABLE:
        print(f"{Colors.BLUE}📊 Counting items for progress tracking...{Colors.RESET}")
        for dirpath, dirnames, filenames in os.walk(root_dir):
            if shutdown_requested:
                return deleted
            try:
                path_guard.existing_directory(dirpath)
            except UnsafeCleanupPathError:
                dirnames.clear()
                continue
            total_dirs += len(dirnames)
            total_files += len(filenames)
    
    # Single-pass processing with progress indication
    processed_dirs = 0
    # Collect all directories and files in a single walk
    print(f"\n{Colors.MAGENTA}🔄 Processing directories and files...{Colors.RESET}")
    
    # Use progress bar if available
    pbar = None
    if TQDM_AVAILABLE and total_dirs > 0:
        pbar = tqdm(total=total_dirs, desc="Processing directories", 
                   bar_format='{desc}: {percentage:3.0f}%|{bar}| {n_fmt}/{total_fmt} [{elapsed}<{remaining}]')
    
    try:
        for dirpath, dirnames, filenames in os.walk(root_dir, topdown=False):
            if shutdown_requested:
                print(f"\n{Colors.YELLOW}🛑 Operation interrupted by user{Colors.RESET}")
                break

            try:
                contained_dirpath = path_guard.existing_directory(dirpath)
            except UnsafeCleanupPathError:
                print(f"  {Colors.RED}❌ Skipping unsafe directory{Colors.RESET}")
                continue
            
            # Update statistics for files in this directory
            stats['total_files'] += len(filenames)
            
            # Count file types and calculate total file size
            for filename in filenames:
                if shutdown_requested:
                    break
                    
                ext = os.path.splitext(filename)[1].lower()
                stats[f"files_{safe_extension_bucket(filename)}"] += 1
                    
                # Calculate total file size
                try:
                    file_path, file_stat = path_guard.file_details(
                        contained_dirpath / filename
                    )
                    file_identity = (file_stat.st_dev, file_stat.st_ino)
                    file_size = file_stat.st_size
                    stats['total_size_bytes'] += file_size
                    
                    # Check if this is an image file that should be deleted
                    if ext in IMAGE_EXTENSIONS:
                        should_delete_img, _reason = should_delete_image(
                            file_path, filename, path_guard
                        )
                        if should_delete_img:
                            if pretend:
                                print(f"  {Colors.YELLOW}🖼️  Would delete image matched by policy{Colors.RESET}")
                                print(f"    {Colors.CYAN}Size:{Colors.RESET} {format_size(file_size)}")
                                stats['images_deleted'] += 1
                                stats['total_size_deleted_bytes'] += file_size
                            else:
                                print(f"  {Colors.RED}🖼️  Deleting image matched by policy{Colors.RESET}")
                                print(f"    {Colors.CYAN}Size:{Colors.RESET} {format_size(file_size)}")
                                try:
                                    path_guard.unlink_regular_file(
                                        file_path,
                                        file_identity,
                                    )
                                    stats['images_deleted'] += 1
                                    stats['total_size_deleted_bytes'] += file_size
                                except (OSError, UnsafeCleanupPathError):
                                    print(f"    {Colors.RED}❌ Error deleting image{Colors.RESET}")
                        
                except (OSError, UnsafeCleanupPathError):
                    pass
            
            # Process ZIP files in this directory
            if filenames and not shutdown_requested:
                zip_files = [f for f in filenames if f.lower().endswith('.zip')]
                if zip_files:
                    print(f"\n{Colors.BLUE}📂 Processing ZIP files in current directory{Colors.RESET}")
                    unzip_files_in_directory(contained_dirpath, path_guard, pretend)
            
            # Process directories for potential deletion
            for dirname in dirnames:
                if shutdown_requested:
                    break
                    
                full_path = contained_dirpath / dirname
                processed_dirs += 1
                stats['total_directories'] += 1
                
                # Update progress bar
                if pbar:
                    pbar.update(1)
                
                # Check if directory should be deleted based on keywords/dates or '-1' suffix first
                if should_delete(dirname, delete_dash_one=delete_dash_one):
                    try:
                        full_path, full_path_stat = path_guard.directory_details(
                            full_path
                        )
                        full_path_identity = (
                            full_path_stat.st_dev,
                            full_path_stat.st_ino,
                        )
                        dir_size = get_directory_size(full_path, path_guard)
                    except UnsafeCleanupPathError:
                        print(
                            f"  {Colors.RED}❌ Skipping unsafe directory"
                            f"{Colors.RESET}"
                        )
                        continue
                    if pretend:
                        print(f"  {Colors.YELLOW}🗑️  Would delete directory matched by policy{Colors.RESET}")
                        print(f"    {Colors.CYAN}Size:{Colors.RESET} {format_size(dir_size)}")
                        stats['keyword_directories_deleted'] += 1
                        deleted += 1
                        stats['total_size_deleted_bytes'] += dir_size
                    else:
                        print(f"  {Colors.RED}🗑️  Deleting directory matched by policy{Colors.RESET}")
                        print(f"    {Colors.CYAN}Size:{Colors.RESET} {format_size(dir_size)}")
                        try:
                            path_guard.remove_tree(
                                full_path,
                                full_path_identity,
                            )
                            stats['keyword_directories_deleted'] += 1
                            deleted += 1
                            stats['total_size_deleted_bytes'] += dir_size
                        except (OSError, UnsafeCleanupPathError):
                            print(f"    {Colors.RED}❌ Error deleting directory{Colors.RESET}")
                    continue
                
                # Check SFV integrity (only if not already marked for deletion and SFV checking is enabled)
                if check_sfv:
                    try:
                        full_path, _ = path_guard.directory_details(full_path)
                    except UnsafeCleanupPathError:
                        print(
                            f"  {Colors.RED}❌ Skipping unsafe directory"
                            f"{Colors.RESET}"
                        )
                        continue
                    should_delete_sfv, _sfv_reason, sfv_details, sfv_target_path = check_sfv_integrity(
                        full_path, path_guard, pretend=pretend
                    )
                    if should_delete_sfv:
                        # Use the target path (might be parent directory if SFV is in 'extr')
                        try:
                            sfv_target_path, sfv_target_stat = (
                                path_guard.directory_details(sfv_target_path)
                            )
                        except UnsafeCleanupPathError:
                            print(
                                f"  {Colors.RED}❌ Refusing unsafe SFV "
                                f"deletion target{Colors.RESET}"
                            )
                            continue
                        sfv_target_identity = (
                            sfv_target_stat.st_dev,
                            sfv_target_stat.st_ino,
                        )
                        dir_size = get_directory_size(sfv_target_path, path_guard)
                        
                        if pretend:
                            print(f"  {Colors.YELLOW}🗑️  Would delete directory whose SFV failed{Colors.RESET}")
                            print(f"    {Colors.CYAN}Size:{Colors.RESET} {format_size(dir_size)}")
                            print(f"    {Colors.CYAN}[Reason: SFV verification failed]{Colors.RESET}")
                            if sfv_details:
                                print_sfv_details(sfv_details, dirname)
                            stats['sfv_failed_directories_deleted'] += 1
                            deleted += 1
                            stats['total_size_deleted_bytes'] += dir_size
                        else:
                            print(f"  {Colors.RED}🗑️  Deleting directory whose SFV failed{Colors.RESET}")
                            print(f"    {Colors.CYAN}Size:{Colors.RESET} {format_size(dir_size)}")
                            print(f"    {Colors.CYAN}[Reason: SFV verification failed]{Colors.RESET}")
                            if sfv_details:
                                print_sfv_details(sfv_details, dirname)
                            try:
                                path_guard.remove_tree(
                                    sfv_target_path,
                                    sfv_target_identity,
                                )
                                stats['sfv_failed_directories_deleted'] += 1
                                deleted += 1
                                stats['total_size_deleted_bytes'] += dir_size
                            except (OSError, UnsafeCleanupPathError):
                                print(f"    {Colors.RED}❌ Error deleting directory{Colors.RESET}")
                        continue
                
                # Check if directory is empty and delete it (only if not already matched above)
                elif is_directory_empty(full_path, path_guard):
                    if pretend:
                        print(f"  {Colors.YELLOW}🗂️  Would delete empty directory{Colors.RESET}")
                        stats['empty_directories_deleted'] += 1
                        deleted += 1
                    else:
                        print(f"  {Colors.GREEN}🗂️  Deleting empty directory{Colors.RESET}")
                        try:
                            _, empty_stat = path_guard.directory_details(full_path)
                            path_guard.remove_empty_directory(
                                full_path,
                                (empty_stat.st_dev, empty_stat.st_ino),
                            )
                            stats['empty_directories_deleted'] += 1
                            deleted += 1
                        except (OSError, UnsafeCleanupPathError):
                            print(f"    {Colors.RED}❌ Error deleting directory{Colors.RESET}")
                            continue
                
                # Small delay to allow for interruption on large operations
                if processed_dirs % 100 == 0:
                    time.sleep(0.001)
    
    finally:
        if pbar:
            pbar.close()
        
        if shutdown_requested:
            print(f"\n{Colors.YELLOW}⚠️  Operation was interrupted. Partial results displayed.{Colors.RESET}")
    
    return deleted

def print_statistics():
    """Print comprehensive statistics about the operation."""
    end_time = time.time()
    duration = end_time - start_time
    
    print(f"\n{Colors.BOLD}{Colors.BG_BLUE}                    OPERATION STATISTICS                    {Colors.RESET}")
    print(f"{Colors.BOLD}{'='*60}{Colors.RESET}")
    
    # Time and performance stats
    print(f"{Colors.CYAN}⏱️  Execution Time:{Colors.RESET} {duration:.2f} seconds")
    
    # Directory and file statistics
    print(f"\n{Colors.BOLD}{Colors.UNDERLINE}📁 Directory & File Overview:{Colors.RESET}")
    print(f"  {Colors.GREEN}📂 Total directories scanned:{Colors.RESET} {Colors.BOLD}{stats['total_directories']:,}{Colors.RESET}")
    print(f"  {Colors.GREEN}📄 Total files found:{Colors.RESET} {Colors.BOLD}{stats['total_files']:,}{Colors.RESET}")
    print(f"  {Colors.GREEN}💾 Total data size:{Colors.RESET} {Colors.BOLD}{format_size(stats['total_size_bytes'])}{Colors.RESET}")
    
    # File type breakdown
    file_types = []
    for key, value in stats.items():
        if key.startswith('files_') and value > 0:
            ext = key.replace('files_', '')
            if ext == 'no_extension':
                file_types.append(f"No extension: {value:,}")
            else:
                file_types.append(f".{ext}: {value:,}")
    
    if file_types:
        print(f"\n{Colors.BOLD}{Colors.UNDERLINE}📊 File Type Breakdown:{Colors.RESET}")
        for i, file_type in enumerate(sorted(file_types)[:10]):  # Show top 10
            print(f"  {Colors.BLUE}▪️{Colors.RESET} {file_type}")
        if len(file_types) > 10:
            print(f"  {Colors.YELLOW}... and {len(file_types) - 10} more file types{Colors.RESET}")
    
    # Processing statistics
    print(f"\n{Colors.BOLD}{Colors.UNDERLINE}🔧 Processing Results:{Colors.RESET}")
    print(f"  {Colors.MAGENTA}📦 ZIP files processed:{Colors.RESET} {Colors.BOLD}{stats['zip_files_processed']}{Colors.RESET}")
    print(f"  {Colors.YELLOW}🗂️  Empty directories removed:{Colors.RESET} {Colors.BOLD}{stats['empty_directories_deleted']}{Colors.RESET}")
    print(f"  {Colors.RED}🗑️  Keyword/date directories removed:{Colors.RESET} {Colors.BOLD}{stats['keyword_directories_deleted']}{Colors.RESET}")
    print(f"  {Colors.RED}📋 SFV failed directories removed:{Colors.RESET} {Colors.BOLD}{stats.get('sfv_failed_directories_deleted', 0)}{Colors.RESET}")
    print(f"  {Colors.CYAN}🖼️  Images deleted:{Colors.RESET} {Colors.BOLD}{stats['images_deleted']}{Colors.RESET}")
    
    total_deleted = stats['empty_directories_deleted'] + stats['keyword_directories_deleted'] + stats.get('sfv_failed_directories_deleted', 0)
    print(f"\n{Colors.BOLD}{Colors.BG_GREEN} TOTAL DIRECTORIES DELETED: {total_deleted} {Colors.RESET}")
    
    if stats.get('images_deleted', 0) > 0:
        print(f"{Colors.BOLD}{Colors.BG_BLUE} TOTAL IMAGES DELETED: {stats['images_deleted']} {Colors.RESET}")
    
    if stats.get('total_size_deleted_bytes', 0) > 0:
        print(f"{Colors.BOLD}{Colors.BG_RED} TOTAL DATA FREED: {format_size(stats['total_size_deleted_bytes'])} {Colors.RESET}")
    
    # Efficiency metrics
    if stats['total_directories'] > 0:
        deletion_rate = (total_deleted / stats['total_directories']) * 100
        print(f"\n{Colors.CYAN}📈 Deletion Rate:{Colors.RESET} {deletion_rate:.1f}% of directories processed")
    
    print(f"{Colors.BOLD}{'='*60}{Colors.RESET}")
    
    if total_deleted > 0:
        print(f"{Colors.GREEN}✅ Cleanup completed successfully!{Colors.RESET}")
    else:
        print(f"{Colors.BLUE}ℹ️  No directories matched deletion criteria.{Colors.RESET}")

# Signal handler for graceful shutdown
def signal_handler(signum, frame):
    """Handle interrupt signals gracefully."""
    global shutdown_requested
    print(f"\n{Colors.YELLOW}🛑 Interrupt received. Finishing current operation...{Colors.RESET}")
    shutdown_requested = True
    
def setup_signal_handlers():
    """Setup signal handlers for graceful shutdown."""
    signal.signal(signal.SIGINT, signal_handler)
    signal.signal(signal.SIGTERM, signal_handler)

def get_image_dimensions(file_path, path_guard):
    """
    Get image dimensions using PIL.
    
    Args:
        file_path: Path to image file
    
    Returns:
        Tuple of (width, height) or None if unable to read
    """
    if not PIL_AVAILABLE:
        return None
    
    try:
        with path_guard.open_regular_file(file_path) as image_handle:
            with Image.open(image_handle) as img:
                return img.size
    except (
        OSError,
        IOError,
        Image.UnidentifiedImageError,
        UnsafeCleanupPathError,
    ):
        return None

def should_delete_image(file_path, filename, path_guard):
    """
    Check if an image file should be deleted based on size or "proof" in name.
    
    Args:
        file_path: Full path to the file
        filename: Just the filename
    
    Returns:
        Tuple of (should_delete: bool, reason: str)
    """
    # Check if filename contains "proof"
    if PROOF_PATTERN.search(filename):
        return True, "contains 'proof'"
    
    # Check image dimensions if PIL is available
    if PIL_AVAILABLE:
        dimensions = get_image_dimensions(file_path, path_guard)
        if dimensions:
            width, height = dimensions
            if width < 300 or height < 300:
                return True, f"small resolution ({width}x{height})"
    
    return False, ""

def calculate_crc32(file_path, path_guard):
    """
    Calculate CRC32 checksum for a file.
    
    Args:
        file_path: Path to the file
    
    Returns:
        CRC32 checksum as an 8-character uppercase hex string, or None if error
    """
    checksum, _ = calculate_crc32_with_identity(file_path, path_guard)
    return checksum


def calculate_crc32_with_identity(file_path, path_guard):
    """Calculate CRC32 and return the identity of the exact opened file."""
    try:
        crc = 0
        with path_guard.open_regular_file(file_path) as file_handle:
            file_stat = os.fstat(file_handle.fileno())
            while True:
                chunk = file_handle.read(65536)
                if not chunk:
                    break
                crc = zlib.crc32(chunk, crc)
        return (
            f"{crc & 0xffffffff:08X}",
            (file_stat.st_dev, file_stat.st_ino),
        )
    except (OSError, IOError, UnsafeCleanupPathError):
        return None, None

def parse_sfv_file(sfv_path, path_guard):
    """
    Parse an SFV file and return a dictionary of filename -> expected_crc32.
    Handles various SFV formats and encodings robustly.
    
    Args:
        sfv_path: Path to the SFV file
    
    Returns:
        Dictionary mapping relative filenames to expected CRC32 checksums
    """
    file_checksums = {}
    encodings = ["utf-8", "latin1", "cp1252", "ascii"]

    with path_guard.open_regular_file(sfv_path) as file_handle:
        sfv_stat = os.fstat(file_handle.fileno())
        if sfv_stat.st_size > MAX_SFV_BYTES:
            raise UnsafeCleanupPathError("SFV file exceeds the byte limit.")
        content = file_handle.read(MAX_SFV_BYTES + 1)
    if len(content) > MAX_SFV_BYTES:
        raise UnsafeCleanupPathError("SFV file exceeds the byte limit.")

    for encoding in encodings:
        try:
            decoded_content = content.decode(encoding)
            break
        except UnicodeDecodeError:
            continue
    else:
        print(
            f"  {Colors.RED}❌ Could not decode SFV file with a supported "
            f"encoding{Colors.RESET}"
        )
        return file_checksums

    lines = decoded_content.splitlines()
    if len(lines) > MAX_SFV_LINES:
        raise UnsafeCleanupPathError("SFV file exceeds the line limit.")

    for line_num, line in enumerate(lines, 1):
        line = line.strip()
        
        # Skip empty lines and comments
        if not line or line.startswith(';') or line.startswith('#'):
            continue
        
        # Handle various SFV formats
        # Format 1: filename.ext CRC32HASH
        # Format 2: filename.ext CRC32HASH size
        # Format 3: "filename with spaces.ext" CRC32HASH
        
        # Try quoted filename first
        if line.startswith('"'):
            quote_end = line.find('"', 1)
            if quote_end != -1:
                filename = line[1:quote_end]
                remainder = line[quote_end + 1:].strip()
            else:
                continue  # Malformed quoted line
        else:
            # Split on whitespace
            parts = line.split()
            if len(parts) < 2:
                continue  # Not enough parts
            
            # Last part should be CRC32 (8 hex chars)
            potential_crc = parts[-1].upper()
            if len(potential_crc) == 8 and all(c in '0123456789ABCDEF' for c in potential_crc):
                # CRC32 is last part, filename is everything before it
                filename = ' '.join(parts[:-1])
                remainder = potential_crc
            else:
                continue  # No valid CRC32 found
        
        # Extract CRC32 from remainder
        if remainder:
            crc_parts = remainder.split()
            if crc_parts:
                crc32 = crc_parts[0].upper()
                # Validate CRC32 format
                if len(crc32) == 8 and all(c in '0123456789ABCDEF' for c in crc32):
                    safe_filename = validate_relative_member(filename, "SFV")
                    file_checksums[os.fspath(safe_filename)] = crc32
                else:
                    print(
                        f"  {Colors.YELLOW}⚠️  Invalid CRC32 format at "
                        f"line {line_num}{Colors.RESET}"
                    )
    
    return file_checksums

def verify_sfv_file(sfv_path, path_guard, search_dirs=None):
    """
    Verify all files listed in an SFV file.
    Handles cases where SFV is in 'extr' subdirectory but files are in parent directory.
    
    Args:
        sfv_path: Path to the SFV file
        search_dirs: Optional list of directories to search for files
    
    Returns:
        Tuple of (all_passed: bool, results: dict)
        results dict contains: {'filename': {'expected': 'CRC32', 'actual': 'CRC32', 'status': 'PASS/FAIL/MISSING'}}
    """
    global shutdown_requested
    
    sfv_path = path_guard.existing_file(sfv_path)
    sfv_dir = sfv_path.parent
    try:
        file_checksums = parse_sfv_file(sfv_path, path_guard)
    except UnsafeCleanupPathError:
        return False, {
            "<unsafe-sfv-entry>": {
                "expected": None,
                "actual": None,
                "status": "UNSAFE",
                "actual_filename": None,
                "rename_needed": False,
                "error": "Unsafe SFV entry",
            }
        }
    
    if not file_checksums:
        return False, {}
    
    results = {}
    all_passed = True
    
    # Use provided search_dirs or determine them automatically
    if search_dirs is None:
        # Check if SFV is in an 'extr' directory - if so, also check parent directory for files
        sfv_dir_name = sfv_dir.name.lower()
        search_dirs = [sfv_dir]
        
        if sfv_dir_name == 'extr':
            parent_dir = sfv_dir.parent
            search_dirs.append(parent_dir)
            print(f"    {Colors.BLUE}📁 SFV in 'extr' directory - will also search parent directory{Colors.RESET}")
    
    for filename, expected_crc in file_checksums.items():
        if shutdown_requested:
            break
        
        # Skip files with "proof" in the filename - these are optional
        if PROOF_PATTERN.search(filename):
            print(f"    {Colors.BLUE}📸 Skipping optional proof file{Colors.RESET}")
            continue
        
        # Try to find the file in search directories (case-insensitive)
        file_found = False
        file_path = None
        actual_filename = None
        
        for search_dir in search_dirs:
            try:
                search_dir = path_guard.existing_directory(search_dir)
            except UnsafeCleanupPathError:
                continue

            # First try exact match
            try:
                potential_path = path_guard.existing_file(search_dir / filename)
            except UnsafeCleanupPathError:
                potential_path = None
            if potential_path is not None:
                file_path = potential_path
                actual_filename = filename
                file_found = True
                break
            
            # If exact match fails, try case-insensitive search and encoding variants
            try:
                dir_files = list(search_dir.iterdir())
                for dir_file_path in dir_files:
                    dir_file = dir_file_path.name
                    # Case-insensitive exact match
                    if dir_file.lower() == filename.lower():
                        file_path = path_guard.existing_file(dir_file_path)
                        actual_filename = dir_file
                        file_found = True
                        break
                    # Check for files with encoding corruption suffix
                    elif '(invalid encoding)' in dir_file.lower():
                        # Extract the base filename without the encoding suffix
                        base_name = dir_file
                        
                        # Remove various encoding suffixes
                        for suffix in [' (invalid encoding).mp3', '(invalid encoding).mp3', ' (invalid encoding)', '(invalid encoding)']:
                            if base_name.lower().endswith(suffix.lower()):
                                base_name = base_name[:-len(suffix)]
                                break
                        
                        # For encoding-corrupted files, we need to be more flexible in matching
                        # Remove file extension from both for comparison
                        target_base = os.path.splitext(filename)[0]
                        actual_base = os.path.splitext(base_name)[0]
                        
                        # Try different approaches to match corrupted encoding
                        # 1. Direct comparison (ignoring case)
                        if actual_base.lower() == target_base.lower():
                            file_path = path_guard.existing_file(dir_file_path)
                            actual_filename = dir_file
                            file_found = True
                            break
                        
                        # 2. Try to match by normalizing both strings and removing special characters
                        try:
                            # Normalize and remove special characters from target
                            normalized_target = unicodedata.normalize('NFKD', target_base).encode('ascii', 'ignore').decode('ascii')
                            
                            # For actual filename, replace common corruption characters and normalize
                            cleaned_actual = actual_base.replace('�', '').replace('?', '')
                            normalized_actual = unicodedata.normalize('NFKD', cleaned_actual).encode('ascii', 'ignore').decode('ascii')
                            
                            # Also try removing the corrupted character entirely and seeing if strings match
                            if len(normalized_actual) > 0 and len(normalized_target) > 0:
                                # Compare similarity - if they're very close, consider it a match
                                if abs(len(normalized_target) - len(normalized_actual)) <= 2:
                                    # Simple substring match approach
                                    shorter = normalized_actual if len(normalized_actual) < len(normalized_target) else normalized_target
                                    longer = normalized_target if len(normalized_actual) < len(normalized_target) else normalized_actual
                                    
                                    if shorter.lower() in longer.lower() and len(shorter) > 5:  # Reasonable minimum length
                                        file_path = path_guard.existing_file(
                                            dir_file_path
                                        )
                                        actual_filename = dir_file
                                        file_found = True
                                        break
                        except (UnicodeError, ValueError):
                            pass
                        
                        # 3. Try character-by-character comparison, ignoring corruption characters
                        try:
                            target_chars = [c for c in target_base.lower()]
                            actual_chars = [c for c in actual_base.lower() if ord(c) != 65533]  # Skip replacement character
                            
                            # More lenient heuristic: if 75% of characters match in order, consider it a match
                            matches = 0
                            min_len = min(len(target_chars), len(actual_chars))
                            for i in range(min_len):
                                if target_chars[i] == actual_chars[i]:
                                    matches += 1
                            
                            if min_len > 0 and matches / min_len >= 0.75:
                                file_path = path_guard.existing_file(dir_file_path)
                                actual_filename = dir_file
                                file_found = True
                                break
                        except (TypeError, ValueError):
                            pass
                if file_found:
                    break
            except (OSError, UnsafeCleanupPathError):
                continue
        
        if not file_found:
            results[filename] = {
                'expected': expected_crc,
                'actual': None,
                'status': 'MISSING',
                'actual_filename': None,
                'rename_needed': False
            }
            all_passed = False
            continue
        
        actual_crc, file_identity = calculate_crc32_with_identity(
            file_path,
            path_guard,
        )
        
        if actual_crc is None:
            results[filename] = {
                'expected': expected_crc,
                'actual': None,
                'status': 'ERROR',
                'actual_filename': actual_filename,
                'rename_needed': False
            }
            all_passed = False
            continue
        
        status = 'PASS' if actual_crc == expected_crc else 'FAIL'
        
        # Check if file needs to be renamed (has encoding issues but passes validation)
        rename_needed = False
        if (status == 'PASS' and actual_filename != filename and 
            actual_filename and '(invalid encoding)' in actual_filename):
            rename_needed = True
        
        results[filename] = {
            'expected': expected_crc,
            'actual': actual_crc,
            'status': status,
            'actual_filename': actual_filename,
            'rename_needed': rename_needed,
            'file_path': file_path if rename_needed else None,
            'file_identity': file_identity if rename_needed else None,
        }
        
        if status != 'PASS':
            all_passed = False
    
    return all_passed, results

def rename_encoding_files(sfv_results, search_dirs, path_guard, pretend=True):
    """
    Rename files that have encoding issues but pass SFV validation.
    
    Args:
        sfv_results: Results from verify_sfv_file
        search_dirs: Directories where files are located
        pretend: Whether to actually rename or just show what would be renamed
    
    Returns:
        Number of files renamed
    """
    renamed_count = 0
    
    for filename, result in sfv_results.items():
        if result.get('rename_needed', False) and result.get('file_path'):
            try:
                old_path = path_guard.existing_file(result['file_path'])
                relative_filename = validate_relative_member(filename, "SFV")
            except (OSError, UnsafeCleanupPathError):
                print(f"        {Colors.RED}❌ Rename refused{Colors.RESET}")
                continue

            # Calculate new path in the same directory
            file_dir = old_path.parent
            new_path = path_guard._lexical_within_root(
                file_dir / relative_filename
            )

            try:
                if pretend:
                    # Portable pretend mode checks the destination without
                    # performing a mutation.
                    if new_path.exists() or new_path.is_symlink():
                        print(
                            f"        {Colors.YELLOW}⚠️  Would skip rename:"
                            f"{Colors.RESET} target exists"
                        )
                        continue
                    print(
                        f"        {Colors.GREEN}📝 Would repair encoded "
                        f"filename{Colors.RESET}"
                    )
                else:
                    print(
                        f"        {Colors.GREEN}📝 Repairing encoded "
                        f"filename{Colors.RESET}"
                    )
                    path_guard.rename_regular_file_no_replace(
                        old_path,
                        new_path,
                        result.get("file_identity"),
                    )
                
                renamed_count += 1
                
            except (OSError, UnsafeCleanupPathError):
                print(f"        {Colors.RED}❌ Rename failed{Colors.RESET}")
    
    return renamed_count


def check_sfv_integrity(directory_path, path_guard, pretend=True):
    """
    Check if a directory contains SFV files and verify their integrity.
    Handles 'extr' subdirectories where SFV might reference files in parent directory.
    
    Args:
        directory_path: Path to directory to check
    
    Returns:
        Tuple of (should_delete: bool, reason: str, details: dict, target_path: str)
        target_path: The actual directory that should be deleted (might be parent if SFV is in 'extr')
    """
    global shutdown_requested
    
    if shutdown_requested:
        return False, "", {}, directory_path
    
    try:
        directory_path = path_guard.existing_directory(directory_path)
        files = list(directory_path.iterdir())
    except (OSError, UnsafeCleanupPathError):
        return False, "", {}, directory_path
    
    sfv_files = [path for path in files if path.name.lower().endswith('.sfv')]
    
    if not sfv_files:
        return False, "", {}, directory_path  # No SFV files to verify
    
    failed_sfv_files = []
    all_results = {}
    
    # Check if this is an 'extr' directory
    dir_name = directory_path.name.lower()
    is_extr_dir = dir_name == 'extr'
    
    for sfv_file in sfv_files:
        if shutdown_requested:
            break

        try:
            sfv_path = path_guard.existing_file(sfv_file)
        except UnsafeCleanupPathError:
            print(
                f"    {Colors.RED}🛑 Refusing unsafe SFV path"
                f"{Colors.RESET}"
            )
            return False, "Unsafe SFV path", all_results, directory_path
        
        # Search in SFV file directory first, then in parent directory (for "extr" case)
        search_dirs = [directory_path]
        
        # If this is an extr directory, also search parent directory
        if is_extr_dir:
            try:
                parent_dir = path_guard.existing_directory(directory_path.parent)
                search_dirs.append(parent_dir)
            except UnsafeCleanupPathError:
                pass
        
        all_passed, results = verify_sfv_file(
            sfv_path, path_guard, search_dirs
        )
        
        # Handle file renaming for encoding issues if SFV passed
        if all_passed:
            rename_encoding_files(
                results, search_dirs, path_guard, pretend=pretend
            )
        
        all_results[sfv_file.name] = {
            'passed': all_passed,
            'results': results
        }
        
        if not all_passed:
            failed_sfv_files.append(sfv_file.name)
    
    if failed_sfv_files:
        # If SFV is in 'extr' directory and failed, suggest deleting parent directory
        target_path = directory_path.parent if is_extr_dir else directory_path
        target_path = path_guard.existing_directory(target_path)
        reason = "SFV verification failed"
        if is_extr_dir:
            reason += " (in 'extr' subdirectory)"
        return True, reason, all_results, target_path
    
    return False, "", all_results, directory_path

def print_sfv_details(sfv_results, _directory_name):
    """
    Print detailed SFV verification results.
    
    Args:
        sfv_results: Results dictionary from check_sfv_integrity
        directory_name: Name of the directory being checked
    """
    if not sfv_results:
        return
    
    print(f"    {Colors.CYAN}📋 SFV Verification Details:{Colors.RESET}")
    
    for data in sfv_results.values():
        passed = data['passed']
        results = data['results']
        
        status_color = Colors.GREEN if passed else Colors.RED
        status_text = "✅ PASSED" if passed else "❌ FAILED"
        
        print(f"      {status_color}{status_text}{Colors.RESET}")
        
        if not passed:
            status_counts = defaultdict(int)
            for file_result in results.values():
                status = file_result.get("status", "ERROR")
                if status != "PASS":
                    status_counts[status] += 1
            for status in ("MISSING", "FAIL", "ERROR", "UNSAFE"):
                if status_counts[status]:
                    print(
                        f"        {Colors.RED}{status}:{Colors.RESET} "
                        f"{status_counts[status]} file(s)"
                    )

if __name__ == "__main__":
    # Setup signal handlers for graceful shutdown
    setup_signal_handlers()
    parser = argparse.ArgumentParser(description='Clean up directories based on keywords, dates, SFV integrity, and duplicate "-1" suffix')
    parser.add_argument('--pretend', type=str, choices=['true', 'false'], default='true',
                        help='Pretend mode: true (default, safe mode - only show what would be deleted) or false (actually delete)')
    parser.add_argument('--root-dir', default='.',
                        help='Root directory to search from (default: current directory)')
    parser.add_argument(
        "--trusted-boundary",
        default=None,
        help=(
            "Existing directory that is allowed to contain --root-dir "
            "(default: current working directory)"
        ),
    )
    parser.add_argument('--check-sfv', type=str, choices=['true', 'false'], default='true',
                        help='Enable SFV integrity checking: true (default) or false (skip SFV verification)')
    parser.add_argument('--delete-dash-one', type=str, choices=['true', 'false'], default='true',
                        help='Delete directories ending with "-1" (likely duplicates): true (default) or false')
    args = parser.parse_args()
    pretend_mode = args.pretend.lower() == 'true'
    check_sfv_enabled = args.check_sfv.lower() == 'true'
    delete_dash_one_enabled = args.delete_dash_one.lower() == 'true'
    try:
        path_guard = CleanupPathGuard.from_cli(
            args.root_dir, args.trusted_boundary
        )
        if not pretend_mode:
            path_guard.require_secure_mutation()
    except UnsafeCleanupPathError as error:
        parser.error(str(error))
    print(f"{Colors.BOLD}{Colors.BG_BLUE}                  NEWS GROUP CLEANUP TOOL                  {Colors.RESET}")
    print(f"{Colors.BOLD}{'='*60}{Colors.RESET}")
    if pretend_mode:
        print(f"{Colors.YELLOW}🔍 PRETEND MODE:{Colors.RESET} Showing what would be deleted (use {Colors.BOLD}--pretend false{Colors.RESET} to actually delete)")
    else:
        print(f"{Colors.RED}⚠️  LIVE MODE:{Colors.RESET} {Colors.BOLD}Actually deleting directories{Colors.RESET}")
        print(f"{Colors.RED}⚠️  WARNING:{Colors.RESET} This will permanently delete directories!")
    print(
        f"{Colors.CYAN}📁 Canonical Cleanup Root:{Colors.RESET} "
        f"{Colors.BOLD}[path withheld]{Colors.RESET}"
    )
    print(
        f"{Colors.CYAN}🛡️  Trusted Boundary:{Colors.RESET} "
        f"{Colors.BOLD}[path withheld]{Colors.RESET}"
    )
    print(f"{Colors.MAGENTA}🎯 Keywords:{Colors.RESET} {', '.join(KEYWORDS)}")
    if check_sfv_enabled:
        print(f"{Colors.BLUE}📋 SFV Checking:{Colors.RESET} {Colors.GREEN}Enabled{Colors.RESET} (use {Colors.BOLD}--check-sfv false{Colors.RESET} to disable)")
    else:
        print(f"{Colors.BLUE}📋 SFV Checking:{Colors.RESET} {Colors.YELLOW}Disabled{Colors.RESET}")
    print(f"{Colors.YELLOW}🗂️  Delete '-1' Duplicates:{Colors.RESET} {'Enabled' if delete_dash_one_enabled else 'Disabled'} (use --delete-dash-one false to disable)")
    print(f"{Colors.BOLD}{'-' * 60}{Colors.RESET}")
    deleted_count = delete_matching_dirs(
        path_guard,
        pretend_mode,
        check_sfv_enabled,
        delete_dash_one=delete_dash_one_enabled,
    )
    print_statistics()
