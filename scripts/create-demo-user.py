#!/usr/bin/env python3
"""
Demo User Creator for Melodee

This script creates a properly configured demo user with encrypted password
that matches Melodee's authentication expectations.

Usage:
    python3 create-demo-user.py [--connection-string CONNECTION_STRING]

Environment Variables:
    MELODEE_CONNECTION_STRING  PostgreSQL connection string
    MELODEE_ENCRYPTION_KEY     Encryption key (defaults to development key)
"""

import base64
import hashlib
import os
import sys
import uuid
from datetime import datetime, timezone

# Try to import required libraries
try:
    import psycopg2
    from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
    from cryptography.hazmat.backends import default_backend
    from cryptography.hazmat.primitives import padding
except ImportError as e:
    print("ERROR: Required Python packages not installed")
    print("Install with: pip3 install psycopg2-binary cryptography")
    print(f"Missing: {e}")
    sys.exit(1)


class MelodeeEncryption:
    """Encryption helper matching Melodee's C# EncryptionHelper"""

    @staticmethod
    def generate_public_key() -> str:
        """Generate a random base64-encoded public key (32 bytes)"""
        return base64.b64encode(os.urandom(32)).decode("utf-8")

    @staticmethod
    def encrypt(private_key: str, plain_text: str, public_key: str) -> str:
        """
        Encrypt plaintext using AES-256-CBC with PKCS7 padding.
        Matches the C# EncryptionHelper.Encrypt method.
        """
        # Derive key and IV from private_key and public_key. This matches the C#
        # implementation, which uses similar derivation.
        key_material = (private_key + public_key).encode("utf-8")
        key = hashlib.sha256(key_material).digest()  # 32 bytes for AES-256
        iv = hashlib.md5(public_key.encode("utf-8")).digest()  # 16-byte IV

        # Pad plaintext to AES block size (128 bits = 16 bytes).
        padder = padding.PKCS7(128).padder()
        padded_data = padder.update(plain_text.encode("utf-8")) + padder.finalize()

        cipher = Cipher(
            algorithms.AES(key),
            modes.CBC(iv),
            backend=default_backend(),
        )
        encryptor = cipher.encryptor()
        encrypted_data = encryptor.update(padded_data) + encryptor.finalize()

        return base64.b64encode(encrypted_data).decode("utf-8")


def parse_connection_string(conn_str: str) -> dict[str, str]:
    """Parse .NET-style connection string to Python dict"""
    params: dict[str, str] = {}
    for part in conn_str.split(";"):
        if "=" in part:
            key, value = part.split("=", 1)
            params[key.strip().lower()] = value.strip()

    return {
        "host": params.get("host", "localhost"),
        "port": params.get("port", "5432"),
        "database": params.get("database", "melodee"),
        "user": params.get("username", "melodee"),
        "password": params.get("password", "melodee"),
    }


def create_demo_user(conn_params: dict[str, str], encryption_key: str) -> bool:
    """Create the demo user with proper encryption"""

    print("╔════════════════════════════════════════════════════════════╗")
    print("║  Creating Demo User                                        ║")
    print("╚════════════════════════════════════════════════════════════╝")
    print()

    # Generate user credentials
    username = "demo"
    email = "demo@melodee.org"
    password = "Mel0deeR0cks!"

    # Generate encryption keys
    public_key = MelodeeEncryption.generate_public_key()
    api_key = str(uuid.uuid4())

    # Encrypt password using Melodee's encryption method
    print("Encrypting demo credentials...")
    try:
        encrypted_password = MelodeeEncryption.encrypt(
            encryption_key,
            password,
            public_key,
        )
    except Exception:
        # Encryption exceptions can include secret-bearing input from a backend.
        print("ERROR: Unable to encrypt demo credentials.", file=sys.stderr)
        return False

    print(f"  Username: {username}")
    print(f"  Email: {email}")
    print("  Password and generated key material are not displayed.")
    print()

    # Connect to database
    try:
        conn = psycopg2.connect(**conn_params)
        cur = conn.cursor()

        # Check if demo user already exists
        cur.execute(
            'SELECT "Id" FROM "Users" WHERE "UserNameNormalized" = %s', ("DEMO",)
        )
        existing_user = cur.fetchone()

        if existing_user:
            print("Demo user already exists. Updating password...")

            # Update existing user
            cur.execute(
                """
                UPDATE "Users" 
                SET "PublicKey" = %s,
                    "PasswordEncrypted" = %s,
                    "LastUpdatedAt" = %s,
                    "Email" = %s,
                    "EmailNormalized" = %s
                WHERE "UserNameNormalized" = %s
            """,
                (
                    public_key,
                    encrypted_password,
                    datetime.now(timezone.utc),
                    email,
                    email.upper(),
                    "DEMO",
                ),
            )
        else:
            print("Creating new demo user...")

            # Insert new user
            cur.execute(
                """
                INSERT INTO "Users" (
                    "ApiKey",
                    "UserName",
                    "UserNameNormalized",
                    "Email",
                    "EmailNormalized",
                    "PublicKey",
                    "PasswordEncrypted",
                    "IsAdmin",
                    "IsEditor",
                    "HasSettingsRole",
                    "HasDownloadRole",
                    "HasUploadRole",
                    "HasPlaylistRole",
                    "HasCoverArtRole",
                    "HasCommentRole",
                    "HasPodcastRole",
                    "HasStreamRole",
                    "HasJukeboxRole",
                    "HasShareRole",
                    "IsScrobblingEnabled",
                    "TimeZoneId",
                    "CreatedAt",
                    "IsLocked"
                ) VALUES (
                    %s, %s, %s, %s, %s, %s, %s,
                    false, false, true, true, false, true,
                    true, true, true, true, true, true,
                    false, 'UTC', %s, false
                )
            """,
                (
                    api_key,
                    username,
                    username.upper(),
                    email,
                    email.upper(),
                    public_key,
                    encrypted_password,
                    datetime.now(timezone.utc),
                ),
            )

        conn.commit()
        cur.close()
        conn.close()

        print()
        print("✓ Demo user created successfully!")
        print()
        print("Demo account:")
        print("  Username: demo")
        print("  Email: demo@melodee.org")
        print("  Password and generated key material are not displayed.")
        print()

        return True

    except Exception:
        # Database exceptions may echo connection parameters or SQL values.
        print(
            "ERROR: Failed to create or update the demo user.",
            file=sys.stderr,
        )
        return False


def main():
    # Get connection string
    conn_str = os.getenv("MELODEE_CONNECTION_STRING")
    if not conn_str:
        if "--connection-string" in sys.argv:
            idx = sys.argv.index("--connection-string")
            if idx + 1 < len(sys.argv):
                conn_str = sys.argv[idx + 1]

    if not conn_str:
        print("ERROR: PostgreSQL connection string not provided")
        print(
            "Set MELODEE_CONNECTION_STRING environment variable or use --connection-string option"
        )
        sys.exit(1)

    # Get encryption key
    encryption_key = os.getenv(
        "MELODEE_ENCRYPTION_KEY",
        "H+Kiik6VMKfTD2MesF1GoMjczTrD5RhuKckJ5+/UQWOdWajGcsEC3yEnlJ5eoy8Y",
    )

    # Parse connection string
    conn_params = parse_connection_string(conn_str)

    # Create demo user
    success = create_demo_user(conn_params, encryption_key)

    sys.exit(0 if success else 1)


if __name__ == "__main__":
    main()
