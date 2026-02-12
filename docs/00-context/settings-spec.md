Span (스팬) 설정 창(Settings) 상세 기획안을 정리해 드립니다.



WinUI 3의 표준 컨트롤인 \*\*NavigationView (사이드바)\*\*와 \*\*SettingsExpander (설정 카드)\*\*를 적극 활용하여, 기능은 많지만 복잡해 보이지 않는 \*\*'개발자 친화적'\*\*인 구조입니다.



1\. UI 구조 및 디자인 컨셉

레이아웃: 좌측 사이드바(카테고리) + 우측 컨텐츠(스크롤 가능한 패널).



검색 중심: 좌측 최상단에 \[🔍 설정 검색] 바를 배치하여 어떤 옵션이든 타이핑으로 즉시 접근.



소재: Mica (마이카) 재질을 배경으로 사용하여 윈도우 11과 일체감 형성.



아이콘: Segoe Fluent Icons 사용.



2\. 카테고리별 상세 구성

A. 🔍 검색 (Header)

기능: 설정 항목 이름, 키워드(예: "터미널", "cmd")로 실시간 필터링.



UX: 검색어 입력 시 사이드바가 숨겨지고 검색 결과 리스트만 표시됨.



B. ⚙️ 일반 (General)

가장 기초적인 앱 동작 설정입니다.



언어 (Language)



UI: \[ 🌐 언어 선택 ] - \[ 콤보 박스 ]



옵션: System Default (권장), 한국어, English, 日本語



알림: 변경 시 "앱을 재시작해야 완벽하게 적용됩니다." 문구 노출.



시작 시 동작 (Startup)



UI: \[ 🚀 앱을 실행할 때 ] - \[ 라디오 버튼 ]



옵션:



마지막 세션 복원 (이전에 열어둔 탭/경로 유지 - Default)



내 PC (홈) 열기



특정 폴더 열기...



시스템 트레이 (System Tray)



UI: \[ 닫기 버튼을 누르면 트레이로 최소화 ] - \[ 토글 스위치 ]



C. 🎨 모양 (Appearance) ✨ \[수익화 핵심]

사용자의 눈을 즐겁게 하고, Pro 결제를 가장 강하게 유도하는 섹션입니다.



앱 테마 (App Theme)



UI: \[ 🌗 테마 선택 ] - \[ 콤보 박스 ]



옵션: 시스템 설정, 라이트, 다크



Pro 전용 테마 (Premium Themes) 🔒



UI: SettingsExpander (카드 형태)



내용: "개발자를 위한 프리미엄 테마 잠금 해제"



리스트:



🌑 Midnight Gold (블랙 \& 골드 포인트) - 🔒 Pro Only



🔮 Cyberpunk (네온 퍼플 \& 사이언) - 🔒 Pro Only



🌲 Nordic (차분한 블루 그레이) - 🔒 Pro Only



액션: 클릭 시 \[MS Store 구매 팝업] 호출.



레이아웃 밀도 (Layout Density)



UI: \[ 📏 목록 간격 ] - \[ 슬라이더 또는 버튼 ]



옵션: Compact (좁게) (개발자 선호), Comfortable (보통), Spacious (넓게)



폰트 (Font)



UI: \[ 🔤 폰트 선택 ] - \[ 콤보 박스 ]



옵션: Segoe UI Variable (기본), Cascadia Code (개발자용), Pretendard 등



D. 🧭 탐색 (Browsing)

파일 관리의 본질적인 동작을 제어합니다.



보기 옵션 (View Options)



UI: 토글 스위치 모음



숨김 항목 표시 (Show Hidden Items) (단축키: Ctrl+H)



파일 확장자 표시 (Show Extensions)



체크박스 사용하여 항목 선택



밀러 컬럼 동작 (Miller Columns Behavior)



UI: \[ 👉 폴더 열기 방식 ]



옵션: 한 번 클릭으로 하위 폴더 펼치기 (Default), 더블 클릭으로 펼치기



