#!/usr/bin/env python3
"""
Melodee Container Setup Script

This script prepares the environment for running Melodee in containers.
It will:
1. Detect available container runtime (podman or docker)
2. Offer to install podman if no runtime is found
3. Check system requirements (disk space, memory, ports)
4. Generate secure random values for secrets
5. Create a .env file with all required configuration
6. Verify required files exist (Dockerfile, compose.yml, entrypoint.sh)
7. Optionally start the containers
8. Verify containers are healthy after startup
9. Check volume permissions (when using --start with rootless podman)

Usage:
    python scripts/run-container-setup.py [OPTIONS]

Options:
    --help, -h              Show this help message and exit
    --start                 Start containers after setup and verify permissions
    --check-only            Only run checks, don't create .env or start containers
    --check-permissions     Check volume permissions for rootless podman issues
    --force                 Overwrite existing .env file without prompting
    --update                Safely update running containers to latest code (preserves volumes)
    --yes, -y               Skip confirmation prompts (for automated deployments)
    --prune                 After build/update, prune dangling images and build cache (recommended for demo/test servers)
    --prune-all             More aggressive prune of unused images/containers (keeps volumes; may require re-pull/rebuild later)

Examples:
    # Show help
    python scripts/run-container-setup.py --help

    # Run preflight checks only
    python scripts/run-container-setup.py --check-only

    # Setup and start containers (auto-checks permissions on rootless podman)
    python scripts/run-container-setup.py --start

    # Check for permission issues on existing installation
    python scripts/run-container-setup.py --check-permissions

    # Update running containers
    python scripts/run-container-setup.py --update
"""

import os
import re
import secrets
import shutil
import socket
import subprocess
import sys
import time
from pathlib import Path

if __package__:
    from .private_config import (
        existing_private_file_message,
        path_entry_exists,
        path_entry_is_regular_file,
        path_entry_is_symlink,
        private_file_created_message,
        secure_existing_private_file,
        write_private_file,
    )
else:
    _PRIVATE_CONFIG_PATH = Path(__file__).resolve().with_name("private_config.py")
    _SCRIPT_DIRECTORY = str(_PRIVATE_CONFIG_PATH.parent)
    if _SCRIPT_DIRECTORY not in sys.path:
        sys.path.insert(0, _SCRIPT_DIRECTORY)
    import private_config as _private_config_module

    if Path(_private_config_module.__file__).resolve() != _PRIVATE_CONFIG_PATH:
        raise ImportError(
            f"Refusing non-sibling private_config module at "
            f"{_private_config_module.__file__}"
        )
    from private_config import (
        existing_private_file_message,
        path_entry_exists,
        path_entry_is_regular_file,
        path_entry_is_symlink,
        private_file_created_message,
        secure_existing_private_file,
        write_private_file,
    )

    del _private_config_module


def print_help():
    """Print help message and exit."""
    print(__doc__)
    sys.exit(0)


def print_banner():
    """Print the Melodee setup banner."""
    print("\n" + "=" * 60)
    print("  Melodee Container Setup")
    print("=" * 60 + "\n")


def print_success(message: str):
    """Print a success message in green."""
    print(f"✓ {message}")


def print_error(message: str):
    """Print an error message in red."""
    print(f"✗ {message}", file=sys.stderr)


def print_info(message: str):
    """Print an info message."""
    print(f"  {message}")


def print_warning(message: str):
    """Print a warning message."""
    print(f"⚠ {message}")


# Minimum requirements
MIN_DISK_SPACE_GB = 5
MIN_MEMORY_GB = 2
REQUIRED_FILES = ["Dockerfile", "compose.yml", "entrypoint.sh"]
DEFAULT_PORT = 8080


def check_disk_space(
    project_root: Path, min_gb: int = MIN_DISK_SPACE_GB
) -> tuple[bool, str]:
    """Check if there's enough disk space for containers."""
    try:
        stat = os.statvfs(project_root)
        free_gb = (stat.f_bavail * stat.f_frsize) / (1024**3)
        if free_gb < min_gb:
            return False, f"Only {free_gb:.1f}GB free, need at least {min_gb}GB"
        return True, f"{free_gb:.1f}GB available"
    except OSError as e:
        return False, f"Could not check disk space: {e}"


def check_memory() -> tuple[bool, str]:
    """Check if system has enough memory."""
    try:
        with open("/proc/meminfo") as f:
            for line in f:
                if line.startswith("MemTotal:"):
                    kb = int(line.split()[1])
                    gb = kb / (1024**2)
                    if gb < MIN_MEMORY_GB:
                        return (
                            False,
                            f"Only {gb:.1f}GB RAM, need at least {MIN_MEMORY_GB}GB",
                        )
                    return True, f"{gb:.1f}GB RAM available"
    except (OSError, ValueError):
        pass
    # Can't determine on macOS/Windows this way, assume OK
    return True, "Memory check skipped (non-Linux)"


def check_port_available(port: int) -> tuple[bool, str]:
    """Check if a port is available for binding."""
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
            s.settimeout(1)
            result = s.connect_ex(("localhost", port))
            if result == 0:
                return False, f"Port {port} is already in use"
            return True, f"Port {port} is available"
    except OSError as e:
        return False, f"Could not check port {port}: {e}"


def check_required_files(project_root: Path) -> tuple[bool, list[str]]:
    """Check if all required files exist."""
    missing = []
    for filename in REQUIRED_FILES:
        if not (project_root / filename).exists():
            missing.append(filename)
    return len(missing) == 0, missing


def check_dockerfile_valid(project_root: Path) -> tuple[bool, str]:
    """Basic validation of Dockerfile."""
    dockerfile = project_root / "Dockerfile"
    if not dockerfile.exists():
        return False, "Dockerfile not found"

    content = dockerfile.read_text()

    # Check for multi-stage build
    if "FROM" not in content:
        return False, "Dockerfile missing FROM instruction"

    # Check for entrypoint
    if "ENTRYPOINT" not in content and "CMD" not in content:
        return False, "Dockerfile missing ENTRYPOINT or CMD"

    return True, "Dockerfile looks valid"


def check_compose_valid(project_root: Path) -> tuple[bool, str]:
    """Basic validation of compose.yml."""
    compose_file = project_root / "compose.yml"
    if not compose_file.exists():
        return False, "compose.yml not found"

    content = compose_file.read_text()

    # Check for required services
    if "melodee-db:" not in content:
        return False, "compose.yml missing melodee-db service"

    if "melodee.blazor:" not in content:
        return False, "compose.yml missing melodee.blazor service"

    # Check for localhost/ prefix (podman compatibility)
    if "image: melodee:latest" in content and "localhost/melodee:latest" not in content:
        return (
            False,
            "compose.yml should use 'localhost/melodee:latest' for Podman compatibility",
        )

    return True, "compose.yml looks valid"


