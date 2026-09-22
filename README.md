# Insight-Web-API / VisionCore

Cognex In-Sight Suite 기반 산업용 비전 검사 시스템 및 제어 프로그램.
In-Sight 센서(또는 에뮬레이터)에 HMI 프로토콜로 접속해 여러 대를 동시에 모니터링/제어하고,
검사 결과를 Point 단위로 기록·저장하는 WPF 데스크톱 앱입니다.

## 주요 기능

- **다중 센서 대시보드**: 스캔으로 찾은 센서/에뮬레이터를 여러 개 동시에 연결해 타일로 표시.
  타일은 자유롭게 드래그 이동·모서리로 크기 조절·전체화면 토글이 가능하고, "정렬" 버튼으로
  언제든 격자 배치로 되돌릴 수 있음.
- **Point 기반 실시간 결과 모니터링**: 검사 결과 셀(Result Cell)과 PLC가 알려주는 현재 Point 번호
  (Current Point Number Cell)를 기준으로, 센서별·Point별 최신 판정(OK/NG)과 이미지를 타일 하단에
  실시간으로 표시. 썸네일을 클릭하면 해당 센서 디스플레이에 그 이미지를 바로 띄워 확인 가능.
- **사용자 정의 추적 필드**: Point 개수/Result/PointNumber 셀 외에 추가로 보고 싶은 값이 있으면
  Config에서 이름과 셀 위치를 자유롭게 추가·삭제 가능.
- **결과 이미지 저장**: 저장 경로를 Config에서 직접 지정 가능. 센서별/OK·NG별/날짜별로 자동 정리되어 저장됨.
- **그리드(스프레드시트) 편집**: 셀을 더블클릭하면 팝업창 없이 그리드 스타일 그대로 셀 안에서 바로
  식(expression)을 입력할 수 있고, 셀 주소(A0~Z599) 자동완성을 지원. 선택한 셀이 참조하는/참조당하는
  셀은 화살표로 시각화됨.
- **Job / Live / Trigger / Online 제어**: 센서별로 Job 파일 열기·저장, Live 모드, 수동 Trigger,
  Online/Offline 전환을 타일 안에서 개별적으로 제어.
- 설정을 저장하면 앱을 재시작하지 않아도 연결된 센서들에 즉시 반영됨.

## 프로젝트 구조

```
VisionCore.sln
VisionCore/                        WPF 메인 애플리케이션
  Models/                          ConfigModel, CameraControlModel, FileManagerModel, LoggerService 등
  ViewModels/                      MainViewModel, SensorSessionViewModel(센서 1개=타일 1개), ConfigViewModel 등
  Views/                           MainWindow, ConfigWindow, InSightBehavior(WPF↔WinForms 연결 담당)
  Cognex.InSight.Web/              In-Sight HMI 프로토콜 통신 SDK 래퍼 (CvsInSight 등)
  Cognex.InSight.Web.Controls/     WinForms 기반 영상 표시(CvsDisplay)/스프레드시트(CvsSpreadsheet) 컨트롤
```

- MVVM 패턴 사용 (커스텀 `ViewModelBase`/`RelayCommand`, CommunityToolkit.Mvvm은 미사용).
- 센서 연결 정보는 싱글톤이 아니라 `SensorSessionViewModel` 인스턴스 단위로 관리되어 여러 대를
  동시에 붙일 수 있음.

## 빌드 / 실행

- .NET Framework 4.8 대상. Visual Studio 2022(또는 Build Tools)로 `VisionCore.sln`을 열어 빌드/F5.
- ⚠️ 이 저장소의 `VisionCore.csproj`는 구버전(non-SDK) 프로젝트 형식이라, 환경에 따라
  `dotnet build`만으로는 WPF XAML 컴파일 단계가 정상적으로 수행되지 않을 수 있습니다.
  Visual Studio에서 빌드하거나, VS Build Tools의 `MSBuild.exe`로 빌드하세요.

## 설정 파일

최초 실행 시 실행 파일 폴더에 자동 생성됩니다.

- `config.ini`: 카메라 연결 정보(IP/Port/계정), Point 개수, Result/Current Point Number 셀 위치,
  결과 이미지 저장 경로
- `customfields.json`: 사용자가 추가한 커스텀 추적 필드(이름 + 셀 위치) 목록
- `VisionImage/`: 검사 결과 이미지 저장 폴더(저장 경로는 Config에서 변경 가능)
