# Security Policy

## Supported Versions

MapLibre Unity is pre-1.0. Security fixes ship on the latest tagged
release; older versions are not patched. Always upgrade to the most
recent `0.x.y` tag when a fix is announced.

| Version | Supported |
|---------|-----------|
| Latest `0.x.y` release | ✅ |
| Any earlier release | ❌ |

## Reporting a Vulnerability

**Please do not file public GitHub issues for security problems.** Use
GitHub's private vulnerability reporting so we can investigate before
the details become public.

### How to report

Go to the
[Security tab](https://github.com/KazukiKuriyama/maplibre-unity/security/advisories/new)
of the repository and click **Report a vulnerability**. This opens a
private advisory only the maintainer can read.

If you cannot use GitHub Security Advisories (for example because you
do not have a GitHub account), open a minimal public issue asking the
maintainer to contact you privately — do **not** include vulnerability
details in that issue.

## What to Include

A useful report contains:

- Affected version (commit hash or release tag).
- Reproduction steps or a minimal sample project.
- Impact assessment (e.g. tile-server SSRF, malicious style.json
  causing remote code execution, denial-of-service via crafted vector
  tile).
- Whether the issue is already public or known to other parties.

## Response Timeline

| Stage | Target |
|-------|--------|
| Acknowledge receipt | within 7 days |
| Initial assessment | within 14 days |
| Fix or mitigation | depends on severity; coordinated with reporter |
| Public advisory | after a fix is released, with reporter credited |

This is a single-maintainer project, so timelines are best-effort and
may stretch around major release work or holidays.

## Scope

In scope:

- Code under `Packages/com.kazukikuriyama.maplibre-unity/Runtime/` and
  `Packages/com.kazukikuriyama.maplibre-unity/Editor/`.
- Default style / tile loading pipeline (network request handling,
  archive parsing for PMTiles / MBTiles, sprite + glyph fetching).
- Sample scenes only when they demonstrate a library-level flaw.

Out of scope:

- Vulnerabilities in upstream Unity, URP, TextMeshPro, or
  Newtonsoft.Json — please report those to their respective vendors.
- Issues that require a malicious local actor with write access to the
  project files.
- Third-party tile servers referenced in samples (OpenStreetMap,
  MapLibre demo tiles, etc.).

## Disclosure Policy

We follow a coordinated-disclosure model. Once a fix is released, the
advisory is published on GitHub Security Advisories with credit to the
reporter (unless they prefer to remain anonymous).