def run_preflight_checks(project_root: Path, port: int = DEFAULT_PORT) -> bool:
    """Run all preflight checks and report results."""
    print("\n" + "-" * 60)
    print("  Preflight Checks")
    print("-" * 60 + "\n")

    all_passed = True

    # Check required files
    files_ok, missing = check_required_files(project_root)
    if files_ok:
        print_success("All required files present")
    else:
        print_error(f"Missing required files: {', '.join(missing)}")
        all_passed = False

    # Check Dockerfile
    dockerfile_ok, dockerfile_msg = check_dockerfile_valid(project_root)
    if dockerfile_ok:
        print_success(dockerfile_msg)
    else:
        print_error(dockerfile_msg)
        all_passed = False

    # Check compose.yml
    compose_ok, compose_msg = check_compose_valid(project_root)
    if compose_ok:
        print_success(compose_msg)
    else:
        print_error(compose_msg)
        all_passed = False

    # Check disk space
    disk_ok, disk_msg = check_disk_space(project_root)
    if disk_ok:
        print_success(f"Disk space: {disk_msg}")
    else:
        print_error(f"Disk space: {disk_msg}")
        all_passed = False

    # Check memory
    mem_ok, mem_msg = check_memory()
    if mem_ok:
        print_success(f"Memory: {mem_msg}")
    else:
        print_warning(f"Memory: {mem_msg}")
        # Don't fail on memory, just warn

    # Check port
    port_ok, port_msg = check_port_available(port)
    if port_ok:
        print_success(port_msg)
    else:
        print_warning(port_msg)
        print_info("  You can change the port in .env file (MELODEE_PORT)")

    print()
    return all_passed


def detect_os() -> dict | None:
    """Detect the operating system and return info for package installation."""
    os_info = {"type": None, "id": None, "version": None}

    # Check for Linux
    if sys.platform.startswith("linux"):
        os_info["type"] = "linux"

        # Try to read /etc/os-release
        os_release = Path("/etc/os-release")
        if os_release.exists():
            content = os_release.read_text()
            for line in content.splitlines():
                if line.startswith("ID="):
                    os_info["id"] = line.split("=", 1)[1].strip().strip('"').lower()
                elif line.startswith("VERSION_ID="):
                    os_info["version"] = line.split("=", 1)[1].strip().strip('"')

        return os_info

    elif sys.platform == "darwin":
        os_info["type"] = "macos"
        os_info["id"] = "macos"
        return os_info

    elif sys.platform == "win32":
        os_info["type"] = "windows"
        os_info["id"] = "windows"
        return os_info

    return None


def get_install_commands(os_info: dict) -> list[list[str]] | None:
    """Get the commands to install podman and podman-compose for the detected OS."""
    if os_info is None:
        return None

    os_id = os_info.get("id", "")
    os_type = os_info.get("type", "")

    # Debian/Ubuntu based
    if os_id in ["debian", "ubuntu", "linuxmint", "pop"]:
        return [
            ["sudo", "apt-get", "update"],
            ["sudo", "apt-get", "install", "-y", "podman", "podman-compose"],
        ]

    # Fedora
    elif os_id == "fedora":
        return [
            ["sudo", "dnf", "install", "-y", "podman", "podman-compose"],
        ]

    # RHEL/CentOS/Rocky/Alma
    elif os_id in ["rhel", "centos", "rocky", "almalinux"]:
        return [
            ["sudo", "dnf", "install", "-y", "podman", "podman-compose"],
        ]

    # Arch Linux
    elif os_id in ["arch", "manjaro", "endeavouros"]:
        return [
            ["sudo", "pacman", "-Sy", "--noconfirm", "podman", "podman-compose"],
        ]

    # openSUSE
    elif os_id in ["opensuse-leap", "opensuse-tumbleweed", "sles"]:
        return [
            ["sudo", "zypper", "install", "-y", "podman", "podman-compose"],
        ]

    # macOS
    elif os_type == "macos":
        if shutil.which("brew"):
            return [
                ["brew", "install", "podman", "podman-compose"],
                ["podman", "machine", "init"],
                ["podman", "machine", "start"],
            ]
        else:
            return None  # Need Homebrew

    return None


def install_podman(os_info: dict) -> bool:
    """Attempt to install podman and podman-compose."""
    commands = get_install_commands(os_info)

    if commands is None:
        return False

    print_info("Installing podman and podman-compose...")
    print()

    for cmd in commands:
        print_info(f"Running: {' '.join(cmd)}")
        try:
            result = subprocess.run(cmd, timeout=300)
            if result.returncode != 0:
                print_error(f"Command failed with exit code {result.returncode}")
                return False
        except subprocess.TimeoutExpired:
            print_error("Installation timed out")
            return False
        except OSError as e:
            print_error(f"Failed to run command: {e}")
            return False

    print()
    return True


def offer_install_podman() -> str | None:
    """Offer to install podman if no container runtime is found."""
    os_info = detect_os()

    if os_info is None:
        print_error("Could not detect operating system")
        return None

    os_type = os_info.get("type", "unknown")
    os_id = os_info.get("id", "unknown")

    print_info(f"Detected OS: {os_id} ({os_type})")

    # Check if we know how to install on this OS
    commands = get_install_commands(os_info)

    if commands is None:
        if os_type == "macos" and not shutil.which("brew"):
            print_error("Homebrew is required to install podman on macOS")
            print_info("Install Homebrew first: https://brew.sh")
        elif os_type == "windows":
            print_error("Automatic installation not supported on Windows")
            print_info("Please install Podman Desktop: https://podman-desktop.io")
            print_info(
                "Or Docker Desktop: https://www.docker.com/products/docker-desktop"
            )
        else:
            print_error(f"Automatic installation not supported for {os_id}")
            print_info("Please install podman or docker manually")
        return None

    print()
    print_warning("No container runtime (podman or docker) found!")
    print_info("This script can install podman for you.")
    print()

    response = input("  Would you like to install podman now? (y/N): ").strip().lower()

    if response != "y":
        print_info("Installation cancelled")
        return None

    print()
    if install_podman(os_info):
        # Verify installation
        if shutil.which("podman"):
            print_success("Podman installed successfully!")
            return "podman"
        else:
            print_error("Installation completed but podman not found in PATH")
            print_info("You may need to restart your terminal or log out and back in")
            return None
    else:
        print_error("Failed to install podman")
        return None


def detect_container_runtime() -> str | None:
    """Detect available container runtime, preferring podman over docker."""
    for runtime in ["podman", "docker"]:
        if shutil.which(runtime):
            try:
                result = subprocess.run(
                    [runtime, "--version"], capture_output=True, text=True, timeout=10
                )
                if result.returncode == 0:
                    version_info = result.stdout.strip().split("\n")[0]
                    print_success(f"Found {runtime}: {version_info}")
                    return runtime
            except (subprocess.TimeoutExpired, OSError):
                continue
    return None


def check_compose_available(runtime: str) -> bool:
    """Check if compose is available for the detected runtime."""
    try:
        result = subprocess.run(
            [runtime, "compose", "version"], capture_output=True, text=True, timeout=10
        )
        if result.returncode == 0:
            version_info = result.stdout.strip().split("\n")[0]
            print_success(f"Found compose: {version_info}")
            return True
    except (subprocess.TimeoutExpired, OSError):
        pass

    # Try docker-compose as fallback for docker
    if runtime == "docker" and shutil.which("docker-compose"):
        try:
            result = subprocess.run(
                ["docker-compose", "--version"],
                capture_output=True,
                text=True,
                timeout=10,
            )
            if result.returncode == 0:
                print_success(f"Found docker-compose: {result.stdout.strip()}")
                return True
        except (subprocess.TimeoutExpired, OSError):
            pass

    # Try podman-compose as fallback for podman
    if runtime == "podman" and shutil.which("podman-compose"):
        try:
            result = subprocess.run(
                ["podman-compose", "--version"],
                capture_output=True,
                text=True,
                timeout=10,
            )
            if result.returncode == 0:
                version_info = result.stdout.strip().split("\n")[0]
                print_success(f"Found podman-compose: {version_info}")
                return True
        except (subprocess.TimeoutExpired, OSError):
            pass

    return False


