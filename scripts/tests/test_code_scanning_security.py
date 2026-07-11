"""Security regression tests for Python maintenance scripts."""

import contextlib
import importlib.util
import io
import sys
import types
import unittest
from pathlib import Path
from unittest import mock

SCRIPT_DIRECTORY = Path(__file__).resolve().parents[1]


def load_script(module_name: str, script_name: str):
    """Load a hyphenated or standalone script as an importable module."""
    script_path = SCRIPT_DIRECTORY / script_name
    module_spec = importlib.util.spec_from_file_location(
        module_name,
        script_path,
    )
    if module_spec is None or module_spec.loader is None:
        raise RuntimeError(f"Unable to load script from {script_path}")
    module = importlib.util.module_from_spec(module_spec)
    sys.modules[module_name] = module
    module_spec.loader.exec_module(module)
    return module


REQUESTS_MODULE = types.ModuleType("requests")
with mock.patch.dict(sys.modules, {"requests": REQUESTS_MODULE}):
    CODE_SCANNING_EXPORTER = load_script(
        "code_scanning_exporter_under_test",
        "create_code_scanning_combined_serif.py",
    )

PSYCOPG2_MODULE = types.ModuleType("psycopg2")
PSYCOPG2_MODULE.connect = mock.Mock()
CIPHERS_MODULE = types.ModuleType("cryptography.hazmat.primitives.ciphers")
CIPHERS_MODULE.Cipher = mock.Mock()
CIPHERS_MODULE.algorithms = mock.Mock()
CIPHERS_MODULE.modes = mock.Mock()
BACKENDS_MODULE = types.ModuleType("cryptography.hazmat.backends")
BACKENDS_MODULE.default_backend = mock.Mock()
PRIMITIVES_MODULE = types.ModuleType("cryptography.hazmat.primitives")
PRIMITIVES_MODULE.padding = mock.Mock()
HAZMAT_MODULE = types.ModuleType("cryptography.hazmat")
CRYPTOGRAPHY_MODULE = types.ModuleType("cryptography")

DEMO_DEPENDENCIES = {
    "psycopg2": PSYCOPG2_MODULE,
    "cryptography": CRYPTOGRAPHY_MODULE,
    "cryptography.hazmat": HAZMAT_MODULE,
    "cryptography.hazmat.backends": BACKENDS_MODULE,
    "cryptography.hazmat.primitives": PRIMITIVES_MODULE,
    "cryptography.hazmat.primitives.ciphers": CIPHERS_MODULE,
}
with mock.patch.dict(sys.modules, DEMO_DEPENDENCIES):
    DEMO_USER = load_script(
        "create_demo_user_under_test",
        "create-demo-user.py",
    )


class LinkHeaderParserTests(unittest.TestCase):
    """Verify bounded parsing of GitHub pagination Link headers."""

    def test_parse_preserves_github_pagination_relations(self):
        """Standard GitHub next and last links retain their exact URLs."""
        next_url = "https://api.github.com/repos/example/repo/alerts?page=2"
        last_url = "https://api.github.com/repos/example/repo/alerts?page=9"
        header = f'<{next_url}>; rel="next", ' f'<{last_url}>; rel="last"'

        links = CODE_SCANNING_EXPORTER.parse_link_header(header)

        self.assertEqual(
            {"next": next_url, "last": last_url},
            links,
        )

    def test_parse_keeps_commas_inside_urls_and_quoted_parameters(self):
        """Commas inside a URI or quoted parameter do not split a link."""
        next_url = "https://api.github.com/search?labels=one,two&page=2"
        header = (
            f'<{next_url}>; title="one, two"; rel="next", '
            '<https://api.github.com/search?page=1>; rel="first"'
        )

        links = CODE_SCANNING_EXPORTER.parse_link_header(header)

        self.assertEqual(next_url, links["next"])
        self.assertEqual(
            "https://api.github.com/search?page=1",
            links["first"],
        )

    def test_parse_rejects_an_oversized_adversarial_header(self):
        """Headers above the fixed input limit are rejected before parsing."""
        repetitions = CODE_SCANNING_EXPORTER.MAX_LINK_HEADER_LENGTH
        header = "<" + "<=" * repetitions

        links = CODE_SCANNING_EXPORTER.parse_link_header(header)

        self.assertEqual({}, links)

    def test_parse_handles_malformed_repetition_without_backtracking(self):
        """A bounded '<=' repetition is processed without a vulnerable regex."""
        header = "<" + "<=" * 4_000

        links = CODE_SCANNING_EXPORTER.parse_link_header(header)

        self.assertEqual({}, links)

    def test_parse_limits_the_number_of_link_entries(self):
        """An adversarial header cannot create an unbounded result mapping."""
        entry_count = CODE_SCANNING_EXPORTER.MAX_LINK_HEADER_ENTRIES + 20
        header = ",".join(
            f'<https://api.github.com/items?page={index}>; rel="page{index}"'
            for index in range(entry_count)
        )

        links = CODE_SCANNING_EXPORTER.parse_link_header(header)

        self.assertEqual(
            CODE_SCANNING_EXPORTER.MAX_LINK_HEADER_ENTRIES,
            len(links),
        )


