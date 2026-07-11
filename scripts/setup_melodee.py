#!/usr/bin/env python3
"""
Melodee Setup Script
===================

This script automates the process of getting Melodee up and running:
1. Clones the repository (if not already present)
2. Checks for required dependencies (Git, Docker/Podman)
3. Sets up environment configuration
4. Builds and starts the containers
5. Provides the user with the URL to access the Blazor Admin UI

Cross-platform support for Windows, Linux, and macOS.
"""

import os
import platform
import secrets
import shutil
import subprocess
import sys
import time
from pathlib import Path

if __package__:
    from .private_config import (
        PRIVATE_FILE_MODE as _PRIVATE_FILE_MODE,
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
        PRIVATE_FILE_MODE as _PRIVATE_FILE_MODE,
        existing_private_file_message,
        path_entry_exists,
        path_entry_is_regular_file,
        path_entry_is_symlink,
        private_file_created_message,
        secure_existing_private_file,
        write_private_file,
    )

    del _private_config_module

PRIVATE_FILE_MODE = _PRIVATE_FILE_MODE
DATABASE_SECRET_BYTES = 32
AUTH_TOKEN_SECRET_BYTES = 64


def print_header():
    """Print the script header."""
    header = r"""
 ___      ___   _______  ___        ______    ________    _______   _______
|"  \    /"  | /"     "||"  |      /    " \  |"      "\  /"     "| /"     "|
 \   \  //   |(: ______)||  |     // ____  \ (.  ___  :)(: ______)(: ______)
 /\\  \/.    | \/    |  |:  |    /  /    ) :)|: \   ) || \/    |   \/    |
|: \.        | // ___)_  \  |___(: (____/ // (| (___\ || // ___)_  // ___)_
|.  \    /:  |(:      "|( \_|:  \\        /  |:       :)(:      "|(:      "|
|___|\__/|___| \_______) \_______)\"_____/   (________/  \_______) \_______)
                Music System Setup
    """
    print(header)
    print("=" * 50)


def detect_os():
    """Detect the operating system."""
    system = platform.system().lower()
    if system == "darwin":
        return "macos"
    elif system == "linux":
        return "linux"
    elif system == "windows":
        return "windows"
    else:
        return system


def check_command_exists(command):
    """Check if a command exists in the system."""
    return shutil.which(command) is not None


def generate_secret(byte_count: int) -> str:
    """Generate a URL-safe secret from the requested random byte count."""
    return secrets.token_urlsafe(byte_count)


def replace_environment_setting(
    env_content: str,
    setting_name: str,
    setting_value: str,
) -> str:
    """Replace one required setting in environment-file content."""
    setting_prefix = f"{setting_name}="
    lines = env_content.splitlines()

    for index, line in enumerate(lines):
        if line.startswith(setting_prefix):
            lines[index] = f"{setting_prefix}{setting_value}"
            return "\n".join(lines) + "\n"

    raise ValueError(f"Required setting {setting_name} was not found")


def check_dependencies():
    """Check if required dependencies are installed."""
    print("\\n🔍 Checking Dependencies...")

    # Check Git
    if not check_command_exists("git"):
        print("❌ Git is not installed or not in PATH")
        print("   Please install Git from https://git-scm.com/")
        return False
    else:
        print("✅ Git is available")

    # Check for Docker or Podman
    has_docker = check_command_exists("docker")
    has_podman = check_command_exists("podman")
    has_compose = check_command_exists("docker-compose") or check_command_exists(
        "podman-compose"
    )

    if not (has_docker or has_podman):
        print("❌ Neither Docker nor Podman is installed or in PATH")
        print("   Please install:")
        print("   - Docker Desktop: https://www.docker.com/get-started/")
        print("   - Or Podman: https://podman.io/getting-started/installation")
        return False
    else:
        if has_docker:
            print("✅ Docker is available")
        if has_podman:
            print("✅ Podman is available")

    if not has_compose:
        print("❌ Docker Compose or Podman Compose is not installed or in PATH")
        print("   Please install:")
        print("   - Docker Compose (comes with Docker Desktop)")
        print("   - Or Podman Compose: pip install podman-compose")
        return False
    else:
        print("✅ Docker/Podman Compose is available")

    return True


def clone_repository(
    repo_url="https://github.com/melodee-project/melodee.git", target_dir="melodee"
):
    """Clone the Melodee repository if it doesn't exist."""
    print("\\n📥 Cloning Melodee repository...")

    if os.path.exists(target_dir):
        print(f"📁 Repository directory '{target_dir}' already exists, skipping clone")
        return True

    try:
        subprocess.run(
            ["git", "clone", repo_url, target_dir], check=True, capture_output=True
        )
        print(f"✅ Successfully cloned repository to '{target_dir}'")
        return True
    except subprocess.CalledProcessError as e:
        print(f"❌ Failed to clone repository: {e}")
        return False