이미지 미리보기 (Thumbnails)



UI: \[ 🖼️ 썸네일 표시 ] - \[ 토글 ]



설명: "성능을 위해 네트워크 드라이브에서는 썸네일을 끕니다."



E. ⚡ 도구 (Tools) 💻 \[개발자 전용]

타겟 유저(개발자)가 가장 열광할 기능들입니다.



터미널 통합 (Terminal Integration)



UI: \[ 💻 기본 터미널 앱 ] - \[ 콤보 박스 ]



옵션: Windows Terminal (추천), PowerShell, CMD, Git Bash



동작: Ctrl+T 또는 우클릭 '터미널 열기' 시 실행될 앱.



Smart Run (빠른 명령 실행)



UI: \[ ⚡ Smart Run 활성화 ] - \[ 토글 ]



설정: "자주 쓰는 명령어 단축키 관리" (확장 패널)



gp -> git pull



gs -> git status



code -> code .



컨텍스트 메뉴 (Context Menu)



UI: \[ 윈도우 우클릭 메뉴에 'Open in Span' 추가 ] - \[ 토글 ]



F. 💎 라이선스 및 정보 (About \& License)

앱의 신뢰도를 높이고 후원을 받는 공간입니다.



앱 정보 (App Info)



로고: Span 아이콘 (크게)



버전: v1.0.0 (Build 20260211)



업데이트: \[업데이트 확인] 버튼 (회전 애니메이션)



라이선스 상태 (License Status)



상태:



무료 유저: Evaluation Copy (Unregistered) - 회색



유료 유저: Span PRO License (Registered) - 금색/굵게



구매 버튼 (무료일 때만 보임):



\[ 💎 Upgrade to Pro ($14.99) ] - 크고 강조된 버튼.



"Pro 배지를 획득하고 개발자를 응원해주세요."



개발자 후원 (Donation) ☕



제목: "Buy me a coffee"



UI: 귀여운 버튼 3~4개 나열.



\[ ☕ $3 ] \[ 🍰 $5 ] \[ 🍕 $10 ] \[ 🚀 $50 ]



설명: "Pro 구매가 부담스럽다면, 커피 한 잔으로 응원해 주세요!"



관련 링크



GitHub 저장소, 버그 제보, 개인정보 처리방침



3\. 구현 가이드 (XAML 구조 예시)

WinUI 3 갤러리의 \*\*SettingsExpander\*\*를 사용하면 디자인이 아주 깔끔하게 나옵니다.



XML

<ScrollViewer>

&nbsp;   <StackPanel Spacing="20" Padding="24">

&nbsp;       

&nbsp;       <TextBlock Text="일반" Style="{StaticResource TitleTextBlockStyle}"/>

&nbsp;       

&nbsp;       <controls:SettingsExpander Header="언어 (Language)" IconSource="World">

&nbsp;           <ComboBox SelectedIndex="0">

&nbsp;               <ComboBoxItem Content="시스템 기본값"/>

&nbsp;               <ComboBoxItem Content="한국어"/>

&nbsp;               <ComboBoxItem Content="English"/>

&nbsp;           </ComboBox>

&nbsp;       </controls:SettingsExpander>



&nbsp;       <TextBlock Text="도구" Style="{StaticResource TitleTextBlockStyle}"/>

&nbsp;       

&nbsp;       <controls:SettingsExpander Header="기본 터미널" IconSource="CommandPrompt"

&nbsp;                                  Description="단축키(Ctrl+T) 사용 시 실행할 터미널을 선택합니다.">

&nbsp;           <ComboBox SelectedIndex="0">

&nbsp;               <ComboBoxItem Content="Windows Terminal"/>

&nbsp;               <ComboBoxItem Content="PowerShell"/>

&nbsp;               <ComboBoxItem Content="Command Prompt"/>

&nbsp;           </ComboBox>

&nbsp;       </controls:SettingsExpander>

&nbsp;       

&nbsp;   </StackPanel>

</ScrollViewer>

