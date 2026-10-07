# VisionCore — Cognex In-Sight 멀티 센서 비전 검사 HMI

> Cognex In-Sight 비전 센서 여러 대를 **하나의 화면에서 연결·운영·기록·추적**하는 WPF 기반 산업용 검사 HMI
> (앱 타이틀: *FAPLUS Vision Professional*)

![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4)
![WPF](https://img.shields.io/badge/UI-WPF%20%2B%20MVVM-0C54C2)
![C#](https://img.shields.io/badge/C%23-7.3-239120)
![Cognex](https://img.shields.io/badge/Cognex-In--Sight%20Web%20API-F7A800)
![WebSocket](https://img.shields.io/badge/Protocol-WebSocket%20%2F%20HTTP-555)

<!-- 스크린샷: docs/screenshots/ 에 이미지를 넣고 경로를 맞춰주세요 -->
<p align="center">
  <img src="docs/screenshots/main.png" width="90%" alt="Main Dashboard"/>
</p>

---

## 한눈에 보기

| 구분 | 내용 |
|---|---|
| **목적** | 생산 라인의 다중 비전 센서 검사 결과를 실시간 모니터링하고, 이미지·판정 이력을 자동으로 보관 및 추적 |
| **대상 장비** | Cognex In-Sight 비전 센서 / In-Sight Emulator |
| **통신** | In-Sight Web API (WebSocket 기반 CogSocket + HTTP 이미지 다운로드) |
| **아키텍처** | MVVM (View ↔ ViewModel ↔ Model/Service 분리) |
| **주요 역할** | 장비 탐색, 멀티 센서 대시보드, 결과 CSV 기록, 결과 이력 조회·필터링, 오프라인 재검사, 디스크 자동 정리 |

---

## 핵심 기능

### 1. 장비 자동 탐색 (Device Discovery)
- **로컬 에뮬레이터 + 네트워크 카메라 병렬 스캔** (`Task.WhenAll`)
- 에뮬레이터: 실행 중인 프로세스의 PID에서 `GetExtendedTcpTable`(iphlpapi P/Invoke)로 **실제 리스닝 포트 추출**
- 네트워크: 로컬 서브넷을 순회하며 TCP 포트 개방 확인 후 **HMI 포트 유효성 검증**

### 2. 멀티 센서 대시보드
- 센서별 독립 세션(`SensorSessionViewModel`)으로 여러 대를 동시에 연결·관리
- 타일 자동 정렬(Arrange Tiles), 센서 추가/해제
- 센서별 제어: **Live / Manual Trigger / ONLINE·OFFLINE 전환 / Graphics·Grid 표시**
- In-Sight 스프레드시트 편집기가 연결된 경우 **ONLINE 전환 차단 상태를 UI로 안내**

### 3. 검사 결과 자동 기록
- **ONLINE(생산) 상태에서만** 결과 저장 → 수동 트리거/재검사 결과가 이력에 섞이지 않음
- 결과 이미지 **무손실 BMP 저장**: `저장경로/yyyy/MM/dd/센서명/OK|NG/HH-mm-ss-fff.bmp`
- 이미지 다운로드 실패 시에도 **판정 결과는 반드시 기록** (데이터 유실 방지)
- 전용 백그라운드 스레드에서 **일 단위 CSV 배치 기록** → UI 스레드 블로킹 없음, 파일 잠금 감지 및 재시도

### 4. Point 기반 결과 매핑
- PLC가 기록하는 **현재 Point 번호 셀**을 읽어 결과를 Point 단위로 구분
- 결과 셀(OK/NG) + **사용자 정의 필드(이름·셀 위치)** 를 설정 화면에서 자유롭게 추가

### 5. 결과 이력 뷰어 (Result History)
- 날짜별 CSV 로드 + **실시간 신규 행 추가** (배치 Flush로 UI 부하 최소화)
- 센서별 최신 판정 요약(OK/NG), 전체 건수 표시
- **Excel 스타일 컬럼 헤더 필터** (값 목록 체크박스, 전체 선택/해제) + 전체 텍스트 검색
- 행 컨텍스트 메뉴: 이미지 열기 / 이미지 폴더 열기 / 행 복사

### 6. 오프라인 재검사 (Re-inspection)
- 이력에서 선택한 이미지를 **센서에 업로드해 Job을 재실행** → 센서 그래픽·판정을 해당 이미지 기준으로 재현
- 빠르게 연속 선택해도 **진행 중 1건 완료 후 마지막 선택만 처리** (요청 폭주 방지)
- 미지원 센서(404)는 자동 감지 후 조용히 이미지 표시로 대체

### 7. Job 관리
- Windows 탐색기 스타일 **Job 브라우저**: 내 PC ↔ 비전 센서 파일 시스템 탐색
- Job Load / Save / Save As

### 8. 디스크 자동 정리
- 설정한 **디스크 사용률(50~95%) 초과 시 가장 오래된 날짜 이미지부터 자동 삭제**
- 안전장치: 드라이브 루트·시스템 경로 차단, 심볼릭 링크(Reparse Point) 제외, `yyyy/MM/dd` 형식 폴더만 대상

### 9. 설정 & 로그
- NETWORK / POINTS / STORAGE 탭 구성의 설정 창 (INI 기반 영속화)
- 레벨별(Info/Success/Warning/Error) 색상 구분 실시간 로그 패널

---

## 화면 구성

| 메인 대시보드 | 결과 이력 / 필터 |
|:---:|:---:|
| <img src="docs/screenshots/dashboard.png" width="420"/> | <img src="docs/screenshots/history.png" width="420"/> |
| **설정 (Configuration)** | **Job 브라우저** |
| <img src="docs/screenshots/config.png" width="420"/> | <img src="docs/screenshots/job-browser.png" width="420"/> |

---

## 아키텍처

```
┌──────────────────────── View (XAML) ────────────────────────┐
│ MainWindow · ConfigWindow · JobBrowserWindow · InSightBehavior│
└───────────────────────────┬──────────────────────────────────┘
                            │ DataBinding / Command
┌──────────────────────── ViewModel ──────────────────────────┐
│ MainViewModel ── SensorSessionViewModel (센서 N개)            │
│              ├─ ResultHistoryViewModel (이력·필터)            │
│              ├─ DiskStatusViewModel   (디스크 감시·정리)       │
│              └─ ConfigViewModel / JobExplorerViewModel        │
└───────────────────────────┬──────────────────────────────────┘
┌──────────────────────── Model / Service ────────────────────┐
│ CameraControlModel (탐색·연결)  ResultCsvService (CSV I/O)    │
│ FileManagerModel (Job·결과 매핑) ConfigModel · LoggerService  │
└───────────────────────────┬──────────────────────────────────┘
┌──────────────── Cognex.InSight.Web (SDK Layer) ─────────────┐
│ CvsInSight · CogSocket(WebSocket) · Serialization(Cog 그래픽)│
│ Cognex.InSight.Web.Controls (CvsDisplay · CvsSpreadsheet ...)│
└──────────────────────────────────────────────────────────────┘
```

## 프로젝트 구조

```
VisionCore/
├─ Views/                       # XAML 화면, WinForms 컨트롤 호스팅 Behavior
├─ ViewModels/                  # MVVM ViewModel, RelayCommand
├─ Models/                      # 장비·파일·CSV·설정·로그 서비스
├─ Cognex.InSight.Web/          # In-Sight Web API 통신 / 직렬화 라이브러리
├─ Cognex.InSight.Web.Controls/ # 이미지 디스플레이·스프레드시트 컨트롤
└─ Resources/                   # 이미지, 다국어(Language.ini)
```

---

## 기술 스택

| 영역 | 사용 기술 |
|---|---|
| Language / Runtime | C# 7.3, .NET Framework 4.8 |
| UI | WPF (MVVM), WinForms 컨트롤 호스팅 |
| Communication | WebSocketSharp, HttpClient, Cognex In-Sight Web API |
| Serialization | Newtonsoft.Json |
| Concurrency | async/await, `Task.WhenAll`, 전용 Writer Thread, System.Threading.Channels |
| Native Interop | P/Invoke (`iphlpapi.dll`, `kernel32.dll` INI) |

---

## 기술적 포인트

- **UI 응답성 확보** — 네트워크 스캔·이미지 저장·CSV 기록을 모두 비동기/백그라운드로 분리
- **데이터 무결성** — 생산(ONLINE) 결과만 기록, 이미지 실패와 판정 기록을 분리해 유실 방지
- **운영 안정성** — 디스크 자동 정리 시 경로 안전성 검증, 파일 잠금 감지, 재검사 요청 디바운싱
- **확장성** — 센서 수·Point 수·결과 필드를 코드 수정 없이 설정으로 확장

---

## 빌드 & 실행

**요구 사항**: Windows 10/11, Visual Studio 2022 (.NET Framework 4.8 개발 도구), Cognex In-Sight 센서 또는 In-Sight Emulator

```bash
git clone <repo-url>
# Visual Studio에서 VisionCore.sln 열기 → NuGet 복원 → 빌드(F5)
```

> `dotnet build` 가 아닌 **Visual Studio / MSBuild** 로 빌드해야 합니다 (.NET Framework WPF 프로젝트).

**사용 흐름**
1. `Search Devices` 로 센서/에뮬레이터 탐색
2. 장비 선택 후 `Add` → 대시보드에 타일 추가
3. `Configuration` 에서 결과 셀·Point·저장 경로 설정
4. ONLINE 전환 → 검사 결과 자동 저장 및 이력 확인

---

## 개발자

**(이름)** · rlawkdyd13@gmail.com
