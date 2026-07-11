#!/usr/bin/env python3
"""
Export GitHub Code Scanning alerts + SARIF to a single JSON inside a ZIP.

Why this shape?
- Alerts are per-rule findings; SARIF is per-analysis/run.
- So we export all alerts, then download SARIF for the analyses that produced them.

Docs (GitHub REST API):
- List alerts endpoint includes most_recent_instance with ref/analysis_key/category/commit_sha, etc. :contentReference[oaicite:2]{index=2}
- List analyses returns analysis ids + matching metadata. :contentReference[oaicite:3]{index=3}
- Get analysis supports custom media type application/sarif+json to return SARIF subset. :contentReference[oaicite:4]{index=4}
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import sys
import time
import zipfile
from dataclasses import dataclass
from typing import Any, Dict, Iterable, List, Optional, Tuple
from urllib.parse import urljoin

import requests

MAX_LINK_HEADER_LENGTH = 16_384
MAX_LINK_HEADER_ENTRIES = 32
MAX_RELATIONS_PER_LINK = 8


def _split_link_header_values(link_header: str) -> List[str]:
    """Split a bounded Link header at commas outside URIs and quotes."""
    values: List[str] = []
    value_start = 0
    inside_uri = False
    inside_quotes = False
    escaped = False

    for index, character in enumerate(link_header):
        if escaped:
            escaped = False
            continue

        if inside_quotes and character == "\\":
            escaped = True
            continue

        if not inside_quotes:
            if character == "<":
                inside_uri = True
            elif character == ">":
                inside_uri = False

        if not inside_uri and character == '"':
            inside_quotes = not inside_quotes
        elif not inside_uri and not inside_quotes and character == ",":
            values.append(link_header[value_start:index])
            if len(values) >= MAX_LINK_HEADER_ENTRIES:
                return values
            value_start = index + 1

    if len(values) < MAX_LINK_HEADER_ENTRIES:
        values.append(link_header[value_start:])

    return values


def _parse_link_parameter_value(
    link_value: str,
    position: int,
) -> Tuple[str, int]:
    """Read a quoted or unquoted Link parameter value once, left to right."""
    if position >= len(link_value):
        return "", position

    if link_value[position] != '"':
        value_start = position
        while position < len(link_value) and link_value[position] not in "; \t":
            position += 1
        return link_value[value_start:position], position

    position += 1
    characters: List[str] = []
    while position < len(link_value):
        character = link_value[position]
        position += 1
        if character == '"':
            break
        if character == "\\" and position < len(link_value):
            character = link_value[position]
            position += 1
        characters.append(character)

    return "".join(characters), position


def _parse_link_value(link_value: str) -> Tuple[str, List[str]]:
    """Extract a URL and relation names from one RFC 8288 Link value."""
    link_value = link_value.strip()
    if not link_value.startswith("<"):
        return "", []

    url_end = link_value.find(">", 1)
    if url_end == -1:
        return "", []

    url = link_value[1:url_end].strip()
    relation_value = ""
    position = url_end + 1

    while position < len(link_value):
        while position < len(link_value) and link_value[position] in " \t":
            position += 1
        if position >= len(link_value):
            break
        if link_value[position] != ";":
            return "", []

        position += 1
        while position < len(link_value) and link_value[position] in " \t":
            position += 1

        name_start = position
        while position < len(link_value) and link_value[position] not in "=; \t":
            position += 1
        parameter_name = link_value[name_start:position].casefold()

        while position < len(link_value) and link_value[position] in " \t":
            position += 1
        if position >= len(link_value) or link_value[position] != "=":
            while position < len(link_value) and link_value[position] != ";":
                position += 1
            continue

        position += 1
        while position < len(link_value) and link_value[position] in " \t":
            position += 1
        parameter_value, position = _parse_link_parameter_value(
            link_value,
            position,
        )
        if parameter_name == "rel" and not relation_value:
            relation_value = parameter_value

        while position < len(link_value) and link_value[position] in " \t":
            position += 1
        if position < len(link_value) and link_value[position] != ";":
            return "", []

    relations = relation_value.split()[:MAX_RELATIONS_PER_LINK]
    return url, relations


def parse_link_header(link_header: str) -> Dict[str, str]:
    """
    Parse GitHub Link header into {rel: url}.
    Example:
      <https://api.github.com/...&page=2>; rel="next", <...&page=4>; rel="last"
    """
    links: Dict[str, str] = {}
    if not link_header or len(link_header) > MAX_LINK_HEADER_LENGTH:
        return links

    for link_value in _split_link_header_values(link_header):
        url, relations = _parse_link_value(link_value)
        if not url:
            continue
        for relation in relations:
            links[relation] = url

    return links


@dataclass(frozen=True)
class AlertKey:
    ref: str
    analysis_key: str
    category: str
    commit_sha: str
    tool_name: str


@dataclass
class GitHubClient:
    api_url: str
    token: str
    api_version: str = "2022-11-28"
    timeout_seconds: int = 60

    def __post_init__(self) -> None:
        self.api_url = self.api_url.rstrip("/") + "/"

        self.session = requests.Session()
        self.session.headers.update(
            {
                "Authorization": f"Bearer {self.token}",
                # Recommended default media type for REST API responses:
                "Accept": "application/vnd.github+json",
                "X-GitHub-Api-Version": self.api_version,
                "User-Agent": "code-scanning-exporter/1.0",
            }
        )

    def request_json(
        self,
        method: str,
        path_or_url: str,
        *,
        params: Optional[Dict[str, Any]] = None,
        headers: Optional[Dict[str, str]] = None,
        expected_status: Iterable[int] = (200,),
        stream: bool = False,
    ) -> Tuple[Any, requests.Response]:
        url = path_or_url
        if not url.startswith("http"):
            url = urljoin(self.api_url, path_or_url.lstrip("/"))

        req_headers = dict(self.session.headers)
        if headers:
            req_headers.update(headers)

        for attempt in range(1, 6):
            resp = self.session.request(
                method=method,
                url=url,
                params=params,
                headers=req_headers,
                timeout=self.timeout_seconds,
                stream=stream,
            )

            # Handle rate limiting politely
            if resp.status_code in (429, 403):
                # GitHub often uses 403 for rate limiting; check headers
                remaining = resp.headers.get("X-RateLimit-Remaining")
                reset = resp.headers.get("X-RateLimit-Reset")
                if remaining == "0" and reset:
                    try:
                        reset_epoch = int(reset)
                        sleep_for = max(0, reset_epoch - int(time.time())) + 2
                        print(
                            f"[rate-limit] Sleeping {sleep_for}s until reset...",
                            file=sys.stderr,
                        )
                        time.sleep(sleep_for)
                        continue
                    except ValueError:
                        pass

            if resp.status_code in expected_status:
                if stream:
                    return resp, resp
                return resp.json(), resp

            # Retry some transient failures
            if resp.status_code in (500, 502, 503, 504):
                backoff = min(2**attempt, 30)
                print(
                    f"[retry] {resp.status_code} from {url} (attempt {attempt}/5), sleeping {backoff}s...",
                    file=sys.stderr,
                )
                time.sleep(backoff)
                continue

            # Not retriable or final failure
            text = resp.text[:2000]
            raise RuntimeError(
                f"GitHub API error {resp.status_code} calling {url}\n"
                f"Response (first 2000 chars):\n{text}"
            )

        raise RuntimeError(f"Failed after retries calling {url}")

    def paginate(
        self, path: str, *, params: Optional[Dict[str, Any]] = None
    ) -> List[Any]:
        """
        Fetch all pages for an endpoint that uses Link headers.
        """
        items: List[Any] = []
        url = urljoin(self.api_url, path.lstrip("/"))
        local_params = dict(params or {})

        while True:
            data, resp = self.request_json(
                "GET", url, params=local_params, expected_status=(200,)
            )
            if isinstance(data, list):
                items.extend(data)
            else:
                # Some endpoints return objects; include whole object as one item.
                items.append(data)

            links = parse_link_header(resp.headers.get("Link", ""))
            next_url = links.get("next")
            if not next_url:
                break

            url = next_url
            # Once we follow next_url, params are already embedded in the URL.
            local_params = {}

        return items


def extract_alert_key(alert: Dict[str, Any]) -> Optional[AlertKey]:
    inst = alert.get("most_recent_instance") or {}
    tool = alert.get("tool") or {}
    ref = inst.get("ref") or ""
    analysis_key = inst.get("analysis_key") or ""
    category = inst.get("category") or ""
    commit_sha = inst.get("commit_sha") or ""
    tool_name = tool.get("name") or ""
    if not (ref and analysis_key and category and commit_sha and tool_name):
        return None
    return AlertKey(
        ref=ref,
        analysis_key=analysis_key,
        category=category,
        commit_sha=commit_sha,
        tool_name=tool_name,
    )


def analysis_matches_key(analysis: Dict[str, Any], key: AlertKey) -> bool:
    tool = analysis.get("tool") or {}
    return (
        analysis.get("ref") == key.ref
        and analysis.get("analysis_key") == key.analysis_key
        and analysis.get("category") == key.category
        and analysis.get("commit_sha") == key.commit_sha
        and tool.get("name") == key.tool_name
    )


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Download all GitHub code scanning alerts and SARIF for matching analyses; output a single JSON inside a ZIP."
    )
    parser.add_argument("owner", help="Repo owner/org, e.g. melodee-project")
    parser.add_argument("repo", help="Repo name, e.g. melodee")
    parser.add_argument(
        "--token",
        default=os.getenv("GH_TOKEN") or os.getenv("GITHUB_TOKEN"),
        help="GitHub token (or set GH_TOKEN/GITHUB_TOKEN env var). Needs code scanning read perms. :contentReference[oaicite:5]{index=5}",
    )
    parser.add_argument(
        "--api-url",
        default="https://api.github.com",
        help="GitHub API base URL (useful for GHES). Default: https://api.github.com",
    )
    parser.add_argument(
        "--out",
        default=None,
        help="Output zip file path. Default: <owner>_<repo>_code_scanning_export_<timestamp>.zip",
    )
    parser.add_argument(
        "--pretty",
        action="store_true",
        help="Pretty-print JSON (larger file). Default is compact JSON to reduce size.",
    )
    parser.add_argument(
        "--include-recent-analyses",
        type=int,
        default=0,
        help=(
            "Also download SARIF for the N most recent analyses, even if not matched via alerts. "
            "Useful if matching misses some alerts. Default: 0"
        ),
    )

    args = parser.parse_args()

    if not args.token:
        print(
            "ERROR: No token provided. Use --token or set GH_TOKEN / GITHUB_TOKEN.",
            file=sys.stderr,
        )
        return 2

    api_url = args.api_url.rstrip("/") + "/"
    client = GitHubClient(api_url=api_url, token=args.token)

    owner, repo = args.owner, args.repo

    # 1) Download alerts across states, de-dupe by alert number.
    # States supported: open/closed/dismissed/fixed. :contentReference[oaicite:6]{index=6}
    states = ["open", "dismissed", "fixed", "closed"]
    alerts_by_number: Dict[int, Dict[str, Any]] = {}

    for st in states:
        print(f"[alerts] Fetching state={st} ...", file=sys.stderr)
        path = f"/repos/{owner}/{repo}/code-scanning/alerts"
        # per_page max 100 :contentReference[oaicite:7]{index=7}
        items = client.paginate(path, params={"per_page": 100, "state": st})
        for a in items:
            num = a.get("number")
            if isinstance(num, int):
                alerts_by_number[num] = a

    alerts = sorted(alerts_by_number.values(), key=lambda a: a.get("number", 0))
    print(f"[alerts] Total unique alerts: {len(alerts)}", file=sys.stderr)

    # 2) Build alert->analysis matching keys from most_recent_instance.
    alert_keys: List[AlertKey] = []
    for a in alerts:
        k = extract_alert_key(a)
        if k:
            alert_keys.append(k)
    key_set = set(alert_keys)

    print(
        f"[match] Alerts with usable most_recent_instance keys: {len(key_set)}",
        file=sys.stderr,
    )

    # 3) Download analyses and match to those keys.
    # Analyses list returns id/ref/analysis_key/category/commit_sha/tool/sarif_id, etc. :contentReference[oaicite:8]{index=8}
    print("[analyses] Fetching analyses list ...", file=sys.stderr)
    analyses_path = f"/repos/{owner}/{repo}/code-scanning/analyses"
    analyses_all = client.paginate(analyses_path, params={"per_page": 100})
    print(f"[analyses] Total analyses returned: {len(analyses_all)}", file=sys.stderr)

    matched_analyses: List[Dict[str, Any]] = []
    matched_ids: List[int] = []

    # Strict match (includes commit_sha)
    for an in analyses_all:
        try:
            an_id = int(an.get("id"))
        except Exception:
            continue
        for k in key_set:
            if analysis_matches_key(an, k):
                matched_analyses.append(an)
                matched_ids.append(an_id)
                break

    # Optionally include N most recent analyses as a safety net
    if args.include_recent_analyses > 0:
        recent = analyses_all[: args.include_recent_analyses]
        for an in recent:
            try:
                an_id = int(an.get("id"))
            except Exception:
                continue
            if an_id not in matched_ids:
                matched_analyses.append(an)
                matched_ids.append(an_id)

    # Unique IDs, preserve order
    seen: set[int] = set()
    analysis_ids: List[int] = []
    for i in matched_ids:
        if i not in seen:
            seen.add(i)
            analysis_ids.append(i)

    print(
        f"[analyses] Analyses selected for SARIF download: {len(analysis_ids)}",
        file=sys.stderr,
    )

    # 4) Download SARIF for each selected analysis id using custom media type application/sarif+json. :contentReference[oaicite:9]{index=9}
    sarif_by_analysis_id: Dict[str, Any] = {}
    for idx, analysis_id in enumerate(analysis_ids, start=1):
        print(
            f"[sarif] ({idx}/{len(analysis_ids)}) Downloading analysis_id={analysis_id} ...",
            file=sys.stderr,
        )
        path = f"/repos/{owner}/{repo}/code-scanning/analyses/{analysis_id}"
        sarif_json, _ = client.request_json(
            "GET",
            path,
            headers={"Accept": "application/sarif+json"},
            expected_status=(200,),
        )
        sarif_by_analysis_id[str(analysis_id)] = sarif_json

    exported_at = dt.datetime.now(dt.timezone.utc).isoformat()

    combined = {
        "schema_version": 1,
        "repo": f"{owner}/{repo}",
        "api_url": args.api_url,
        "exported_at": exported_at,
        "counts": {
            "alerts_unique": len(alerts),
            "analyses_total_returned": len(analyses_all),
            "analyses_sarif_downloaded": len(sarif_by_analysis_id),
        },
        # Raw alerts from API
        "alerts": alerts,
        # Include analysis metadata for analyses we downloaded SARIF for (plus optional recent adds)
        "analyses_selected": matched_analyses,
        # SARIF payloads keyed by analysis id (string)
        "sarif_by_analysis_id": sarif_by_analysis_id,
    }

    # 5) Write zip with a single JSON file.
    ts = dt.datetime.now().strftime("%Y%m%d_%H%M%S")
    out_zip = args.out or f"{owner}_{repo}_code_scanning_export_{ts}.zip"
    json_name = "code_scanning_export.json"

    if args.pretty:
        json_bytes = json.dumps(combined, indent=2, ensure_ascii=False).encode("utf-8")
    else:
        json_bytes = json.dumps(
            combined, separators=(",", ":"), ensure_ascii=False
        ).encode("utf-8")

    with zipfile.ZipFile(
        out_zip, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9
    ) as zf:
        zf.writestr(json_name, json_bytes)

    print(f"[done] Wrote {out_zip} containing {json_name}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
