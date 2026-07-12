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


class FakeResponse:
    """Provide the requests response behavior used by exporter unit tests."""

    def __init__(
        self,
        payload=None,
        *,
        status_code=200,
        headers=None,
        url="",
    ):
        """Initialize a deterministic response without performing network I/O."""
        self.payload = payload
        self.status_code = status_code
        self.headers = dict(headers or {})
        self.url = url
        self.text = ""
        self.closed = False

    def json(self):
        """Return the configured JSON payload."""
        return self.payload

    def close(self):
        """Record that request cleanup closed the response."""
        self.closed = True


class FakeSession:
    """Capture outbound requests and return queued fake responses."""

    def __init__(self, responses):
        """Store responses in their expected request order."""
        self.headers = {}
        self.responses = list(responses)
        self.calls = []

    def request(self, **kwargs):
        """Capture one request and return the next response."""
        self.calls.append(kwargs)
        if not self.responses:
            raise AssertionError("Unexpected outbound request")
        response = self.responses.pop(0)
        if not response.url:
            response.url = kwargs["url"]
        return response


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


class GitHubClientOriginTests(unittest.TestCase):
    """Verify credentials remain on one validated GitHub API origin."""

    def create_client(self, responses, api_url="https://api.github.com"):
        """Create a client backed by a deterministic fake session."""
        session = FakeSession(responses)
        with mock.patch.object(
            CODE_SCANNING_EXPORTER.requests,
            "Session",
            return_value=session,
            create=True,
        ):
            client = CODE_SCANNING_EXPORTER.GitHubClient(
                api_url=api_url,
                token="test-bearer-token",
            )
        return client, session

    def test_paginate_follows_normal_same_origin_github_link(self):
        """Normal GitHub pagination follows a same-origin next link."""
        next_url = "https://api.github.com/repos/example/music/alerts?page=2"
        first = FakeResponse(
            [{"number": 1}],
            headers={"Link": f'<{next_url}>; rel="next"'},
        )
        second = FakeResponse([{"number": 2}])
        client, session = self.create_client([first, second])

        result = client.paginate(
            "/repos/example/music/alerts",
            params={"per_page": 1},
        )

        self.assertEqual([{"number": 1}, {"number": 2}], result)
        self.assertEqual(2, len(session.calls))
        self.assertEqual(next_url, session.calls[1]["url"])
        self.assertEqual({}, session.calls[1]["params"])
        for call in session.calls:
            self.assertFalse(call["allow_redirects"])
            self.assertEqual(
                "Bearer test-bearer-token",
                call["headers"]["Authorization"],
            )

    def test_paginate_rejects_cross_origin_link_before_request(self):
        """An attacker-controlled absolute next link cannot receive the token."""
        first = FakeResponse(
            [],
            headers={"Link": '<https://attacker.example/collect>; rel="next"'},
        )
        client, session = self.create_client([first])

        with self.assertRaisesRegex(ValueError, "changed origin"):
            client.paginate("/repos/example/music/alerts")

        self.assertTrue(first.closed)
        self.assertEqual(1, len(session.calls))
        self.assertEqual(
            "api.github.com",
            CODE_SCANNING_EXPORTER.urlsplit(session.calls[0]["url"]).hostname,
        )

    def test_paginate_rejects_protocol_relative_link(self):
        """Protocol-relative pagination is rejected instead of being reinterpreted."""
        first = FakeResponse(
            [],
            headers={"Link": '<//attacker.example/collect>; rel="next"'},
        )
        client, session = self.create_client([first])

        with self.assertRaisesRegex(ValueError, "Protocol-relative"):
            client.paginate("/repos/example/music/alerts")

        self.assertEqual(1, len(session.calls))

    def test_paginate_rejects_userinfo_and_host_confusion_links(self):
        """Userinfo and lookalike hosts cannot bypass the exact origin check."""
        malicious_links = (
            "https://api.github.com@attacker.example/collect",
            "https://api.github.com.attacker.example/collect",
        )

        for malicious_link in malicious_links:
            with self.subTest(link=malicious_link):
                first = FakeResponse(
                    [],
                    headers={"Link": f'<{malicious_link}>; rel="next"'},
                )
                client, session = self.create_client([first])

                with self.assertRaises(ValueError):
                    client.paginate("/repos/example/music/alerts")

                self.assertEqual(1, len(session.calls))

    def test_paginate_rejects_a_repeated_page_cycle(self):
        """A cyclic same-origin Link chain stops before issuing a third request."""
        first_url = "https://api.github.com/repos/example/music/alerts?page=1"
        second_url = "https://api.github.com/repos/example/music/alerts?page=2"
        first = FakeResponse(
            [{"number": 1}],
            headers={"Link": f'<{second_url}>; rel="next"'},
            url=first_url,
        )
        second = FakeResponse(
            [{"number": 2}],
            headers={"Link": f'<{first_url}>; rel="next"'},
            url=second_url,
        )
        client, session = self.create_client([first, second])

        with self.assertRaisesRegex(RuntimeError, "pagination cycle"):
            client.paginate(first_url)

        self.assertEqual(2, len(session.calls))
        self.assertTrue(first.closed)
        self.assertTrue(second.closed)

    def test_redirect_rejects_cross_origin_before_forwarding_token(self):
        """Manual redirect handling never forwards authorization cross-origin."""
        redirect = FakeResponse(
            status_code=302,
            headers={"Location": "https://attacker.example/collect"},
        )
        client, session = self.create_client([redirect])

        with self.assertRaisesRegex(ValueError, "changed origin"):
            client.request_json("GET", "/repos/example/music")

        self.assertTrue(redirect.closed)
        self.assertEqual(1, len(session.calls))
        self.assertFalse(session.calls[0]["allow_redirects"])
        self.assertEqual(
            "Bearer test-bearer-token",
            session.calls[0]["headers"]["Authorization"],
        )

    def test_redirect_follows_bounded_same_origin_location(self):
        """A normal same-origin redirect is followed with redirects disabled."""
        redirect = FakeResponse(
            status_code=302,
            headers={"Location": "/repositories/42"},
            url="https://api.github.com/repos/example/music",
        )
        final = FakeResponse({"id": 42})
        client, session = self.create_client([redirect, final])

        result, _ = client.request_json("GET", "/repos/example/music")

        self.assertEqual({"id": 42}, result)
        self.assertTrue(redirect.closed)
        self.assertEqual(
            "https://api.github.com/repositories/42",
            session.calls[1]["url"],
        )
        self.assertTrue(all(not call["allow_redirects"] for call in session.calls))

    def test_redirect_rejects_a_chain_above_the_limit(self):
        """A same-origin redirect chain cannot exceed the fixed hop limit."""
        redirects = [
            FakeResponse(
                status_code=302,
                headers={"Location": f"/redirect/{index + 1}"},
            )
            for index in range(CODE_SCANNING_EXPORTER.MAX_REDIRECTS + 1)
        ]
        client, session = self.create_client(redirects)

        with self.assertRaisesRegex(RuntimeError, "too many redirects"):
            client.request_json("GET", "/redirect/0")

        self.assertEqual(
            CODE_SCANNING_EXPORTER.MAX_REDIRECTS + 1,
            len(session.calls),
        )
        self.assertTrue(all(response.closed for response in redirects))
        self.assertTrue(all(not call["allow_redirects"] for call in session.calls))

    def test_transient_retry_closes_response_before_sleeping(self):
        """A transient response is closed before the bounded retry delay."""
        transient = FakeResponse(status_code=503)
        final = FakeResponse({"ok": True})
        client, _ = self.create_client([transient, final])

        with mock.patch.object(CODE_SCANNING_EXPORTER.time, "sleep") as sleep:
            sleep.side_effect = lambda _delay: self.assertTrue(transient.closed)
            result, _ = client.request_json("GET", "/repos/example/music")

        self.assertEqual({"ok": True}, result)
        self.assertTrue(transient.closed)
        sleep.assert_called_once_with(2)

    def test_rate_limit_retry_closes_response_before_sleeping(self):
        """A rate-limit response is closed before waiting for its reset."""
        rate_limited = FakeResponse(
            status_code=403,
            headers={
                "X-RateLimit-Remaining": "0",
                "X-RateLimit-Reset": "101",
            },
        )
        final = FakeResponse({"ok": True})
        client, _ = self.create_client([rate_limited, final])

        with mock.patch.object(
            CODE_SCANNING_EXPORTER.time,
            "time",
            return_value=100,
        ), mock.patch.object(CODE_SCANNING_EXPORTER.time, "sleep") as sleep:
            sleep.side_effect = lambda _delay: self.assertTrue(rate_limited.closed)
            result, _ = client.request_json("GET", "/repos/example/music")

        self.assertEqual({"ok": True}, result)
        self.assertTrue(rate_limited.closed)
        sleep.assert_called_once_with(3)

    def test_non_stream_json_response_closes_and_preserves_metadata(self):
        """Parsed JSON releases its connection without discarding metadata."""
        response = FakeResponse(
            {"ok": True},
            headers={"Link": '<https://api.github.com/page/2>; rel="next"'},
            url="https://api.github.com/page/1",
        )
        client, _ = self.create_client([response])

        result, returned_response = client.request_json("GET", "/page/1")

        self.assertEqual({"ok": True}, result)
        self.assertIs(response, returned_response)
        self.assertTrue(returned_response.closed)
        self.assertEqual(
            '<https://api.github.com/page/2>; rel="next"',
            returned_response.headers["Link"],
        )
        self.assertEqual("https://api.github.com/page/1", returned_response.url)

    def test_invalid_json_closes_response_before_propagating(self):
        """A JSON decoding failure cannot leave its response open."""
        response = FakeResponse()
        response.json = mock.Mock(side_effect=ValueError("invalid JSON"))
        client, _ = self.create_client([response])

        with self.assertRaisesRegex(ValueError, "invalid JSON"):
            client.request_json("GET", "/repos/example/music")

        self.assertTrue(response.closed)

    def test_terminal_error_closes_response_before_raising(self):
        """A non-retriable API error releases its response before raising."""
        response = FakeResponse(status_code=400)
        response.text = "invalid request"
        client, _ = self.create_client([response])

        with self.assertRaisesRegex(RuntimeError, "invalid request"):
            client.request_json("GET", "/repos/example/music")

        self.assertTrue(response.closed)

    def test_streamed_response_remains_caller_owned(self):
        """A streamed response remains open until its caller closes it."""
        response = FakeResponse({"ok": True})
        client, _ = self.create_client([response])

        result, returned_response = client.request_json(
            "GET",
            "/repos/example/music",
            stream=True,
        )

        self.assertIs(response, result)
        self.assertIs(response, returned_response)
        self.assertFalse(response.closed)
        response.close()

    def test_api_base_rejects_insecure_or_ambiguous_origins(self):
        """The configured token origin must be unambiguous HTTPS."""
        invalid_api_urls = (
            "http://api.github.com",
            "https://user@api.github.com",
            "https://api.github.com/#fragment",
            "https://api.github.com/#",
        )

        for api_url in invalid_api_urls:
            with self.subTest(api_url=api_url):
                with self.assertRaises(ValueError):
                    self.create_client([], api_url=api_url)

    def test_client_repr_omits_bearer_token(self):
        """Dataclass diagnostics cannot accidentally expose the API token."""
        client, _ = self.create_client([])

        self.assertNotIn("test-bearer-token", repr(client))

    def test_ghes_api_path_is_preserved_for_relative_endpoints(self):
        """A validated GHES API prefix remains part of relative requests."""
        client, session = self.create_client(
            [FakeResponse({"ok": True})],
            api_url="https://github.example.com/api/v3",
        )

        client.request_json("GET", "/repos/example/music")

        self.assertEqual(
            "https://github.example.com/api/v3/repos/example/music",
            session.calls[0]["url"],
        )

    def test_repository_coordinates_reject_path_delimiters(self):
        """CLI repository values cannot alter the API path or authority."""
        invalid_coordinates = (
            ("example/../../admin", "music"),
            ("example", "music/alerts"),
            ("api.github.com@attacker.example", "music"),
        )

        for owner, repo in invalid_coordinates:
            with self.subTest(owner=owner, repo=repo):
                with self.assertRaises(ValueError):
                    CODE_SCANNING_EXPORTER.validate_repository_coordinates(
                        owner,
                        repo,
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