def parse_human_size_to_bytes(size_str: str) -> int:
    """Parse sizes like '891.3MB', '2.168GB', '65.16kB' into bytes."""
    s = (size_str or "").strip()
    if not s:
        return 0

    # Normalize common suffixes
    s = s.replace("iB", "B")  # KiB -> KB (close enough for pruning heuristics)
    m = re.match(r"^([0-9]+(?:\.[0-9]+)?)\s*([KMGTP]?B)$", s, flags=re.IGNORECASE)
    if not m:
        return 0

    val = float(m.group(1))
    unit = m.group(2).upper()

    multipliers = {
        "B": 1,
        "KB": 1024,
        "MB": 1024**2,
        "GB": 1024**3,
        "TB": 1024**4,
        "PB": 1024**5,
    }
    return int(val * multipliers.get(unit, 1))


def list_dangling_images(runtime: str) -> list[dict]:
    """Return a list of dangling (untagged) images with best-effort size parsing."""
    if runtime not in ("podman", "docker"):
        return []

    cmd = [
        runtime,
        "images",
        "-a",
        "--filter",
        "dangling=true",
        "--format",
        "{{.ID}}\t{{.Size}}\t{{.CreatedSince}}",
    ]
    try:
        result = subprocess.run(cmd, capture_output=True, text=True, timeout=15)
        if result.returncode != 0:
            return []
        images: list[dict] = []
        for line in result.stdout.strip().splitlines():
            parts = line.split("\t")
            if len(parts) < 2:
                continue
            img_id = parts[0].strip()
            size_str = parts[1].strip()
            created = parts[2].strip() if len(parts) > 2 else ""
            if img_id:
                images.append(
                    {
                        "id": img_id,
                        "size_str": size_str,
                        "size_bytes": parse_human_size_to_bytes(size_str),
                        "created": created,
                    }
                )
        return images
    except (subprocess.TimeoutExpired, OSError):
        return []


def prune_container_storage(runtime: str, prune_all: bool, skip_confirm: bool) -> bool:
    """
    Prune container storage to prevent disk bloat from repeated builds.

    - Safe prune (default): removes dangling images + build cache
    - Aggressive prune (--prune-all): removes ALL unused images/containers/networks (keeps volumes)
    """
    if runtime not in ("podman", "docker"):
        return True

    # Summarize what we'd reclaim (best effort)
    dangling = list_dangling_images(runtime)
    dangling_count = len(dangling)
    dangling_bytes = sum(x.get("size_bytes", 0) for x in dangling)

    print("\n" + "-" * 60)
    print("  Container Storage Cleanup")
    print("-" * 60 + "\n")

    if prune_all:
        print_warning(
            f"{runtime}: Aggressive prune will remove ALL unused images/containers/networks (volumes are kept)"
        )
    else:
        if dangling_count == 0:
            print_info(f"{runtime}: No dangling images found")
        else:
            approx_gb = dangling_bytes / (1024**3) if dangling_bytes else 0
            if approx_gb > 0:
                print_info(
                    f"{runtime}: Found {dangling_count} dangling images (≈ {approx_gb:.1f}GB)"
                )
            else:
                print_info(f"{runtime}: Found {dangling_count} dangling images")

    if not skip_confirm:
        if prune_all:
            response = (
                input("  Prune unused images/containers now? (y/N): ").strip().lower()
            )
            if response != "y":
                print_info("Cleanup skipped")
                return True
        else:
            response = (
                input("  Prune dangling images/build cache now? (Y/n): ")
                .strip()
                .lower()
            )
            if response == "n":
                print_info("Cleanup skipped")
                return True
    else:
        print_info("Proceeding with cleanup (--yes flag specified)")

    # Execute prune
    try:
        if prune_all:
            # Keep volumes by default (no --volumes)
            cmd = [runtime, "system", "prune", "-a", "-f"]
            print_info(f"Running: {' '.join(cmd)}")
            subprocess.run(cmd, timeout=600)
        else:
            cmd = [runtime, "image", "prune", "-f"]
            print_info(f"Running: {' '.join(cmd)}")
            subprocess.run(cmd, timeout=600)

        # Builder cache prune (helps a lot on repeated builds). Best-effort.
        builder_cmd = [runtime, "builder", "prune", "-a", "-f"]
        print_info(f"Running: {' '.join(builder_cmd)}")
        subprocess.run(builder_cmd, timeout=600)

        print_success("Container storage cleanup completed")
        return True
    except subprocess.TimeoutExpired:
        print_warning("Cleanup timed out")
        return False
    except OSError as e:
        print_warning(f"Cleanup failed: {e}")
        return False


def maybe_suggest_prune(runtime: str):
    """If there are many dangling images, warn the user how to reclaim disk."""
    if runtime not in ("podman", "docker"):
        return

    dangling = list_dangling_images(runtime)
    if not dangling:
        return

    dangling_count = len(dangling)
    dangling_bytes = sum(x.get("size_bytes", 0) for x in dangling)
    approx_gb = dangling_bytes / (1024**3) if dangling_bytes else 0

    # Heuristic: warn if more than a few images or > 1GB (approx)
    if dangling_count >= 5 or approx_gb >= 1.0:
        print()
        print_warning(
            "Container builds can accumulate untagged (dangling) images over time and consume disk space."
        )
        if approx_gb > 0:
            print_info(
                f"Detected {dangling_count} dangling images (≈ {approx_gb:.1f}GB)."
            )

        if runtime == "podman":
            print_info("To clean up safely:")
            print("    podman image prune")
            print("    podman builder prune -a")
        else:
            print_info("To clean up safely:")
            print("    docker image prune")
            print("    docker builder prune -a")
        print_info(
            "Or re-run this script with --prune (or --prune-all for a more aggressive cleanup).\n"
        )


def generate_secure_password(length: int = 32) -> str:
    """Generate a secure random password."""
    return secrets.token_urlsafe(length)


def generate_jwt_token() -> str:
    """Generate a secure JWT token (64+ characters)."""
    return secrets.token_urlsafe(64)


def get_project_root() -> Path:
    """Get the project root directory."""
    script_dir = Path(__file__).resolve().parent
    return script_dir.parent


