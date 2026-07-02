"""Detecção leve de linguagens do projeto — decorativo, não precisa de precisão de linguist.

Duas fontes, manifesto primeiro (barato e confiável), extensão como complemento
até preencher o teto de exibição.
"""

import os
from collections import Counter
from pathlib import Path

_LIMIT = 3
_MAX_FILES_SCANNED = 3000
_MAX_DEPTH = 4
_SKIP_DIRS = {
    "node_modules", ".git", ".next", ".venv", "venv", "vendor",
    "dist", "build", "__pycache__", ".cache", "target", ".pytest_cache",
}

_MANIFEST_MAP = [
    ("composer.json", "php"),
    ("pyproject.toml", "python"),
    ("requirements.txt", "python"),
    ("setup.py", "python"),
    ("Gemfile", "ruby"),
    ("go.mod", "go"),
    ("Cargo.toml", "rust"),
    ("pom.xml", "java"),
    ("build.gradle", "java"),
]

_EXT_MAP = {
    ".py": "python",
    ".js": "javascript",
    ".jsx": "javascript",
    ".mjs": "javascript",
    ".cjs": "javascript",
    ".ts": "typescript",
    ".tsx": "typescript",
    ".php": "php",
    ".go": "go",
    ".rs": "rust",
    ".rb": "ruby",
    ".java": "java",
    ".kt": "kotlin",
    ".c": "c",
    ".h": "c",
    ".cpp": "cpp",
    ".hpp": "cpp",
    ".cs": "csharp",
    ".sh": "shell",
    ".vue": "vue",
}


def detect_languages(path: Path, limit: int = _LIMIT) -> list[str]:
    found: list[str] = []

    def add(lang: str) -> None:
        if lang not in found:
            found.append(lang)

    if (path / "tsconfig.json").exists():
        add("typescript")
    elif (path / "package.json").exists():
        add("javascript")

    for filename, lang in _MANIFEST_MAP:
        if (path / filename).exists():
            add(lang)

    if len(found) >= limit:
        return found[:limit]

    for lang, _count in _scan_extensions(path).most_common():
        if lang in found:
            continue
        add(lang)
        if len(found) >= limit:
            break

    return found


def _scan_extensions(path: Path) -> Counter:
    counts: Counter = Counter()
    scanned = 0
    base_depth = len(path.parts)

    for root, dirs, files in os.walk(path):
        dirs[:] = [d for d in dirs if d not in _SKIP_DIRS and not d.startswith(".")]
        if len(Path(root).parts) - base_depth >= _MAX_DEPTH:
            dirs[:] = []

        for filename in files:
            if scanned >= _MAX_FILES_SCANNED:
                return counts
            scanned += 1
            lang = _EXT_MAP.get(Path(filename).suffix.lower())
            if lang:
                counts[lang] += 1

    return counts
