# CareerMode-Footballer

Football Manager와 FC 커리어 모드를 섞은 축구 게임. 경기 중에는 선수 한 명만 조작한다. 지금은 프로토타입이다.

## 핵심 — 시야 제한

화면은 선수가 보는 방향만 밝다(Escape from Duckov 방식). **선수는 달리는 쪽을 보고, Q를 누르는 동안에만 커서 쪽을
본다**(어깨 너머 확인). 커서는 늘 조준점이다.
능력치(Vision → 시야각, Anticipation → 잔상 시간, Composure → 압박 시 시야 축소)를 시야에 이어,
능력치가 오르면 숫자가 아니라 화면이 넓어지게 하는 것이 목표다.

**검증 지표:** HUD 왼쪽 위 "확인 후 / 블라인드" 성공률. 격차가 벌어지지 않으면 시야 설계를 다시 한다.

## 실행

Unity 6000.4.1f1, URP 17.4.0. 씬 파일이 기준이다(빌더 없음).

| 씬 | 내용 |
|---|---|
| `Assets/_Prototype/Scenes/ScanAndTurnDrill.unity` | 경기 |
| `Assets/Career/Scenes/CharacterCreation.unity` | 캐릭터 생성 |
| `Assets/Career/Scenes/ShotDrill.unity` | 슛 훈련 |

## 조작

| 키 | 동작 |
|---|---|
| WASD / 마우스 | 이동(시선도 이 방향) / 조준 |
| 좌클릭(E) · 마우스5 · 마우스4(F) | 짧은 패스 · 로빙 · 스루패스 |
| 우클릭+좌클릭(R) / C | 슛(누르는 동안 세기 게이지, 떼면 찬다) / 크로스 |
| Q | 어깨 너머 확인(홀드) — 누르는 동안 커서 쪽을 본다 |
| Shift / Space / LeftCtrl | 스프린트 / 몸으로 지키기 / 제자리 고정 |
| G / Tab | 디버그 뷰 / 시야 제한 토글 |
| P / T / Backspace | 패스 벤치 / 압박 드릴 / 리셋 |

## 문서

- [PROJECT.md](PROJECT.md) — 현재 설계, 그 이유, 수치. 코드를 만지기 전에 읽을 것.
- [DECISIONS.md](DECISIONS.md) — 누가 무엇을 정했는가.
