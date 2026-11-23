#!/usr/bin/env bash
# Richtet das GitHub-Repository gemäß ADR 0007 ein (idempotent, kann beliebig oft laufen).
# Voraussetzung: gh CLI, angemeldet mit `gh auth login` (Admin-Rechte auf dem Repo).
# Aufruf: scripts/github-einrichten.sh [owner/repo]
set -euo pipefail

REPO="${1:-PfeilImKnie/Rezeptverwaltung}"
BRANCH="main"

echo "==> Repository-Einstellungen ($REPO)"
# Nur Squash-Merges: ein Commit pro PR, PR-Titel (Conventional Commit) wird Commit-Titel.
gh api -X PATCH "repos/$REPO" --silent \
  -F allow_squash_merge=true \
  -F allow_merge_commit=false \
  -F allow_rebase_merge=false \
  -F allow_auto_merge=true \
  -F delete_branch_on_merge=true \
  -f squash_merge_commit_title=PR_TITLE \
  -f squash_merge_commit_message=PR_BODY

echo "==> Dependabot-Sicherheitswarnungen und -Updates"
gh api -X PUT "repos/$REPO/vulnerability-alerts" --silent
gh api -X PUT "repos/$REPO/automated-security-fixes" --silent

echo "==> Branch-Schutz für $BRANCH"
# Statuschecks = Job-Namen aus .github/workflows/ci.yml. Bei Umbenennung hier anpassen!
# Kein Pflicht-Approval (Einzelentwickler); das Review erfolgt über /pr-review und wird vom pr-gate geprüft.
gh api -X PUT "repos/$REPO/branches/$BRANCH/protection" --silent --input - <<'JSON'
{
  "required_status_checks": {
    "strict": true,
    "contexts": [
      "Build, Format, Tests",
      "Mutation Testing (Domain)",
      "API-Vertrag (Schemathesis)",
      "PR-Gate (Test, Review, Doku, DoD)"
    ]
  },
  "enforce_admins": true,
  "required_pull_request_reviews": {
    "required_approving_review_count": 0,
    "dismiss_stale_reviews": true
  },
  "restrictions": null,
  "required_linear_history": true,
  "allow_force_pushes": false,
  "allow_deletions": false,
  "required_conversation_resolution": true
}
JSON

echo "==> Kontrolle"
gh api "repos/$REPO/branches/$BRANCH/protection" \
  --jq '{checks: .required_status_checks.contexts, strict: .required_status_checks.strict, admins: .enforce_admins.enabled, linear: .required_linear_history.enabled, force_push: .allow_force_pushes.enabled}'
echo "Fertig."