def create_env_file(project_root: Path, overwrite: bool = False) -> bool:
    """Securely create a .env file, using POSIX mode 0600."""
    env_file = project_root / ".env"
    replace_existing = overwrite

    if path_entry_exists(env_file):
        if path_entry_is_symlink(env_file):
            print_error("Refusing to use .env because it is a symbolic link")
            return False
        if not path_entry_is_regular_file(env_file):
            print_error("Refusing to use .env because it is not a regular file")
            return False
        if not overwrite:
            print_info(f".env file already exists at {env_file}")
            response = input("  Overwrite? (y/N): ").strip().lower()
            if response != "y":
                try:
                    secure_existing_private_file(env_file)
                except (OSError, NotImplementedError) as error:
                    print_error(f"Failed to secure existing .env file: {error}")
                    return False
                print_info(existing_private_file_message("existing .env file"))
                return True
            replace_existing = True

    # Generate secure values
    db_password = generate_secure_password()
    jwt_token = generate_jwt_token()

    env_content = f"""# Melodee Docker Configuration
# Generated by run-container-setup.py
#
# WARNING: This file contains secrets. Do not commit to version control!

# Database password (auto-generated)
DB_PASSWORD={db_password}

# Database connection pool settings
DB_MIN_POOL_SIZE=10
DB_MAX_POOL_SIZE=50

# Port configuration - the port Melodee will be available on
MELODEE_PORT=8080

# Authentication settings
# JWT token secret (auto-generated, 64+ characters)
MELODEE_AUTH_TOKEN={jwt_token}
# Token validity in hours
MELODEE_AUTH_TOKEN_HOURS=24

# Brave Search API Configuration (optional)
# Get your API key from https://brave.com/search/api/
BRAVE_SEARCH__ENABLED=false
BRAVE_SEARCH__APIKEY=your_brave_api_key_here
BRAVE_SEARCH__BASEURL=https://api.search.brave.com
BRAVE_SEARCH__IMAGESEARCHPATH=/res/v1/images/search
"""

    try:
        write_private_file(
            env_file,
            env_content,
            overwrite=replace_existing,
        )
        print_success(private_file_created_message(f".env file at {env_file}"))
        return True
    except (OSError, ValueError) as e:
        print_error(f"Failed to create .env file: {e}")
        return False


def ensure_gitignore_has_env(project_root: Path):
    """Ensure .env is in .gitignore."""
    gitignore = project_root / ".gitignore"

    if not gitignore.exists():
        return

    content = gitignore.read_text()
    if ".env" not in content:
        print_info("Adding .env to .gitignore")
        with gitignore.open("a") as f:
            f.write("\n# Environment file with secrets\n.env\n")
        print_success("Updated .gitignore")


def is_rootless_podman(runtime: str) -> bool:
    """Check if using rootless podman."""
    if runtime != "podman":
        return False

    try:
        result = subprocess.run(
            ["podman", "info", "--format", "{{.Host.Security.Rootless}}"],
            capture_output=True,
            text=True,
            timeout=10,
        )
        if result.returncode == 0:
            return result.stdout.strip().lower() == "true"
    except (subprocess.TimeoutExpired, OSError, FileNotFoundError):
        pass

    # Fallback: if not running as root, assume rootless
    return os.geteuid() != 0


def check_melodee_user_exists() -> bool:
    """Check if melodee system user exists on the host."""
    try:
        import pwd

        pwd.getpwnam("melodee")
        return True
    except KeyError:
        return False


def offer_create_melodee_user() -> bool:
    """
    Offer to create a dedicated melodee system user for multi-user setups.

    This is recommended for:
    - Production/demo servers with multiple admins
    - CI/CD deployments where different users need access
    - Shared server environments

    Returns True if user should be created, False if using current user.
    """
    print("\n" + "=" * 60)
    print("  User Configuration")
    print("=" * 60 + "\n")

    current_user = os.getenv("USER") or os.getenv("USERNAME") or "unknown"

    print_info("Melodee can run under different user configurations:")
    print()
    print("  1. Current user only (simple, homelab)")
    print(f"     - Runs as: {current_user}")
    print("     - Files owned by: your user")
    print("     - Best for: Single-user homelab setups")
    print()
    print("  2. Dedicated melodee user (recommended for servers)")
    print("     - Runs as: melodee system user")
    print("     - Files owned by: melodee user")
    print("     - Multiple users can be added to melodee group")
    print("     - Best for: Multi-user servers, CI/CD, production")
    print()

    if check_melodee_user_exists():
        print_success("Melodee user already exists on this system")
        response = (
            input("\n  Use melodee user instead of current user? (Y/n): ")
            .strip()
            .lower()
        )
        return response != "n"

    response = (
        input("\n  Create dedicated melodee system user? (y/N): ").strip().lower()
    )

    if response == "y":
        print_info("\nCreating melodee system user...")
        print_info("You will be prompted for sudo password\n")

        try:
            # Create system user with home directory
            subprocess.run(
                [
                    "sudo",
                    "useradd",
                    "--system",
                    "--create-home",
                    "--shell",
                    "/bin/bash",
                    "melodee",
                ],
                check=True,
                timeout=30,
            )
            print_success("Created melodee system user")

            # Add current user to melodee group
            subprocess.run(
                ["sudo", "usermod", "-aG", "melodee", current_user],
                check=True,
                timeout=10,
            )
            print_success(f"Added {current_user} to melodee group")

            print_warning(
                "\nYou need to log out and back in for group membership to take effect"
            )
            print_info("Or run: newgrp melodee")

            return True

        except subprocess.CalledProcessError as e:
            print_error(f"Failed to create melodee user: {e}")
            print_info("Falling back to current user setup")
            return False
        except subprocess.TimeoutExpired:
            print_error("Command timed out")
            return False

    print_info("Using current user setup")
    return False


def create_compose_override_for_rootless(
    project_root: Path, use_melodee_user: bool = False
) -> bool:
    """
    Create compose.override.yml for rootless podman to fix permission issues.

    In rootless podman:
    - Container UID 0 maps to host user (e.g., 1000)
    - Container non-root users map to sub-UIDs (e.g., 100998)
    - Files created by container non-root users are inaccessible to host user

    Solution: Run container as host UID:GID and remove the user drop in entrypoint.

    Args:
        project_root: Path to project root
        use_melodee_user: If True, run as melodee user instead of current user
    """
    override_file = project_root / "compose.override.yml"

    if use_melodee_user:
        # Get melodee user's UID and GID
        try:
            import pwd

            melodee_user = pwd.getpwnam("melodee")
            uid = melodee_user.pw_uid
            gid = melodee_user.pw_gid
            user_desc = "melodee system user"
        except KeyError:
            print_error("Melodee user not found on system")
            return False
    else:
        # Get current user's UID and GID
        uid = os.getuid()
        gid = os.getgid()
        user_desc = "your user"

    override_content = f"""# Auto-generated by run-container-setup.py for rootless podman
# This ensures files created in volumes are owned by {user_desc}
#
# DO NOT COMMIT THIS FILE - it's specific to your setup
# Add to .gitignore if not already there

services:
  melodee.blazor:
    # Run as {user_desc} to avoid UID mapping issues in rootless podman
    user: "{uid}:{gid}"
    # Disable user namespace remapping - UID inside container = UID on host
    userns_mode: "keep-id:uid={uid},gid={gid}"
    environment:
      # Let the container know it's running as non-root
      - MELODEE_RUNNING_AS_USER=true
"""

    try:
        override_file.write_text(override_content)
        print_success(
            f"Created compose.override.yml for rootless podman (UID={uid}, GID={gid})"
        )
        print_info(f"  Files in volumes will be owned by {user_desc}")
        return True
    except OSError as e:
        print_error(f"Failed to create compose.override.yml: {e}")
        return False


