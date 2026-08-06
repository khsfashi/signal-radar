# Signal Radar 운영·복구 가이드

## 배포 전 확인

- `.env`가 Git에 포함되지 않았는지 확인한다.
- `SIGNAL_RADAR_ENVIRONMENT=Production`으로 실행한다.
- PostgreSQL, Discord, OpenAI, Gemini, GitHub Token에 예제 문자열이 남아 있지 않은지 확인한다.
- `config/*.json` 파일은 필요한 소스만 활성화한다.
- 외부 본문 수집에서 사설망 접근 허용은 기본값 `false`를 유지한다.

## 시작과 종료

```bash
docker compose up -d --build
docker compose ps
docker compose logs -f worker
```

안전한 종료:

```bash
docker compose stop worker
docker compose stop postgres
```

Worker는 SIGTERM을 받으면 수집 Loop와 Discord Gateway를 취소하고 종료한다. Compose의 종료 유예 시간은 30초다.

## 상태 확인

Discord 허용 채널:

```text
/status
```

Docker:

```bash
docker compose ps
docker inspect --format='{{json .State.Health}}' signal-radar-worker
```

직접 Health Check:

```bash
docker compose exec worker dotnet SignalRadar.Worker.dll --healthcheck
```

## 로그 정책

- 로그 파일은 Docker 로그 드라이버에서 파일당 10 MiB, 최대 3개로 회전한다.
- Worker 로그는 설정된 Token과 API Key를 `[REDACTED]`로 치환한다.
- Provider의 오류 응답은 최대 길이를 제한해 기록한다.
- 원문 HTML, Cookie, Authorization Header, Discord Token은 저장하거나 출력하지 않는다.

로그 확인:

```bash
docker compose logs --tail=200 worker
docker compose logs --since=30m worker
```

## PostgreSQL 백업

업그레이드나 대규모 설정 변경 전 백업한다.

```bash
docker compose exec -T postgres \
  pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
  --format=custom > signal-radar-$(date +%Y%m%d-%H%M%S).dump
```

PowerShell:

```powershell
docker compose exec -T postgres pg_dump `
  -U $env:POSTGRES_USER -d $env:POSTGRES_DB --format=custom `
  | Set-Content -Encoding Byte signal-radar.dump
```

백업 파일에는 기사 URL, 정제 본문, 피드백, Discord 사용자 기반 Actor ID가 포함될 수 있으므로 외부 공개 저장소에 업로드하지 않는다.

## 복구

1. Worker를 중지한다.
2. 대상 데이터베이스가 맞는지 확인한다.
3. 백업을 복원한다.
4. Worker를 다시 시작하고 `/status`를 확인한다.

```bash
docker compose stop worker
cat signal-radar.dump | docker compose exec -T postgres \
  pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
  --clean --if-exists

docker compose start worker
```

기존 데이터베이스 연결이 불가능한 상태라면 새 PostgreSQL Volume을 만든 뒤 복원한다.

## 업그레이드

```bash
git pull
docker compose build worker
docker compose up -d worker
```

Worker 시작 시 Migration Advisory Lock을 얻고, 적용된 Migration의 Checksum을 확인한 뒤 새 Migration을 순서대로 실행한다.

이 프로젝트의 SQL Migration은 자동 Down Migration을 제공하지 않는다. 이전 버전으로 되돌려야 할 때는 코드만 내리지 말고 업그레이드 전 백업도 함께 복원한다.

## Token 교체

### Discord

1. Developer Portal에서 Bot Token을 재발급한다.
2. `.env`의 `DISCORD_BOT_TOKEN`을 교체한다.
3. `docker compose up -d --force-recreate worker`를 실행한다.

### OpenAI·Gemini·GitHub

Provider 사이트에서 기존 Key를 폐기하고 `.env`의 값을 교체한 뒤 Worker를 재생성한다. Key 값은 PostgreSQL에 저장되지 않는다.

## 예약 다이제스트 복구

예약 전송 상태는 `digest_delivery_receipts`에 저장된다.

- 완료된 동일 기간은 재전송하지 않는다.
- 전송 중 프로세스가 종료되면 Lease 만료 후 재시도한다.
- 기사 0건도 완료 Receipt로 기록해 같은 기간을 계속 확인하지 않는다.
- 수동 `/digest`는 Receipt와 무관하며 언제든 다시 조회할 수 있다.

## 데이터 보존

현재 자동 삭제 정책은 제공하지 않는다. 장기 운영 시 다음 테이블의 크기를 확인한다.

```text
articles
article_content_cache
article_summary_cache
article_feedback
article_saves
digest_delivery_receipts
```

정제 본문은 원본 HTML보다 작게 제한되지만, 장기간 쌓일 수 있다. 보존 기간 자동화는 v0.1 이후 운영 기능으로 다룬다.

## 보안 경계

- 허용 Guild와 Channel 밖의 Discord 명령·버튼을 거부한다.
- 예약 전송 Channel도 허용 목록에 있어야 한다.
- 기사 원문 요청은 Public HTTP/HTTPS만 허용한다.
- Localhost, Loopback, 사설망, Link-local, CGNAT, Multicast 주소를 기본 차단한다.
- Redirect마다 대상 주소와 robots 정책을 다시 검사한다.
- JavaScript 실행, 로그인 세션, Paywall 우회, 인증 Cookie 사용은 지원하지 않는다.
- 기사 본문은 신뢰하지 않는 외부 데이터로 취급하며 그 안의 지시문을 LLM 명령으로 따르지 않는다.
