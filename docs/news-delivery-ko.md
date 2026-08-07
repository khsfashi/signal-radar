# Signal Radar 뉴스 운영 가이드

Signal Radar는 수집된 기사를 주제별 Discord 채널에 일정 시간 동안 모아 한 번에 게시할 수 있습니다. 영어 제목은 self-hosted LibreTranslate로 한국어 번역을 시도하고, 번역 실패 시 원문 제목으로 안전하게 폴백합니다. 성공한 번역은 PostgreSQL에 캐시되므로 같은 제목을 반복 번역하지 않습니다.

## 권장 채널 구성

처음에는 다음처럼 4~5개 채널만 두는 것을 권장합니다.

- `#ai-tech` — AI, 모델, 에이전트, 개발 도구
- `#game-industry` — 국내외 게임업계, 조직, 매출, 인수, 퍼블리싱
- `#game-dev` — Unreal, Unity, Godot, 렌더링, 개발 기술
- `#economy` — 금리, 물가, 환율, 거시경제와 한국은행 1차 자료
- `#markets` — 코스피·코스닥·미국 증시·기업 실적 등 시장 기사

채널을 너무 잘게 나누면 기사 수가 적을 때 피드가 비어 보일 수 있습니다. 실제 유입량을 본 뒤 분리하는 편이 좋습니다.

## 최초 실행

`docker-compose.yml`에는 LibreTranslate sidecar가 포함됩니다. 운영 Worker는 기본적으로 `http://libretranslate:5000/`를 사용합니다.

```powershell
docker compose up -d --build
docker compose ps
docker compose logs --tail=200 worker
docker compose logs --tail=100 libretranslate
```

LibreTranslate가 사용할 영문→한국어 모델은 최초 시작 시 준비 시간이 필요할 수 있습니다. 번역 서비스가 아직 준비되지 않았거나 요청이 실패해도 기사 발행 자체는 실패하지 않고 원문 제목을 사용합니다.

`docker compose down -v`는 PostgreSQL 데이터와 번역 캐시를 포함한 영속 데이터를 삭제할 수 있으므로 의도한 초기화가 아니면 사용하지 마세요.

## Discord에서 Feed 관리

서버의 `Administrator` 또는 `Manage Guild` 권한이 있는 사용자는 `.env`를 편집하지 않고 RSS/Atom Feed를 관리할 수 있습니다.

```text
/feed-add name:<고유 이름> url:<RSS/Atom URL> minutes:<수집 주기>
/feed-list
/feed-enable name:<이름>
/feed-disable name:<이름>
```

예시:

```text
/feed-add name:my-game-news url:https://example.com/feed.xml minutes:20
```

Feed 추가와 활성화 상태는 PostgreSQL에 저장되므로 Worker 재시작 후에도 유지됩니다. 기존 `config/feed-sources.json`은 bootstrap 및 코드로 관리하는 기본 소스에 계속 사용할 수 있습니다.

## Discord에서 주제별 채널 라우팅

관리자는 주제별 목적 채널, 묶음 시간, 최소 점수를 런타임에 설정할 수 있습니다.

```text
/route-set topic:<주제> channel:<채널> batch-minutes:<5~1440> min-score:<0~100>
/route-list
/route-remove topic:<주제>
```

지원 주제:

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

예시:

```text
/route-set topic:game-industry channel:#game-industry batch-minutes:30 min-score:0
/route-set topic:economy channel:#economy batch-minutes:60 min-score:0
/route-set topic:markets channel:#markets batch-minutes:30 min-score:0
```

`min-score=0`이면 점수 때문에 기사를 숨기지 않습니다. 기사가 너무 많아질 때만 50~70 정도로 올리는 것을 권장합니다.

런타임 라우트는 PostgreSQL이 우선이며, 기존 `DISCORD_TOPIC_CHANNELS`는 bootstrap/레거시 설정으로만 남겨둘 수 있습니다.

## 배치 게시 동작