def ensure_gitignore_has_override(project_root: Path):
    """Ensure compose.override.yml is in .gitignore."""
    gitignore = project_root / ".gitignore"

    if not gitignore.exists():
        return

    content = gitignore.read_text()
    if "compose.override.yml" not in content:
        print_info("Adding compose.override.yml to .gitignore")
        with gitignore.open("a") as f:
            f.write(
                "\n# Compose override (user-specific for rootless podman)\ncompose.override.yml\n"
            )
        print_success("Updated .gitignore")


def get_compose_command(runtime: str) -> list[str]:
    """Get the appropriate compose command for the runtime."""
    # Check if 'podman compose' (plugin) works
    if runtime == "podman":
        try:
            result = subprocess.run(
                ["podman", "compose", "version"], capture_output=True, timeout=10
            )
            if result.returncode == 0:
                return ["podman", "compose"]
        except (subprocess.TimeoutExpired, OSError):
            pass

        # Fall back to podman-compose (standalone)
        if shutil.which("podman-compose"):
            return ["podman-compose"]

    # Docker uses 'docker compose'
    return [runtime, "compose"]


def start_containers(runtime: str, project_root: Path) -> bool:
    """Start the containers using the detected runtime."""
    compose_cmd = get_compose_command(runtime)

    # Build the image first (required for podman with local images)
    print_info("Building container image (this may take a while on first run)...")
    try:
        result = subprocess.run(
            [*compose_cmd, "build"],
            cwd=project_root,
            timeout=900,  # 15 minute timeout for build
        )
        if result.returncode != 0:
            print_error("Failed to build container image")
            print_info("Check the build output above for errors")
            return False
        print_success("Container image built successfully")
    except subprocess.TimeoutExpired:
        print_error("Container build timed out (15 minutes)")
        print_info("Try running the build manually to see progress")
        return False
    except OSError as e:
        print_error(f"Failed to build container: {e}")
        return False

    # Start the containers
    print_info("Starting containers...")
    try:
        result = subprocess.run(
            [*compose_cmd, "up", "-d"],
            cwd=project_root,
            timeout=600,  # 10 minute timeout for start (slower boxes need more time)
        )
        if result.returncode != 0:
            print_error("Failed to start containers")
            return False
        return True
    except subprocess.TimeoutExpired:
        print_error("Container startup timed out (10 minutes)")
        return False
    except OSError as e:
        print_error(f"Failed to start containers: {e}")
        return False


def wait_for_healthy(runtime: str, project_root: Path, timeout: int = 600) -> bool:
    """Wait for containers to become healthy."""
    compose_cmd = get_compose_command(runtime)

    print_info(f"Waiting for containers to become healthy (timeout: {timeout}s)...")

    start_time = time.time()
    db_healthy = False
    app_healthy = False

    while time.time() - start_time < timeout:
        try:
            # Try direct health check first (most reliable)
            if not app_healthy:
                try:
                    health_result = subprocess.run(
                        ["curl", "-fsS", "http://localhost:8080/health"],
                        capture_output=True,
                        timeout=5,
                    )
                    if health_result.returncode == 0:
                        if not app_healthy:
                            print_success("Application health check passed")
                        app_healthy = True
                except (subprocess.TimeoutExpired, OSError, FileNotFoundError):
                    pass

            # Check database health via podman/docker inspect
            if not db_healthy:
                try:
                    # Get container name
                    ps_result = subprocess.run(
                        [*compose_cmd, "ps", "-q", "melodee-db"],
                        cwd=project_root,
                        capture_output=True,
                        text=True,
                        timeout=10,
                    )
                    if ps_result.returncode == 0 and ps_result.stdout.strip():
                        container_id = ps_result.stdout.strip().split("\n")[0]

                        # Check health status with podman/docker inspect
                        inspect_cmd = [
                            compose_cmd[0],
                            "inspect",
                            "--format",
                            "{{.State.Health.Status}}",
                            container_id,
                        ]
                        inspect_result = subprocess.run(
                            inspect_cmd, capture_output=True, text=True, timeout=10
                        )
                        if inspect_result.returncode == 0:
                            health_status = inspect_result.stdout.strip()
                            if health_status == "healthy":
                                if not db_healthy:
                                    print_success("Database container is healthy")
                                db_healthy = True
                except (subprocess.TimeoutExpired, OSError):
                    pass

            # If both are healthy, we're done
            if db_healthy and app_healthy:
                return True

        except (subprocess.TimeoutExpired, OSError):
            pass

        # Show progress every 10 seconds
        elapsed = int(time.time() - start_time)
        if elapsed > 0 and elapsed % 10 == 0:
            # Only print if we just hit a 10-second mark
            current_elapsed = int(time.time() - start_time)
            if current_elapsed == elapsed:
                print_info(f"  Still waiting... ({elapsed}s)")

        time.sleep(2)

    # Timeout reached
    if not db_healthy:
        print_warning("Database container did not become healthy in time")
    if not app_healthy:
        print_warning("Application container did not become healthy in time")

    return False


def show_container_logs(runtime: str, project_root: Path, lines: int = 50):
    """Show recent container logs for debugging."""
    compose_cmd = get_compose_command(runtime)

    print("\n" + "-" * 60)
    print("  Recent Container Logs")
    print("-" * 60 + "\n")

    try:
        subprocess.run(
            [*compose_cmd, "logs", "--tail", str(lines)], cwd=project_root, timeout=30
        )
    except (subprocess.TimeoutExpired, OSError) as e:
        print_error(f"Failed to retrieve logs: {e}")


def get_expected_version(project_root: Path) -> str | None:
    """Extract the version from Melodee.Blazor.csproj file."""
    csproj_path = project_root / "src" / "Melodee.Blazor" / "Melodee.Blazor.csproj"

    if not csproj_path.exists():
        return None

    try:
        import xml.etree.ElementTree as ET

        tree = ET.parse(csproj_path)
        root = tree.getroot()

        # Find VersionPrefix element
        for prop_group in root.findall(".//PropertyGroup"):
            version_prefix = prop_group.find("VersionPrefix")
            if version_prefix is not None and version_prefix.text:
                return version_prefix.text.strip()

        return None
    except Exception:
        return None


def get_container_version(runtime: str, container_name: str) -> str | None:
    """Get the version from a running container by checking the deps.json file."""
    try:
        result = subprocess.run(
            [runtime, "exec", container_name, "cat", "/app/Melodee.Blazor.deps.json"],
            capture_output=True,
            text=True,
            timeout=10,
        )

        if result.returncode != 0:
            return None

        import json

        deps = json.loads(result.stdout)

        if "targets" not in deps:
            return None

        # Find the Melodee.Blazor version in the targets
        for target_value in deps["targets"].values():
            for lib_key in target_value.keys():
                if "Melodee.Blazor/" in lib_key:
                    # Extract version from "Melodee.Blazor/1.7.2+build..." format
                    version_with_build = lib_key.split("/")[1]
                    # Remove the build timestamp part (everything after +)
                    version = version_with_build.split("+")[0]
                    return version

        return None
    except Exception:
        return None


