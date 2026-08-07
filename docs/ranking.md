# Ranking and feedback

Signal Radar classifies and scores every accepted article before persistence. The ingestion path does not call an LLM, so the same ranking profile and normalized article input produce the same deterministic assessment.

The system deliberately keeps **base assessment**, **aggregate feedback**, and **actor-specific filtering** as separate concerns. This makes ranking inspectable and lets historical articles be reassessed without changing article identity.

## Topics

Current concrete topic slugs are:

```text
ai
game-industry
game-development
developer-tools
research
business
security
economy
markets
other
```

`all` is available as a query/digest filter but is not a concrete publication topic.

An article may match several topics. The highest-priority matching rule becomes the primary topic while the full topic mask is retained.

Economy and Markets were added as explicit topics so macroeconomic material and market/company-performance material do not have to collapse into broad Business classification.

## Deterministic base score

Every score component is bounded to 0–100.

```text
BaseScore = SourceTrust × 0.30
          + TopicInterest × 0.30
          + PracticalImpact × 0.20
          + Freshness × 0.20
```

- `SourceTrust` uses configured source/host trust rules.
- `TopicInterest` uses the strongest configured interest value among matched topics.
- `PracticalImpact` starts from the profile base and applies matching impact rules once.
- `Freshness` is derived from publication/collection timing rather than current wall-clock time during later reclassification.

The score components, topic mask, primary topic, final base score, and ranking-profile version are persisted with the article assessment. Ranking decisions therefore remain inspectable instead of disappearing into a prompt or transient model response.

## URL hostname handling

Canonical URLs remain available to source-trust logic, but hostname text is deliberately excluded from topic and practical-impact keyword matching.

For example, a market article hosted on `v.daum.net` must not become `developer-tools` merely because the hostname contains the substring `.net`. This behavior has dedicated regression coverage.

## Feedback adjustment

Explicit article feedback is actor-scoped. A later feedback choice from the same actor replaces the previous choice for that article.

```text
Interested      +3
NotInterested  -2
Hidden         -3
```

Aggregate feedback adjusts effective ranking without mutating the deterministic base score:

```text
EffectiveScore = clamp(BaseScore + Sum(FeedbackWeight) × 5, 0, 100)
```

`Hidden` also removes the article from later ranked results for that actor.

Discord article buttons are connected to this store:

- `관심` → Interested
- `별로` → NotInterested
- `숨김` → Hidden
- `저장` / `저장 해제` → the independent saved-article relation

Saved state is intentionally separate from feedback so saving an article does not implicitly alter its ranking score.

## Per-user source preferences

Source mutes are also actor-scoped and separate from the deterministic article assessment.

```text
/source-mute source:<exact source name>
/source-unmute source:<exact source name>
/source-muted
```

A muted source remains collected and can still appear in shared public topic publication. The preference only filters that actor's personal actor-aware reads such as ranked/search/digest workflows.

This keeps public Discord history consistent while still allowing each user to tune private reading results.

## Ranking profile

Without `RANKING_PROFILE_PATH`, the Worker uses the built-in default profile. To customize it:

```bash
cp config/ranking-profile.example.json config/ranking-profile.json
```

Then set:

```text
RANKING_PROFILE_PATH=config/ranking-profile.json
```

The profile file is bounded and validated completely during startup. Invalid topic names, score ranges, empty keywords, or malformed rule sets stop startup rather than silently changing ranking behavior.

Profile versions are persisted with article assessments so a stored score can be interpreted relative to the rules that produced it.

## Historical reclassification

Managers can re-evaluate stored articles with the currently loaded topic/ranking policy:

```text
/reclassify days:<1..3650>
```

Default: 30 days.

Reclassification:

- scans the requested recent window in bounded pages;
- preserves each article's original collection timestamp when recomputing freshness;
- updates assessment fields only when the result changed;
- batches PostgreSQL writes rather than issuing one round trip per article;
- does not rewrite canonical article identity;
- does not reset actor feedback, saved state, source preferences, or completed publication receipts.

That means ranking-policy fixes can be applied to historical data without intentionally redelivering already published articles.

## Public topic thresholds

Automatic topic publication uses the stored deterministic assessment as input. Runtime routes can define a per-topic minimum score:

```text
/route-set topic:<topic> channel:<channel> batch-minutes:<5..1440> min-score:<0..100>
```

The default minimum is `0`; source quality and route organization should normally be improved before using score thresholds as a broad suppression mechanism.

## Intentionally not implemented yet

The current baseline does not attempt to hide uncertainty behind an opaque learned scorer. The following remain explicit roadmap items:

- title-similarity novelty scoring;
- cross-source event clustering;
- source-volume baseline and momentum scoring;
- automatic learning of profile values;
- vector-assisted retrieval/ranking as a primary path.

See [Roadmap](roadmap.md) for planned work and [Discord interactions](discord-interactions.md) for the user-facing commands.
