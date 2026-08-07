# Discord에 Signal Radar 적용하기

이 문서는 개인 서버에 Signal Radar 봇을 설치하고 실제 수집·명령·예약 다이제스트까지 동작시키는 절차다.

## 1. Discord 애플리케이션 만들기

1. [Discord Developer Portal](https://discord.com/developers/applications)에 로그인한다.
2. **New Application**을 누르고 이름을 `Signal Radar`로 지정한다.
3. 왼쪽 **Bot** 메뉴에서 봇 사용자를 만든다.
4. **Reset Token** 또는 토큰 표시 버튼으로 Bot Token을 발급한다.
5. 토큰을 `.env`의 `DISCORD_BOT_TOKEN`에만 저장한다. 채팅, Git, 스크린샷에는 넣지 않는다.

토큰이 한 번이라도 노출됐다면 기존 토큰을 계속 사용하지 말고 Developer Portal에서 즉시 재발급한다.

## 2. Message Content Intent 켜기

Signal Radar는 지정 채널에 올라오는 GeekNews형 메시지와 Embed를 읽으므로 다음 설정이 필요하다.

1. Developer Portal의 **Bot** 메뉴로 이동한다.
2. **Privileged Gateway Intents**에서 **Message Content Intent**를 켠다.
3. **Presence Intent**와 **Server Members Intent**는 현재 기능에 필요하지 않으므로 끈 상태로 둔다.

이 설정이 꺼져 있으면 Slash Command는 보여도 채널 기사 수집은 동작하지 않는다.

## 3. 서버 설치 권한 설정

Developer Portal의 **Installation** 메뉴에서 Guild Install 기본 설정을 구성한다.

Scopes:

```text
applications.commands
bot
```

Bot Permissions:

```text
View Channels
Send Messages
Embed Links
Attach Files
Read Message History
```

설정을 저장한 뒤 Installation 페이지의 Install Link를 복사해 브라우저에서 열고, 본인이 관리하는 Discord 서버를 선택한다.

개발 중에는 운영 서버보다 별도의 테스트 서버에 먼저 설치하는 편이 안전하다.

## 4. Discord ID 복사하기

Discord 클라이언트에서 **사용자 설정 → 고급 → 개발자 모드**를 켠다.

이후 우클릭 메뉴의 **ID 복사**를 사용한다.

| 환경변수 | 복사할 값 |
|---|---|
| `DISCORD_ALLOWED_GUILD_IDS` | Signal Radar를 설치한 서버 ID |
| `DISCORD_ALLOWED_CHANNEL_IDS` | 기사 수집과 명령을 허용할 채널 ID |
| `DISCORD_ALLOWED_AUTHOR_IDS` | GeekNews Bot 또는 수집 허용 작성자의 ID |
| `DIGEST_CHANNEL_ID` | 예약 다이제스트를 공개 전송할 채널 ID |
| `DIGEST_ACTOR_USER_ID` | 랭킹 개인화를 적용할 본인 Discord 사용자 ID |

여러 서버·채널·작성자를 허용할 때는 쉼표로 구분한다.

```env
DISCORD_ALLOWED_GUILD_IDS=111111111111111111
DISCORD_ALLOWED_CHANNEL_IDS=222222222222222222,333333333333333333
DISCORD_ALLOWED_AUTHOR_IDS=444444444444444444
```

`DIGEST_CHANNEL_ID`는 반드시 `DISCORD_ALLOWED_CHANNEL_IDS`에도 들어 있어야 한다.

## 5. 로컬 설정 파일 준비하기

저장소 루트에서 다음 파일을 복사한다.

```bash
cp .env.example .env
cp config/feed-sources.starter.json config/feed-sources.json
cp config/github-repositories.starter.json config/github-repositories.json
cp config/ranking-profile.example.json config/ranking-profile.json
```

Windows PowerShell에서는 다음처럼 실행할 수 있다.

```powershell
Copy-Item .env.example .env
Copy-Item config/feed-sources.starter.json config/feed-sources.json
Copy-Item config/github-repositories.starter.json config/github-repositories.json
Copy-Item config/ranking-profile.example.json config/ranking-profile.json
```

`.env`에서 최소한 아래 값을 실제 값으로 교체한다.

```env
SIGNAL_RADAR_ENVIRONMENT=Production
POSTGRES_PASSWORD=충분히-긴-새-비밀번호

DISCORD_ENABLED=true
DISCORD_BOT_TOKEN=Developer-Portal에서-복사한-토큰
DISCORD_ALLOWED_GUILD_IDS=서버-ID
DISCORD_ALLOWED_CHANNEL_IDS=채널-ID
DISCORD_ALLOWED_AUTHOR_IDS=GeekNews-Bot-ID
DISCORD_REQUIRE_AUTOMATED_AUTHOR=true
DISCORD_SOURCE_NAME=geeknews
```

직접 작성한 일반 사용자 메시지도 수집하려면 `DISCORD_REQUIRE_AUTOMATED_AUTHOR=false`로 변경하고, 작성자 ID를 허용 목록에 넣는다.

## 6. AI 요약 Provider 선택하기

요약이 필요하지 않으면 다음 상태를 유지한다.

```env
SUMMARY_PROVIDER=disabled
```

OpenAI Responses API:

```env
SUMMARY_PROVIDER=openai-responses
OPENAI_API_KEY=발급한-API-Key
OPENAI_SUMMARY_MODEL=사용할-모델-ID
```

Gemini Generate Content API:

```env
SUMMARY_PROVIDER=gemini-generate-content
GEMINI_API_KEY=Google-AI-Studio에서-발급한-Key
GEMINI_SUMMARY_MODEL=gemini-3.6-flash
```

API Key가 없어도 수집, 랭킹, 저장, `/export`, `/digest`는 정상 동작한다. `/summarize`만 등록되지 않는다.

## 7. 예약 다이제스트 설정하기

매일 오전 8시와 매주 월요일 오전 8시에 서울 시간 기준으로 보내는 예시다.

```env
DIGEST_DAILY_ENABLED=true
DIGEST_DAILY_TIME=08:00
DIGEST_WEEKLY_ENABLED=true
DIGEST_WEEKLY_DAY=Monday
DIGEST_WEEKLY_TIME=08:00
DIGEST_TIME_ZONE=Asia/Seoul
DIGEST_CHANNEL_ID=허용된-채널-ID
DIGEST_ACTOR_USER_ID=본인-사용자-ID
DIGEST_TOPIC=all
DIGEST_LIMIT=10
```

예약 전송은 PostgreSQL Receipt와 Lease를 사용하므로 Worker가 재시작돼도 같은 기간의 메시지를 반복 전송하지 않는다. 전송 도중 실패하면 Lease가 해제된 뒤 다시 시도할 수 있다.

## 8. Docker Compose로 실행하기

```bash
docker compose up -d --build
```

상태 확인:

```bash
docker compose ps
docker compose logs -f worker
```

정상 시작 로그에는 다음 내용이 나타난다.

```text
PostgreSQL migrations and startup health check completed.
Discord inbox gateway started.
Discord commands synchronized for guild ...
```

Worker 컨테이너의 Docker Health Check는 별도 프로세스로 PostgreSQL 연결을 검사한다.

## 9. Discord에서 실제 확인하기

봇이 온라인이 된 뒤 허용 채널에서 순서대로 실행한다.

```text
/status
/top
/digest period:daily
```

그다음 GeekNews Bot이 링크가 포함된 메시지를 올리게 하거나, 허용된 작성자로 테스트 메시지를 보낸다. 수집 후 `/search`, `/top`에 기사가 나타나는지 확인한다.

기사의 `저장` 버튼을 누른 뒤:

```text
/saved
/export
/summarize
```

`/summarize`는 Summary Provider가 활성화된 경우에만 보인다.

명령은 Guild 범위로 동기화하므로 Worker가 Ready 상태가 된 직후 대체로 바로 표시된다.

## 10. 자주 막히는 문제

### Slash Command가 보이지 않음

- 앱 설치 Scope에 `applications.commands`가 포함됐는지 확인한다.
- `DISCORD_ALLOWED_GUILD_IDS`가 실제 서버 ID인지 확인한다.
- Worker 로그에 `Discord commands synchronized`가 있는지 확인한다.
- 앱을 다른 서버에 설치했다면 해당 서버 ID도 허용 목록에 추가하고 Worker를 재시작한다.

### 명령은 되는데 기사 메시지를 못 읽음

- Developer Portal에서 **Message Content Intent**를 켰는지 확인한다.
- 메시지가 `DISCORD_ALLOWED_CHANNEL_IDS` 채널에 올라왔는지 확인한다.
- 작성자 Bot ID가 `DISCORD_ALLOWED_AUTHOR_IDS`에 있는지 확인한다.
- 일반 사용자의 메시지를 읽으려면 `DISCORD_REQUIRE_AUTOMATED_AUTHOR=false`가 필요하다.

### 예약 다이제스트가 오지 않음

- `DIGEST_CHANNEL_ID`가 허용 채널 목록에도 있는지 확인한다.
- 봇에 View Channels, Send Messages, Embed Links 권한이 있는지 확인한다.
- `DIGEST_TIME_ZONE=Asia/Seoul`과 `HH:mm` 형식을 확인한다.
- `/digest`가 수동으로 동작하는지 먼저 확인한다.

### `/summarize`가 보이지 않음

- `SUMMARY_PROVIDER` 값과 해당 API Key·Model 설정을 확인한다.
- Worker를 재시작해야 명령 목록이 다시 동기화된다.
- Provider가 비활성화된 경우 `/summarize`를 등록하지 않는 것이 정상이다.

### 봇이 시작 직후 종료됨

```bash
docker compose logs --tail=200 worker
```

Production 모드에서는 `replace-me`, `change-me` 같은 예제 비밀번호나 토큰을 거부한다. `.env`의 실제 비밀번호와 Token을 확인한다.
