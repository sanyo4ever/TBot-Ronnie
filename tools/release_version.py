"""Keep fork labels tied to an explicitly reviewed upstream release."""
import argparse
import json
import pathlib
import re


def derive_version(upstream, revision, requested=None, ci_run=None):
    number = r"(?:0|[1-9][0-9]*)"
    identifier = rf"(?:{number}|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)"
    match = re.fullmatch(rf"(?P<assembly>{number}\.{number}\.{number})(?:-(?P<prerelease>{identifier}(?:\.{identifier})*))?", upstream)
    if match is None:
        raise ValueError("Upstream version must be X.Y.Z with an optional SemVer prerelease")
    if not re.fullmatch(r"[a-z]", revision):
        raise ValueError("Fork revision must be one lowercase letter")
    if requested is not None and ci_run is not None:
        raise ValueError("Choose release or CI mode")
    label = upstream + revision
    if requested is not None and requested != label:
        raise ValueError(f"The reviewed release version is {label}; update fork-version.json before changing it")
    # A dotted fork identifier keeps an author's prerelease intact for NuGet.
    # The public label stays the author's exact version followed by our letter.
    version = upstream + ("." if match["prerelease"] else "-") + revision
    tag = label
    if ci_run is not None:
        if not re.fullmatch(r"[1-9][0-9]*", str(ci_run)):
            raise ValueError("CI run must be a positive integer")
        label += "-ci." + str(ci_run)
        version += ".ci." + str(ci_run)
        tag = ""
    return {"label": label, "version": version, "assembly": match["assembly"], "tag": tag}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=pathlib.Path, default=pathlib.Path(__file__).resolve().parents[1] / "fork-version.json")
    parser.add_argument("--requested")
    parser.add_argument("--ci-run")
    parser.add_argument("--github-output", type=pathlib.Path)
    args = parser.parse_args()
    try:
        config = json.loads(args.config.read_text(encoding="utf-8"))
        result = derive_version(config["upstream_release"], config["revision"], args.requested, args.ci_run)
    except (ValueError, KeyError, OSError, TypeError) as error:
        parser.error(str(error))
    if args.github_output:
        with args.github_output.open("a", encoding="utf-8", newline="\n") as output:
            for key, value in result.items():
                output.write(f"{key}={value}\n")
    print(json.dumps(result))


if __name__ == "__main__":
    main()