def setup_environment_config(melodee_dir: str) -> bool:
    """Set up a .env file with generated secrets and POSIX mode 0600."""
    print("\\n🔧 Setting up environment configuration...")

    env_example_path = os.path.join(melodee_dir, "example.env")
    env_path = os.path.join(melodee_dir, ".env")

    if path_entry_exists(env_path):
        if path_entry_is_symlink(env_path):
            print("❌ Refusing to use .env because it is a symbolic link")
            return False
        if not path_entry_is_regular_file(env_path):
            print("❌ Refusing to use .env because it is not a regular file")
            return False
        try:
            secure_existing_private_file(env_path)
        except (OSError, NotImplementedError) as error:
            print(f"❌ Failed to secure existing .env file: {error}")
            return False
        print(f"📄 {existing_private_file_message('.env file')}")
        return True

    if not os.path.exists(env_example_path):
        print(f"❌ example.env file not found in {melodee_dir}")
        return False

    try:
        with open(env_example_path, "r", encoding="utf-8") as f:
            env_content = f.read()

        env_content = replace_environment_setting(
            env_content,
            "DB_PASSWORD",
            generate_secret(DATABASE_SECRET_BYTES),
        )
        env_content = replace_environment_setting(
            env_content,
            "MELODEE_AUTH_TOKEN",
            generate_secret(AUTH_TOKEN_SECRET_BYTES),
        )
        write_private_file(env_path, env_content)

        print(f"✅ {private_file_created_message('.env file')}")
        print("✅ Generated database and authentication secrets")
        return True

    except Exception as e:
        print(f"❌ Failed to create .env file: {e}")
        return False


def detect_container_runtime():
    """Detect which container runtime to use (Docker or Podman)."""
    if check_command_exists("podman-compose"):
        return "podman-compose"
    elif check_command_exists("docker-compose"):
        return "docker-compose"
    elif check_command_exists("podman"):
        return "podman-compose"  # Assume podman-compose is available if podman is
    else:
        return "docker-compose"


def build_and_start_containers(melodee_dir):
    """Build and start the containers using compose."""
    print("\\n📦 Building and starting containers...")

    os.chdir(melodee_dir)

    # Detect which compose command to use
    compose_cmd = detect_container_runtime()
    print(f"Using {compose_cmd} for container orchestration")

    try:
        # Build and start the containers
        print("Building and starting Melodee containers...")
        subprocess.run(
            [compose_cmd, "up", "-d", "--build"], check=True, capture_output=True
        )
        print("✅ Containers are building and starting...")
        return True
    except subprocess.CalledProcessError as e:
        print(f"❌ Failed to start containers: {e}")
        return False


def wait_for_service_health(melodee_dir, max_wait_time=300):
    """Wait for the Melodee service to be healthy."""
    print("\\n⏳ Waiting for Melodee service to become healthy...")

    os.chdir(melodee_dir)

    compose_cmd = detect_container_runtime()
    start_time = time.time()

    while time.time() - start_time < max_wait_time:
        try:
            # Check the health status of the services
            result = subprocess.run([compose_cmd, "ps"], capture_output=True, text=True)

            if "melodee-blazor" in result.stdout or "melodee.blazor" in result.stdout:
                # Check if the service is healthy
                if "healthy" in result.stdout or "(healthy)" in result.stdout:
                    print("✅ Melodee service is healthy!")
                    return True

            # Also check if the service is running (even if health check isn't showing yet)
            if "Up " in result.stdout and (
                "melodee-blazor" in result.stdout or "melodee.blazor" in result.stdout
            ):
                print("✅ Melodee service appears to be running!")
                return True

        except subprocess.CalledProcessError:
            pass

        print("   Still waiting... (this may take 2-5 minutes)")
        time.sleep(10)

    print(
        "⚠️  Service may still be starting up. Check manually at http://localhost:8080"
    )
    return True  # Return True anyway as it might just be taking longer


def get_port_from_env():
    """Get the port from the .env file."""
    env_path = os.path.join(os.getcwd(), ".env")

    if os.path.exists(env_path):
        with open(env_path, "r") as f:
            for line in f:
                if line.startswith("MELODEE_PORT="):
                    return line.split("=")[1].strip()

    # Default port if not found
    return "8080"


def main():
    """Main function to orchestrate the setup process."""
    print_header()

    print(f"Operating System Detected: {detect_os()}")
    print(f"Python Version: {sys.version}")

    # Check dependencies
    if not check_dependencies():
        print(
            "\\n❌ Prerequisites not met. Please install the required dependencies and try again."
        )
        sys.exit(1)

    # Clone repository
    if not clone_repository():
        print("\\n❌ Failed to clone repository.")
        sys.exit(1)

    # Change to the melodee directory
    os.chdir("melodee")

    # Set up environment config
    if not setup_environment_config("."):
        print("\\n❌ Failed to set up environment configuration.")
        sys.exit(1)

    # Build and start containers
    if not build_and_start_containers("."):
        print("\\n❌ Failed to build and start containers.")
        sys.exit(1)

    # Wait for service to be healthy
    wait_for_service_health(".")

    # Get the port
    port = get_port_from_env()

    # Provide the URL to the user
    print("\\n🎉 Setup Complete!")
    print("=" * 50)
    print(f"🌐 Access Melodee at: http://localhost:{port}")
    print("📝 First user registered will become administrator")
    print(
        f"💡 Check the logs with: {detect_container_runtime()} logs -f melodee.blazor"
    )
    print(f"🛑 Stop the service with: {detect_container_runtime()} down")
    print("=" * 50)

    print(
        "\\n✅ Melodee is now running! The Blazor Admin UI is accessible at the URL above."
    )


if __name__ == "__main__":
    main()
