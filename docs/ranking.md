# Ranking and feedback

Signal Radar classifies and scores every accepted article before it is inserted into PostgreSQL. The ingestion path does not call an LLM, so the same ranking profile and article input always produce the same assessment.

## Topics

The current multi-label topics are:

- `ArtificialIntelligence`
- `GameIndustry`
- `GameDevelopment`
- `DeveloperTools`
- `Research`
- `Business`
- `Security`
- `Other`

An article may match several topics. The matching rule with the highest priority becomes the primary topic.

## Base score

Every component is bounded to 0–100.

```text
BaseScore = SourceTrust × 0.30
          + TopicInterest × 0.30
          + PracticalImpact × 0.20
          + Freshness × 0.20
```

`SourceTrust` uses the strongest matching source or host rule. `TopicInterest` uses the strongest interest value among the matched topics. `PracticalImpact` starts from a profile base and applies each matching impact rule once. `Freshness` is based on the time between publication and collection.

The score components, topic mask, primary topic, final base score, and ranking-profile version are persisted on the article row. This makes ranking decisions inspectable and allows future re-scoring without losing the original result.

## Feedback adjustment

Explicit feedback is stored once per article and actor. A later choice from the same actor replaces the previous choice.

```text
Interested      +3
NotInterested  -2
Hidden         -3
```

The ranking query applies the aggregate feedback weight without mutating the deterministic base score.

```text
EffectiveScore = clamp(BaseScore + Sum(FeedbackWeight) × 5, 0, 100)
```

Discord interaction commands are not connected yet. The PostgreSQL store and ranking reader are ready for those commands in the next milestone.

## Personal profile

Without `RANKING_PROFILE_PATH`, the worker uses the built-in `default-v1` profile. To customize it:

```bash
cp config/ranking-profile.example.json config/ranking-profile.json
```

Then set:

```text
RANKING_PROFILE_PATH=config/ranking-profile.json
```

The profile file is limited to 1 MiB and is validated completely during startup. Invalid topic names, score ranges, empty keywords, or missing rule arrays stop startup instead of silently changing ranking behavior.

## Not implemented yet

- title-similarity novelty scoring
- cross-source event clustering
- mention-growth momentum scoring
- automatic learning of profile values
- Discord buttons and slash commands for feedback and ranked retrieval