def find_melodee_container(runtime: str) -> str | None:
    """Find the name of the running Melodee Blazor container."""
    try:
        result = subprocess.run(
            [runtime, "ps", "--filter", "name=melodee", "--format", "{{.Names}}"],
            capture_output=True,
            text=True,
            timeout=10,
        )

        if result.returncode != 0 or not result.stdout.strip():
            return None

        for name in result.stdout.strip().split("\n"):
            if "blazor" in name.lower() or "melodee" in name.lower():
                return name

        return None
    except Exception:
        return None


def check_existing_containers(runtime: str, project_root: Path) -> tuple[bool, str]:
    """Check if containers already exist from a previous run."""
    compose_cmd = get_compose_command(runtime)

    try:
        # Try compose ps first (without -a as podman-compose doesn't support it)
        result = subprocess.run(
            [*compose_cmd, "ps"],
            cwd=project_root,
            capture_output=True,
            text=True,
            timeout=10,
        )

        # Check if compose ps worked
        if result.returncode == 0 and result.stdout.strip():
            # Check if there are any melodee containers
            if "melodee" in result.stdout.lower():
                return True, result.stdout

        # Fallback: Check using runtime directly (more reliable)
        result = subprocess.run(
            [
                runtime,
                "ps",
                "--filter",
                "name=melodee",
                "--format",
                "table {{.ID}}\t{{.Image}}\t{{.Status}}\t{{.Names}}",
            ],
            capture_output=True,
            text=True,
            timeout=10,
        )

        if result.returncode == 0 and result.stdout.strip():
            # Look for any line with melodee in it (case insensitive)
            lines = result.stdout.strip().split("\n")
            # Skip header line if present
            for line in lines[1:] if len(lines) > 1 else lines:
                if line.strip() and "melodee" in line.lower():
                    return True, result.stdout

        return False, ""
    except (subprocess.TimeoutExpired, OSError):
        return False, ""


def update_containers(
    runtime: str,
    project_root: Path,
    skip_confirm: bool = False,
    prune: bool = False,
    prune_all: bool = False,
) -> bool:
    """
    Safely update running containers to latest code.

    This will:
    1. Verify containers are currently running
    2. Pull latest git changes (optional, user may have already done this)
    3. Build new image
    4. Recreate containers with new image (volumes preserved)
    5. Wait for healthy status

    Args:
        runtime: Container runtime (podman or docker)
        project_root: Path to project root
        skip_confirm: If True, skip confirmation prompts (for automated deployments)
    """
    compose_cmd = get_compose_command(runtime)

    print("\n" + "=" * 60)
    print("  Melodee Container Update")
    print("=" * 60 + "\n")

    # Check if containers exist
    exists, status = check_existing_containers(runtime, project_root)
    if not exists:
        print_error("No existing Melodee containers found")
        print_info("Use --start to start containers for the first time")
        return False

    print_info("Current container status:")
    print(status)

    # Show current and expected versions
    expected_version = get_expected_version(project_root)
    container_name = find_melodee_container(runtime)
    current_version = (
        get_container_version(runtime, container_name) if container_name else None
    )

    print_info("\nVersion information:")
    if expected_version:
        print_info(f"  Expected (from source): {expected_version}")
    if current_version:
        print_info(f"  Current (in container): {current_version}")

        # Compare versions if both are available
        if expected_version and current_version:
            if expected_version == current_version:
                print_success("  ✓ Container is running the expected version")
            else:
                print_warning(
                    f"  ⚠ Version mismatch! Update will upgrade {current_version} → {expected_version}"
                )
    else:
        print_warning("  Could not determine current container version")

    # Confirm update
    if not skip_confirm:
        print_warning("\nThis will rebuild and restart the Melodee containers.")
        print_info("Your data volumes will be preserved.")
        response = input("\n  Proceed with update? (y/N): ").strip().lower()
        if response != "y":
            print_info("Update cancelled")
            return False
    else:
        print_info("\nProceeding with update (--yes flag specified)")

    # Check for uncommitted changes that might affect build
    try:
        result = subprocess.run(
            ["git", "status", "--porcelain"],
            cwd=project_root,
            capture_output=True,
            text=True,
            timeout=10,
        )
        if result.returncode == 0 and result.stdout.strip():
            print_warning("\nUncommitted changes detected in repository:")
            # Show only relevant files
            for line in result.stdout.strip().split("\n")[:10]:
                print(f"    {line}")
            if result.stdout.strip().count("\n") > 10:
                print("    ... and more")
            print_info("These changes will be included in the build")
    except (subprocess.TimeoutExpired, OSError, FileNotFoundError):
        pass  # Git not available or not a git repo, skip check

    # Show current git commit
    try:
        result = subprocess.run(
            ["git", "log", "-1", "--oneline"],
            cwd=project_root,
            capture_output=True,
            text=True,
            timeout=10,
        )
        if result.returncode == 0:
            print_info(f"\nBuilding from commit: {result.stdout.strip()}")
    except (subprocess.TimeoutExpired, OSError, FileNotFoundError):
        pass

    print()

    # Build new image
    print_info("Step 1/3: Building new container image...")
    try:
        result = subprocess.run(
            [*compose_cmd, "build", "--no-cache"],
            cwd=project_root,
            timeout=900,  # 15 minute timeout
        )
        if result.returncode != 0:
            print_error("Failed to build new image")
            print_info("Check the build output above for errors")
            return False
        print_success("New image built successfully")
    except subprocess.TimeoutExpired:
        print_error("Build timed out (15 minutes)")
        return False
    except OSError as e:
        print_error(f"Build failed: {e}")
        return False

    # Stop and recreate containers (preserves volumes)
    print_info("\nStep 2/3: Updating containers...")
    try:
        # Use 'up -d' which will recreate containers if image changed
        result = subprocess.run(
            [*compose_cmd, "up", "-d"],
            cwd=project_root,
            timeout=600,  # 10 minute timeout for container recreation
        )
        if result.returncode != 0:
            print_error("Failed to update containers")
            return False
        print_success("Containers updated")
    except subprocess.TimeoutExpired:
        print_error("Container update timed out (10 minutes)")
        return False
    except OSError as e:
        print_error(f"Update failed: {e}")
        return False

    # Wait for healthy
    print_info("\nStep 3/3: Waiting for containers to become healthy...")
    healthy = wait_for_healthy(runtime, project_root, timeout=600)

    if healthy:
        print_success("\nUpdate completed successfully!")

        # Show git commit info
        try:
            result = subprocess.run(
                ["git", "log", "-1", "--format=%h %s (%cr)"],
                cwd=project_root,
                capture_output=True,
                text=True,
                timeout=10,
            )
            if result.returncode == 0:
                print_info(f"Built from commit: {result.stdout.strip()}")
        except (subprocess.TimeoutExpired, OSError, FileNotFoundError):
            pass

        # Verify actual running version in container
        print_info("\nVerifying running version...")
        expected_version = get_expected_version(project_root)
        container_name = find_melodee_container(runtime)

        if container_name:
            # Give the app a moment to fully start
            time.sleep(2)
            actual_version = get_container_version(runtime, container_name)

            if actual_version:
                print_success(f"Container is now running: {actual_version}")

                # Verify it matches expected version
                if expected_version and actual_version == expected_version:
                    print_success(
                        f"✓ Successfully updated to version {expected_version}"
                    )
                elif expected_version and actual_version != expected_version:
                    print_error(
                        f"✗ Version mismatch! Expected {expected_version} but container is running {actual_version}"
                    )
                    print_warning(
                        "The container image may not have been rebuilt properly."
                    )
                    print_info(
                        "Try running: podman compose build --no-cache && podman compose up -d"
                    )
                    return False
            else:
                print_warning("Could not verify container version")
        else:
            print_warning("Could not find Melodee container to verify version")

        # Optional cleanup: repeated builds can leave many dangling images/build cache
        if prune or prune_all:
            prune_container_storage(
                runtime, prune_all=prune_all, skip_confirm=skip_confirm
            )
        else:
            maybe_suggest_prune(runtime)

        return True
    else:
        print_warning("\nContainers updated but may not be fully healthy")
        print_info("Showing recent logs:")
        show_container_logs(runtime, project_root, lines=50)
        return False


