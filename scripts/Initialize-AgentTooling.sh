#!/bin/sh
set -eu

script_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
repository_root=$(CDPATH= cd -- "$script_directory/.." && pwd -P)
canonical_directory="$repository_root/.agents"

if [ ! -d "$canonical_directory" ]; then
    echo "Canonical agent directory not found: $canonical_directory" >&2
    exit 1
fi

canonical_directory=$(CDPATH= cd -- "$canonical_directory" && pwd -P)

for alias_name in .codex .claude; do
    alias_path="$repository_root/$alias_name"

    if [ -L "$alias_path" ]; then
        link_target=$(readlink "$alias_path")
        case "$link_target" in
            /*) target_path="$link_target" ;;
            *) target_path="$repository_root/$link_target" ;;
        esac

        if [ ! -d "$target_path" ]; then
            echo "Refusing agent alias with a missing target: $alias_path" >&2
            exit 1
        fi

        resolved_target=$(CDPATH= cd -- "$target_path" && pwd -P)
        if [ "$resolved_target" != "$canonical_directory" ]; then
            echo "Refusing to replace agent alias that points elsewhere: $alias_path" >&2
            exit 1
        fi

        echo "$alias_name already points to .agents"
        continue
    fi

    if [ -e "$alias_path" ]; then
        echo "Refusing to replace existing non-link path: $alias_path" >&2
        exit 1
    fi

    ln -s .agents "$alias_path"
    echo "Created $alias_name -> .agents (SymbolicLink)"
done
