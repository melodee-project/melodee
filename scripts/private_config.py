"""Secure file-writing helpers for setup configuration containing secrets."""

import os
import secrets
import stat
from pathlib import Path

PRIVATE_FILE_MODE = stat.S_IRUSR | stat.S_IWUSR
_TEMP_NAME_ATTEMPTS = 128
_LINK_SUPPORTS_NOFOLLOW = os.link in os.supports_follow_symlinks


def path_entry_exists(file_path: str | Path) -> bool:
    """Return whether a path entry exists, including a dangling symlink."""
    return os.path.lexists(os.fspath(file_path))


def path_entry_is_symlink(file_path: str | Path) -> bool:
    """Return whether an existing path entry is a symbolic link."""
    try:
        return stat.S_ISLNK(os.lstat(file_path).st_mode)
    except FileNotFoundError:
        return False


def path_entry_is_regular_file(file_path: str | Path) -> bool:
    """Return whether an existing path entry is a regular file."""
    try:
        return stat.S_ISREG(os.lstat(file_path).st_mode)
    except FileNotFoundError:
        return False


def _unlink_path_entry(file_path: Path) -> None:
    """Remove one exact path entry without following a symbolic link."""
    try:
        file_path.unlink()
    except FileNotFoundError:
        pass


def _open_private_temporary_file(
    destination: Path,
) -> tuple[Path, int, os.stat_result]:
    """Create and secure an unpredictable same-directory temporary file."""
    open_flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL
    if hasattr(os, "O_NOFOLLOW"):
        open_flags |= os.O_NOFOLLOW
    if hasattr(os, "O_CLOEXEC"):
        open_flags |= os.O_CLOEXEC

    for _ in range(_TEMP_NAME_ATTEMPTS):
        temporary_path = destination.with_name(
            f".{destination.name}.{secrets.token_hex(16)}.tmp"
        )
        try:
            file_descriptor = os.open(
                temporary_path,
                open_flags,
                PRIVATE_FILE_MODE,
            )
        except FileExistsError:
            continue

        try:
            temporary_stat = os.fstat(file_descriptor)
            if not stat.S_ISREG(temporary_stat.st_mode):
                raise FileExistsError(
                    f"Temporary path is not a regular file: {temporary_path}"
                )
            if os.name == "posix":
                # fchmod applies the restriction to the opened inode before
                # any secret bytes are written, independently of the umask.
                os.fchmod(file_descriptor, PRIVATE_FILE_MODE)
                temporary_stat = os.fstat(file_descriptor)
                if stat.S_IMODE(temporary_stat.st_mode) != PRIVATE_FILE_MODE:
                    raise PermissionError(
                        f"Unable to restrict temporary file {temporary_path}"
                    )
            return temporary_path, file_descriptor, temporary_stat
        except BaseException:
            try:
                os.close(file_descriptor)
            finally:
                try:
                    _unlink_path_entry(temporary_path)
                except OSError:
                    pass
            raise

    raise FileExistsError(f"Unable to reserve a temporary file beside {destination}")


def _validate_existing_destination(
    destination: Path,
    overwrite: bool,
) -> os.stat_result | None:
    """Validate a destination without following an existing symlink."""
    try:
        destination_stat = destination.lstat()
    except FileNotFoundError:
        return None

    if stat.S_ISLNK(destination_stat.st_mode):
        raise FileExistsError(f"Refusing to write through symbolic link {destination}")
    if not stat.S_ISREG(destination_stat.st_mode):
        raise FileExistsError(f"Refusing to replace non-regular path {destination}")
    if not overwrite:
        raise FileExistsError(f"Refusing to overwrite existing file {destination}")

    return destination_stat


def _same_file(first: os.stat_result, second: os.stat_result) -> bool:
    """Return whether two metadata snapshots identify the same file."""
    return (first.st_dev, first.st_ino) == (second.st_dev, second.st_ino)


def _validate_path_matches_file(
    file_path: Path,
    expected_stat: os.stat_result,
    description: str,
) -> os.stat_result:
    """Require a path to remain the expected regular file without following it."""
    try:
        current_stat = file_path.lstat()
    except FileNotFoundError as error:
        raise FileExistsError(f"{description} disappeared: {file_path}") from error

    if (
        stat.S_ISLNK(current_stat.st_mode)
        or not stat.S_ISREG(current_stat.st_mode)
        or not _same_file(expected_stat, current_stat)
    ):
        raise FileExistsError(f"{description} changed: {file_path}")
    return current_stat


def _validate_private_path_matches_file(
    file_path: Path,
    expected_stat: os.stat_result,
    description: str,
) -> os.stat_result:
    """Require the expected regular file and POSIX owner-only permissions."""
    current_stat = _validate_path_matches_file(
        file_path,
        expected_stat,
        description,
    )
    if os.name == "posix" and stat.S_IMODE(current_stat.st_mode) != PRIVATE_FILE_MODE:
        raise PermissionError(f"{description} permissions changed: {file_path}")
    return current_stat


