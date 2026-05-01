#!/usr/bin/env bash
set -euo pipefail

usage() {
    echo "Usage: $0 <resource-key> <english-text>" >&2
    echo "Example: $0 \"Navigation.Scripts\" \"Scripts\"" >&2
}

if [[ $# -ne 2 ]]; then
    usage
    exit 2
fi

key="$1"
english_text="$2"

resource_dir="src/Melodee.Blazor/Resources"
base_file="${resource_dir}/SharedResources.resx"

languages=(
    "de-DE" "es-ES" "fr-FR" "it-IT" "ja-JP" "pt-BR" "ru-RU" "zh-CN" "ar-SA"
    "nl-NL" "pl-PL" "tr-TR" "id-ID" "ko-KR" "vi-VN" "fa-IR" "uk-UA" "cs-CZ"
    "sv-SE"
)

if [[ ! -f "${base_file}" ]]; then
    echo "Error: base resource file not found: ${base_file}" >&2
    exit 1
fi

add_key_to_file() {
    local file="$1"
    local value="$2"

    python3 - "$file" "$key" "$value" <<'PY'
import html
import sys
from pathlib import Path

path = Path(sys.argv[1])
key = sys.argv[2]
value = sys.argv[3]

text = path.read_text(encoding="utf-8")
needle = f'<data name="{key}"'
if needle in text:
    sys.exit(0)

if "</root>" not in text:
    print(f"Error: missing </root> in {path}", file=sys.stderr)
    sys.exit(1)

escaped_value = html.escape(value, quote=False)
block = (
    f'\n  <data name="{key}" xml:space="preserve">\n'
    f"    <value>{escaped_value}</value>\n"
    f"  </data>\n"
)

text = text.replace("</root>", f"{block}</root>", 1)
path.write_text(text, encoding="utf-8")
PY
}

add_key_to_file "${base_file}" "${english_text}"

for lang in "${languages[@]}"; do
    lang_file="${resource_dir}/SharedResources.${lang}.resx"
    if [[ ! -f "${lang_file}" ]]; then
        echo "Error: language resource file not found: ${lang_file}" >&2
        exit 1
    fi
    add_key_to_file "${lang_file}" "[NEEDS TRANSLATION] ${english_text}"
done

echo "Added key '${key}' to base + ${#languages[@]} translation file(s)."