class DemoUserOutputTests(unittest.TestCase):
    """Verify that demo-user setup never writes secret material to output."""

    def setUp(self):
        """Reset the mocked database connector before each scenario."""
        DEMO_USER.psycopg2.connect.reset_mock()
        DEMO_USER.psycopg2.connect.side_effect = None

    def test_create_success_omits_all_credentials_and_key_material(self):
        """Successful setup reports identity and status without any secrets."""
        connection = mock.MagicMock()
        connection.cursor.return_value.fetchone.return_value = None
        DEMO_USER.psycopg2.connect.return_value = connection
        output = io.StringIO()
        sensitive_values = (
            "Mel0deeR0cks!",
            "database-password-secret",
            "private-encryption-key-secret",
            "generated-public-key-secret",
            "generated-api-key-secret",
            "encrypted-password-secret",
        )

        with mock.patch.object(
            DEMO_USER.MelodeeEncryption,
            "generate_public_key",
            return_value="generated-public-key-secret",
        ), mock.patch.object(
            DEMO_USER.MelodeeEncryption,
            "encrypt",
            return_value="encrypted-password-secret",
        ), mock.patch.object(
            DEMO_USER.uuid,
            "uuid4",
            return_value="generated-api-key-secret",
        ), contextlib.redirect_stdout(
            output
        ), contextlib.redirect_stderr(
            output
        ):
            result = DEMO_USER.create_demo_user(
                {
                    "host": "localhost",
                    "database": "melodee",
                    "user": "melodee",
                    "password": "database-password-secret",
                },
                "private-encryption-key-secret",
            )

        rendered_output = output.getvalue()
        self.assertTrue(result)
        self.assertIn("Username: demo", rendered_output)
        self.assertIn("Demo user created successfully", rendered_output)
        for sensitive_value in sensitive_values:
            self.assertNotIn(sensitive_value, rendered_output)

    def test_database_failure_does_not_echo_the_raw_exception(self):
        """A database exception containing a password is reported generically."""
        secret = "database-password-secret"
        DEMO_USER.psycopg2.connect.side_effect = RuntimeError(
            f"connection failed for Password={secret}"
        )
        output = io.StringIO()

        with mock.patch.object(
            DEMO_USER.MelodeeEncryption,
            "generate_public_key",
            return_value="generated-public-key-secret",
        ), mock.patch.object(
            DEMO_USER.MelodeeEncryption,
            "encrypt",
            return_value="encrypted-password-secret",
        ), contextlib.redirect_stdout(
            output
        ), contextlib.redirect_stderr(
            output
        ):
            result = DEMO_USER.create_demo_user(
                {"password": secret},
                "private-encryption-key-secret",
            )

        rendered_output = output.getvalue()
        self.assertFalse(result)
        self.assertIn("Failed to create or update", rendered_output)
        self.assertNotIn(secret, rendered_output)

    def test_encryption_failure_does_not_echo_the_raw_exception(self):
        """An encryption exception containing a key is reported generically."""
        secret = "private-encryption-key-secret"
        output = io.StringIO()

        with mock.patch.object(
            DEMO_USER.MelodeeEncryption,
            "generate_public_key",
            return_value="generated-public-key-secret",
        ), mock.patch.object(
            DEMO_USER.MelodeeEncryption,
            "encrypt",
            side_effect=RuntimeError(f"invalid key {secret}"),
        ), contextlib.redirect_stdout(
            output
        ), contextlib.redirect_stderr(
            output
        ):
            result = DEMO_USER.create_demo_user(
                {"password": "database-password-secret"},
                secret,
            )

        rendered_output = output.getvalue()
        self.assertFalse(result)
        self.assertIn("Unable to encrypt demo credentials", rendered_output)
        self.assertNotIn(secret, rendered_output)
        DEMO_USER.psycopg2.connect.assert_not_called()


if __name__ == "__main__":
    unittest.main()
