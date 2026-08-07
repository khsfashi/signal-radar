# Security Policy

## Supported version

Security fixes are applied to the current `main` branch. Signal Radar does not currently maintain parallel supported release branches.

## Reporting a vulnerability

Please do not open a public issue for suspected vulnerabilities, leaked credentials, private Discord identifiers, or other sensitive operational information.

If GitHub private vulnerability reporting is available for this repository, use the repository's **Security → Report a vulnerability** flow. Otherwise, contact the maintainer through a private channel listed on the maintainer's GitHub profile.

Include enough detail to reproduce and assess the issue, but do not include unrelated secrets or personal data.

## Secret handling

Real deployment secrets and personal runtime configuration must stay outside Git:

- `.env` and `.env.*`
- Discord bot tokens and IDs used by a real deployment
- OpenAI, Gemini, and GitHub API keys
- database passwords and production connection strings
- `config/feed-sources.json`
- `config/github-repositories.json`
- `config/ranking-profile.json`

If a real credential is committed, treat it as compromised: revoke or rotate it first, then remove it from reachable history where appropriate.