def print_next_steps(runtime: str, started: bool, healthy: bool = False):
    """Print next steps for the user."""
    compose_cmd = " ".join(get_compose_command(runtime))

    print("\n" + "-" * 60)
    print("  Next Steps")
    print("-" * 60 + "\n")

    if started and healthy:
        print_success("Melodee is up and running!")
        print_info("Access Melodee at: http://localhost:8080")
        print()
        print_info("Useful commands:")
        print(f"    {compose_cmd} logs -f        # View logs")
        print(f"    {compose_cmd} ps             # Check status")
        print(f"    {compose_cmd} down           # Stop containers")
        print(f"    {compose_cmd} build          # Rebuild image")
        print(f"    {compose_cmd} up -d          # Start containers")
    elif started:
        print_warning("Containers started but may not be fully healthy yet")
        print_info("Access Melodee at: http://localhost:8080")
        print_info("(May take a minute for the application to fully start)")
        print()
        print_info("Check status with:")
        print(f"    {compose_cmd} ps")
        print(f"    {compose_cmd} logs -f")
    else:
        print_info("To start Melodee, run:")
        print(f"    {compose_cmd} build")
        print(f"    {compose_cmd} up -d")
        print()
        print_info("Then access Melodee at: http://localhost:8080")

    print()
    print_info("Default admin credentials are set during first login.")
    print_info("Check the README.md for more information.")
    print()


def check_volume_permissions(runtime: str, project_root: Path) -> bool:
    """
    Check volume permissions for rootless podman issues.

    This diagnostic function helps identify UID mapping problems where
    files in volumes are owned by sub-UIDs (e.g., 100998) instead of
    the expected host user or melodee user.

    Returns True if permissions look correct, False if issues found.
    """
    print("\n" + "=" * 60)
    print("  Volume Permissions Diagnostic")
    print("=" * 60 + "\n")

    # Check if rootless
    if runtime == "podman":
        try:
            result = subprocess.run(
                ["podman", "info", "--format", "{{.Host.Security.Rootless}}"],
                capture_output=True,
                text=True,
                timeout=10,
            )
            is_rootless = (
                result.returncode == 0 and result.stdout.strip().lower() == "true"
            )

            if is_rootless:
                print_info("Running in rootless podman mode")
            else:
                print_info("Running in rootful podman mode")
        except (subprocess.TimeoutExpired, OSError, FileNotFoundError):
            print_warning("Could not determine if podman is rootless")
            is_rootless = False
    else:
        print_info(f"Running with {runtime} (typically rootful)")
        is_rootless = False

    print()

    # Get volume storage path
    volume_base = None
    if runtime == "podman" and is_rootless:
        home = os.path.expanduser("~")
        volume_base = Path(home) / ".local/share/containers/storage/volumes"
    elif runtime == "podman":
        volume_base = Path("/var/lib/containers/storage/volumes")
    elif runtime == "docker":
        volume_base = Path("/var/lib/docker/volumes")

    if not volume_base or not volume_base.exists():
        print_warning(f"Volume storage directory not found: {volume_base}")
        return False

    print_info(f"Checking volumes in: {volume_base}")
    print()

    # Volume names to check
    # Note: melodee_db_data is expected to have sub-UID ownership (database internals)
    user_volumes = [
        "melodee_inbound",
        "melodee_staging",
        "melodee_storage",
        "melodee_logs",
    ]

    db_volumes = ["melodee_db_data"]

    issues_found = False
    current_uid = os.getuid()

    # Check user-accessible volumes (should be owned by host user)
    for vol_name in user_volumes:
        vol_path = volume_base / vol_name / "_data"

        if not vol_path.exists():
            print_warning(f"{vol_name}: Volume not found")
            continue

        try:
            # Get directory stats
            stat_info = vol_path.stat()
            owner_uid = stat_info.st_uid
            owner_gid = stat_info.st_gid

            # Check for sub-UID (typically in 100000+ range)
            if owner_uid > 65536:
                print_error(f"{vol_name}: Owned by sub-UID {owner_uid}:{owner_gid}")
                print_info("  ↳ This indicates user namespace mapping issue")
                issues_found = True
            elif owner_uid == current_uid:
                print_success(
                    f"{vol_name}: Owned by current user ({owner_uid}:{owner_gid})"
                )
            else:
                # Check if it's a known system user
                try:
                    import pwd

                    user_info = pwd.getpwuid(owner_uid)
                    print_info(
                        f"{vol_name}: Owned by {user_info.pw_name} ({owner_uid}:{owner_gid})"
                    )
                except KeyError:
                    print_warning(f"{vol_name}: Owned by UID {owner_uid}:{owner_gid}")

        except PermissionError:
            print_error(f"{vol_name}: Permission denied (cannot read directory)")
            issues_found = True
        except OSError as e:
            print_error(f"{vol_name}: Error checking permissions: {e}")
            issues_found = True

    print()

    # Check database volumes (these SHOULD have sub-UID ownership in rootless mode)
    for vol_name in db_volumes:
        vol_path = volume_base / vol_name / "_data"

        if not vol_path.exists():
            print_warning(f"{vol_name}: Volume not found")
            continue

        try:
            stat_info = vol_path.stat()
            owner_uid = stat_info.st_uid
            owner_gid = stat_info.st_gid

            if owner_uid > 65536:
                print_info(
                    f"{vol_name}: Owned by sub-UID {owner_uid}:{owner_gid} (expected for database)"
                )
                print_info(
                    "  ↳ PostgreSQL uses default namespace mapping (this is correct)"
                )
            elif owner_uid == current_uid:
                print_info(
                    f"{vol_name}: Owned by current user ({owner_uid}:{owner_gid})"
                )
            else:
                try:
                    import pwd

                    user_info = pwd.getpwuid(owner_uid)
                    print_info(
                        f"{vol_name}: Owned by {user_info.pw_name} ({owner_uid}:{owner_gid})"
                    )
                except KeyError:
                    print_info(f"{vol_name}: Owned by UID {owner_uid}:{owner_gid}")
        except (PermissionError, OSError) as e:
            print_warning(f"{vol_name}: Cannot check permissions: {e}")

    print()

    # Check compose.override.yml
    override_file = project_root / "compose.override.yml"
    if override_file.exists():
        print_info("Checking compose.override.yml configuration:")
        try:
            content = override_file.read_text()
            if "userns_mode" in content:
                print_success("  ✓ userns_mode configured (UID mapping fix applied)")
            else:
                print_warning("  ⚠ No userns_mode found (may have UID mapping issues)")
                issues_found = True

            # Extract user setting
            import re

            user_match = re.search(r'user:\s*["\']?(\d+):(\d+)["\']?', content)
            if user_match:
                print_info(
                    f"  Container runs as UID:GID {user_match.group(1)}:{user_match.group(2)}"
                )
        except Exception as e:
            print_error(f"  Error reading override file: {e}")
    else:
        if is_rootless:
            print_warning("No compose.override.yml found")
            print_info("  ↳ Re-run setup to create override for rootless podman")
            issues_found = True
        else:
            print_info("No compose.override.yml (not needed for rootful mode)")

    print()

    # Summary and recommendations
    if issues_found:
        print_error("ISSUES FOUND!")
        print()
        print_info("Recommendations:")
        print_info("  1. Stop containers: podman compose down -v")
        print_info("  2. Re-run setup: python scripts/run-container-setup.py --start")
        print_info("  3. Ensure userns_mode is configured in compose.override.yml")
        print()
        return False
    else:
        print_success("No permission issues detected!")
        return True