예를 들어 `batch-minutes=30`이면 새 기사 하나가 들어올 때마다 즉시 Discord에 게시하지 않습니다. 수집 후 30분 이상 지난 미게시 기사를 같은 주제별로 최대 `DISCORD_TOPIC_BATCH_SIZE`만큼 묶어 하나의 Discord 메시지 또는 Forum post로 게시합니다.

각 기사는 기존과 동일하게 `(article, channel, publication kind)` 단위 receipt와 lease를 유지합니다. 따라서 여러 기사가 하나의 Discord 메시지에 들어가도 재시작 및 재시도 시 이미 완료된 기사 중복 게시를 억제할 수 있습니다.

## 제목 번역

환경변수 기본값:

```env
TITLE_TRANSLATION_ENABLED=true
TITLE_TRANSLATION_ENDPOINT=http://localhost:5000/
TITLE_TRANSLATION_TIMEOUT_SECONDS=10
TITLE_TRANSLATION_HTTP_MAX_CONNECTIONS_PER_SERVER=2
```

Docker Compose에서는 Worker endpoint를 자동으로 `http://libretranslate:5000/`로 덮어씁니다.

동작 순서:

1. 제목에 한글이 이미 포함되어 있으면 번역하지 않습니다.
2. 원문 제목의 SHA-256 hash로 PostgreSQL cache를 조회합니다.
3. cache miss일 때만 LibreTranslate를 호출합니다.
4. 성공 결과를 cache에 기록합니다.
5. HTTP 오류, timeout, 번역 결과 오류 시 원문 제목을 그대로 사용합니다.

번역은 기사 수집·정규화·분류·랭킹의 필수 경로가 아닙니다. 번역 서비스 장애가 뉴스 수집을 막지 않습니다.

## 개인별 보기 싫은 소스

공개 Discord 채널의 과거 메시지를 사용자마다 다르게 숨길 수는 없습니다. 대신 Signal Radar의 actor-aware 개인 조회에 소스 선호를 적용합니다.

```text
/source-mute source:<정확한 source 이름>
/source-unmute source:<정확한 source 이름>
/source-muted
```

차단한 source는 해당 사용자의 `/top`, `/search`, `/digest` 등 actor-aware 조회에서 제외됩니다. 다른 사용자의 결과와 공개 뉴스 채널에는 영향을 주지 않습니다.

## Starter source pack

`config/feed-sources.starter.json`에는 다음 계층을 섞어 두었습니다.

- 1차/공식: OpenAI, Unreal Engine, Godot, GitHub, 한국은행
- 전문 매체: Game Developer, GamesIndustry.biz, 게임동아
- 국내 게임업계 탐색: Google News의 `게임업계` 검색 RSS
- 서정근/MTN 추적: Google News의 `서정근 MTN 게임` 검색 RSS
- 국내 증시 탐색: Google News의 코스피·코스닥·주가 검색 RSS

서정근 기자 기사처럼 특정 기자의 양질 기사만 안정적으로 추적할 공식 기자 전용 RSS를 찾기 어려운 경우 검색 RSS를 발견용 fallback으로 사용합니다. 링크의 최종 원문 매체와 기사 제목은 그대로 저장되며, broad discovery 소스는 공식/전문 소스보다 낮은 trust로 운용하는 것을 권장합니다.

## 좋은 기사 유입을 유지하는 방법

Signal Radar의 소스는 다음 순서로 운영하는 편이 안정적입니다.

1. 공식 발표와 1차 자료를 가장 높은 trust로 둡니다.
2. 게임/개발 전문 매체를 상시 수집합니다.
3. 좋은 기자·주제는 검색 RSS로 보완합니다.
4. Hacker News와 broad Google News query는 새로운 소스 발견용으로 사용합니다.
5. 필요 없는 매체는 개인 `/source-mute` 또는 관리자 `/feed-disable`로 정리합니다.
6. 기사량이 늘어나면 `min-score`를 높이기 전에 채널 분리와 source 정리를 먼저 합니다.

이 구조에서는 소스 추가가 코드 배포와 분리되어 있으므로 운영 중 새 RSS를 발견하면 `/feed-add`만으로 바로 실험할 수 있습니다.