def secure_existing_private_file(file_path: str | Path) -> None:
    """Validate an existing regular file and enforce mode 0600 on POSIX."""
    destination = Path(file_path)
    expected_stat = _validate_existing_destination(destination, overwrite=True)
    if expected_stat is None:
        raise FileNotFoundError(destination)

    if os.name != "posix":
        # Python's standard library cannot install an owner-only Windows ACL.
        # Revalidation still rejects a path-entry race without following it.
        _validate_private_path_matches_file(
            destination,
            expected_stat,
            "Destination",
        )
        return

    if not hasattr(os, "O_NOFOLLOW"):
        raise NotImplementedError("Secure existing-file validation needs O_NOFOLLOW")

    open_flags = os.O_RDONLY | os.O_NOFOLLOW
    if hasattr(os, "O_NONBLOCK"):
        open_flags |= os.O_NONBLOCK
    if hasattr(os, "O_CLOEXEC"):
        open_flags |= os.O_CLOEXEC

    file_descriptor = os.open(destination, open_flags)
    try:
        opened_stat = os.fstat(file_descriptor)
        if not stat.S_ISREG(opened_stat.st_mode) or not _same_file(
            expected_stat, opened_stat
        ):
            raise FileExistsError(f"Destination changed while opening {destination}")

        os.fchmod(file_descriptor, PRIVATE_FILE_MODE)
        secured_stat = os.fstat(file_descriptor)
        if (
            not _same_file(opened_stat, secured_stat)
            or stat.S_IMODE(secured_stat.st_mode) != PRIVATE_FILE_MODE
        ):
            raise PermissionError(f"Unable to restrict existing file {destination}")
        _validate_private_path_matches_file(
            destination,
            secured_stat,
            "Destination",
        )
    finally:
        os.close(file_descriptor)


def _validate_temporary_path(
    temporary_path: Path,
    temporary_stat: os.stat_result,
) -> None:
    """Reject replacement of the private temporary path before publication."""
    _validate_private_path_matches_file(
        temporary_path,
        temporary_stat,
        "Temporary file",
    )


def _publish_new_file(
    temporary_path: Path,
    destination: Path,
    temporary_stat: os.stat_result,
) -> bool:
    """Publish a new destination without replacing an existing path entry.

    Return True when publication consumes the temporary path.
    """
    _validate_temporary_path(temporary_path, temporary_stat)

    if os.name == "nt":
        # Windows rename is a same-directory, no-replace operation here. No
        # broader cross-platform atomicity or ACL guarantee is claimed.
        os.rename(temporary_path, destination)
        try:
            _validate_private_path_matches_file(
                destination,
                temporary_stat,
                "Destination",
            )
        except BaseException:
            _unlink_path_entry(destination)
            raise
        return True

    if not _LINK_SUPPORTS_NOFOLLOW:
        raise NotImplementedError("Secure publication needs no-follow hard links")

    os.link(temporary_path, destination, follow_symlinks=False)
    try:
        _validate_private_path_matches_file(
            destination,
            temporary_stat,
            "Destination",
        )
    except BaseException:
        _unlink_path_entry(destination)
        raise
    return False


def write_private_file(
    file_path: str | Path,
    content: str,
    *,
    overwrite: bool = False,
) -> None:
    """Securely publish secret content, using mode 0600 on POSIX systems.

    The destination is never opened for writing directly. New files are
    published with an atomic hard link that cannot replace an existing path;
    explicitly requested replacements use an atomic rename only after the
    original regular file is revalidated. On Windows, same-directory rename is
    used without claiming a portable atomicity guarantee, and access control is
    inherited from the destination directory because Python's standard library
    cannot establish an owner-only Windows ACL.
    """
    destination = Path(file_path)
    existing_stat = _validate_existing_destination(destination, overwrite)
    temporary_path, file_descriptor, temporary_stat = _open_private_temporary_file(
        destination
    )
    remove_temporary_file = True

    try:
        file_handle = os.fdopen(
            file_descriptor,
            "w",
            encoding="utf-8",
            closefd=False,
        )
        with file_handle:
            # Setup secrets must persist across restarts. The file is ignored
            # by Git, never logged, and protected before this intentional sink.
            # codeql[py/clear-text-storage-sensitive-data]
            file_handle.write(content)
            file_handle.flush()
            os.fsync(file_handle.fileno())
        written_stat = os.fstat(file_descriptor)
        if not _same_file(temporary_stat, written_stat):
            raise FileExistsError(f"Temporary file changed: {temporary_path}")
        if (
            os.name == "posix"
            and stat.S_IMODE(written_stat.st_mode) != PRIVATE_FILE_MODE
        ):
            raise PermissionError(
                f"Temporary file permissions changed: {temporary_path}"
            )
        temporary_stat = written_stat
        if existing_stat is None:
            remove_temporary_file = not _publish_new_file(
                temporary_path,
                destination,
                temporary_stat,
            )
        else:
            _validate_temporary_path(temporary_path, temporary_stat)
            _validate_path_matches_file(destination, existing_stat, "Destination")
            os.replace(temporary_path, destination)
            remove_temporary_file = False
            _validate_private_path_matches_file(
                destination,
                temporary_stat,
                "Destination",
            )
    finally:
        try:
            os.close(file_descriptor)
        finally:
            if remove_temporary_file:
                _unlink_path_entry(temporary_path)


def private_file_created_message(file_name: str) -> str:
    """Describe the platform-specific protection applied to a secret file."""
    if os.name == "posix":
        return f"Created {file_name} with owner-only POSIX permissions"
    return (
        f"Created {file_name}; access is governed by the containing directory's "
        "Windows ACL"
    )


def existing_private_file_message(file_name: str) -> str:
    """Describe protection applied while preserving an existing secret file."""
    if os.name == "posix":
        return f"Kept {file_name} after enforcing owner-only POSIX permissions"
    return (
        f"Kept {file_name}; access is governed by the containing directory's "
        "Windows ACL"
    )