def main():
    """Main entry point."""
    # Handle help first
    if "--help" in sys.argv or "-h" in sys.argv:
        print_help()

    print_banner()

    # Parse arguments
    start_after_setup = "--start" in sys.argv
    check_only = "--check-only" in sys.argv
    check_permissions = "--check-permissions" in sys.argv
    force_overwrite = "--force" in sys.argv
    update_mode = "--update" in sys.argv
    skip_confirm = "--yes" in sys.argv or "-y" in sys.argv

    prune_requested = "--prune" in sys.argv
    prune_all = "--prune-all" in sys.argv
    if prune_all:
        prune_requested = True

    # Get project root first for preflight checks
    project_root = get_project_root()
    print(f"Project root: {project_root}")

    # Detect container runtime early (needed for multiple modes)
    print("\nDetecting container runtime...")
    runtime = detect_container_runtime()

    if not runtime and not check_permissions:
        # Only offer to install if not just checking permissions
        runtime = offer_install_podman()

        if not runtime:
            print()
            print_error("Cannot continue without a container runtime.")
            print_info("  - Podman: https://podman.io/getting-started/installation")
            print_info("  - Docker: https://docs.docker.com/get-docker/")
            sys.exit(1)

    # Handle permission check mode
    if check_permissions:
        if not runtime:
            print_error("Cannot check permissions without a container runtime")
            sys.exit(1)
        success = check_volume_permissions(runtime, project_root)
        sys.exit(0 if success else 1)

    # Run preflight checks
    if not run_preflight_checks(project_root):
        if check_only:
            print_error("Preflight checks failed")
            sys.exit(1)
        print_warning("Some preflight checks failed, but continuing...")
        print_info("Fix the issues above for best results")
    else:
        print_success("All preflight checks passed!")

    if check_only:
        print_info("\n--check-only specified, exiting after checks")
        sys.exit(0)
        runtime = offer_install_podman()

        if not runtime:
            print()
            print_error("Cannot continue without a container runtime.")
            print_info("  - Podman: https://podman.io/getting-started/installation")
            print_info("  - Docker: https://docs.docker.com/get-docker/")
            sys.exit(1)

    # Check compose availability
    print("\nChecking compose availability...")
    if not check_compose_available(runtime):
        print_error(f"Compose not available for {runtime}!")
        if runtime == "podman":
            print_info("Install podman-compose: pip install podman-compose")
            print_info("Or ensure podman-compose plugin is installed")
        else:
            print_info(
                "Install Docker Compose: https://docs.docker.com/compose/install/"
            )
        sys.exit(1)

    # Handle update mode separately
    if update_mode:
        success = update_containers(
            runtime,
            project_root,
            skip_confirm=skip_confirm,
            prune=prune_requested,
            prune_all=prune_all,
        )
        sys.exit(0 if success else 1)

    # Check for existing containers
    exists, status = check_existing_containers(runtime, project_root)
    if exists:
        print_warning("\nExisting Melodee containers found:")
        print(status)
        print_info("\nOptions:")
        print_info("  1. Use --update to safely update to latest code (preserves data)")
        print_info("  2. Continue below to remove and start fresh")
        print()
        response = (
            input("  Stop and remove existing containers? (y/N): ").strip().lower()
        )
        if response == "y":
            compose_cmd = get_compose_command(runtime)
            # Don't use -v flag here to preserve volumes by default
            print_warning("Remove volumes too? This will DELETE ALL DATA!")
            remove_volumes = input("  Remove volumes? (y/N): ").strip().lower()
            if remove_volumes == "y":
                subprocess.run(
                    [*compose_cmd, "down", "-v"], cwd=project_root, timeout=60
                )
                print_success("Existing containers and volumes removed")
            else:
                subprocess.run([*compose_cmd, "down"], cwd=project_root, timeout=60)
                print_success("Existing containers removed (volumes preserved)")

    # Create .env file
    print("\nSetting up environment...")
    if not create_env_file(project_root, overwrite=force_overwrite):
        sys.exit(1)

    # Ensure .env is gitignored
    ensure_gitignore_has_env(project_root)

    # For rootless podman, create compose.override.yml to fix permissions
    use_melodee_user = False
    if is_rootless_podman(runtime):
        print_info("\nDetected rootless podman - configuring user permissions...")

        # Offer to create dedicated melodee user for multi-user setups
        if not skip_confirm:
            use_melodee_user = offer_create_melodee_user()

        if not create_compose_override_for_rootless(project_root, use_melodee_user):
            print_warning("Could not create compose.override.yml")
            print_info("You may experience file permission issues with volumes")
        else:
            ensure_gitignore_has_override(project_root)

    # Optionally start containers
    started = False
    healthy = False
    if start_after_setup:
        print("\nStarting containers...")
        started = start_containers(runtime, project_root)
        if started:
            print_success("Containers started!")

            # Wait for healthy status
            healthy = wait_for_healthy(runtime, project_root)

            if not healthy:
                print_warning("Containers may not be fully healthy")
                print_info("Showing recent logs for debugging:")
                show_container_logs(runtime, project_root, lines=30)
            else:
                # Run permission check after successful startup
                print("\nVerifying volume permissions...")
                print_info("Waiting a moment for containers to create initial files...")
                time.sleep(3)  # Give containers time to create files in volumes

                permissions_ok = check_volume_permissions(runtime, project_root)
                if not permissions_ok:
                    print_warning(
                        "\nPermission issues detected but containers are running"
                    )
                    print_info(
                        "Review the recommendations above to fix permission issues"
                    )
                else:
                    print_success("\nVolume permissions verified - setup complete!")
        else:
            print_error("Failed to start containers")
            print_info("Showing recent logs for debugging:")
            show_container_logs(runtime, project_root, lines=50)

    # Optional cleanup / guidance: repeated builds can accumulate dangling images and cache
    if start_after_setup and started:
        if prune_requested:
            prune_container_storage(
                runtime, prune_all=prune_all, skip_confirm=skip_confirm
            )
        else:
            maybe_suggest_prune(runtime)

    # Print next steps
    print_next_steps(runtime, started, healthy)

    return 0 if (not start_after_setup or started) else 1


if __name__ == "__main__":
    sys.exit(main())
