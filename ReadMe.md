# 📄 WPF OpenCV 라이브러리를 이용한 비전프로그램 개발
---

## 📅 2025년 12월 01일
**작성자:** indy  
**검토자:** indy

### 1. 개요
- WPF 기반 OpenCV 라이브러리를 이용하여 영상처리 프로그램 개발 진행

### 2. 진행 내용
- Single UI 처리 방식에서 이미지 로딩 및 이미지 알고리즘 적용 후 연산 시 백그라운드 쓰레드(Task)로 옯겨 비동기 처리 후 작업이 끝난 결과만 UI 쓰레드에 반영하도록 수정.
	OpenCVService.cs 에서 이미지 로드와 처리 함수를 async Task로 변경하여 백그라운드에서 동작하도록 변경.
	(무거운 Cv2 연산은 Task.Run 으로 감싸 백그라운드에서 실행하고, UI용 이미지(BitmapSource) 생성만 Dispatcher 를 통해 UI 쓰레드에서 수행.)

	MainViewModel.cs 에서 커맨드 핸들러를 async void 로 변경하고, 작업 중임을 알리는 IsBusy 속성을 추가하여 중복 실행을 막음.
	(IsBusy 속성을 추가하여 작업 중일 때 UI에 알리고, 커맨드 메서드를 async void로 변경하여 await 키워드를 사용할 수 있게 수정.)
 
	MainWindow.xaml 에서 작업 중에 표시할 로딩 바(ProgressBar) 추가
	(StatusBar의 오른쪽 끝에 IsBusy 속성과 바인딩된 **ProgressBar(진행률 표시줄)**를 추가하여, 사용자가 백그라운드 작업 중임을 알 수 있게 추가.)


---

## 📅 2025년 11월 28일
**작성자:** indy  
**검토자:** indy

### 1. 개요
- WPF 기반 OpenCV 라이브러리를 이용하여 영상처리 프로그램 개발 진행

### 2. 진행 내용
- 이미지 로드 후 Gray 처리 알고리즘 처리에서 Gray 변경 알고리즘 추가.
- Custom Title bar 적용하고, Window Title bar 를 제거. (UI 수정 및 UI 동작 코드 수정)
- Blob 분석 코드 수정. (label, stats, centroids, binary 의 Mat 데이터를 Using문으로 감싸서 메모리 누수 방지)
- Blob 분석은 배경이 검은색, 전경이 흰색인 물체를 분석하는 알고리즘으로 Wafer Die 이미지의 경우 반대로 배경이 흰색, 전경이 검은색이기 때문에 Invert 알고리즘을 추가하여 분석 가능하도록 수정.
- UI 구문에 Blob CheckBox 추가.
- PreviewModel 함수내에 C# pattern matching으로 변수 선언 및 할당으로 수정.

---


## 📅 2025년 11월 27일
**작성자:** indy  
**검토자:** indy

### 1. 개요
- WPF 기반 OpenCV 라이브러리를 이용하여 영상처리 프로그램 개발 진행

### 2. 진행 내용
- 이미지 로드 후 마우스 우클릭 시 ContextMenu 생성.
	직선 그리기: 픽셀 값으로 시작점과 끝점을 이용하여 픽셀 거리 계산.
	원 그리기
	사각형 그리기
- 알고리즘 선택 부분에 있었던 ROI 설정 부분을 알고리즘 항목에서 제외하고, 이미지 로드 후 마우스 우클릭에 따른 ContextMenu 항목으로 삽입.
	기존에 구현되어 있던 동작(ROI 사각형 안에서 마우스 우클릭에 따른 메뉴는 그대로 유지.)

---


## 📅 2025년 11월 26일
**작성자:** indy  
**검토자:** indy

### 1. 개요
- WPF 기반 OpenCV 라이브러리를 이용하여 영상처리 프로그램 개발 진행

### 2. 진행 내용
- TemplateMatching 알고리즘 적용. (GMF동 동일하게 동작 중)
- GMF 전용으로 사용되던 세개의 함수이름 변경 하였고, 인자도 Object 로 변경. 코드내의 수정 진행.
	LoadGmfModelImage -> LoadModelImage
	PreviewGmfModel -> PreviewModel
	_cvServices.TrainGmfModel -> _cvServices.TrainModel (MainViewModel.cs 내에도 TrainModel 이라는 함수가 존재하지만 다른 것임.)


---



## 📅 2025년 11월 25일
**작성자:** indy  
**검토자:** indy

### 1. 개요
- WPF 기반 OpenCV 라이브러리를 이용하여 영상처리 프로그램 개발 진행

### 2. 진행 내용
- 기존 MIL 라이브러리 기반 영상처리 프로그램의 UI와 코드 일부 사용하고, OpenCVService.cs 파일 추가.
- 기본 틀만 잡음.
- Template matching 알고리즘 작성 진행 중. (GMF 동작은 현재 Template matching 알고리즘 함수로 구현되어 있어 알고리즘 분리하고, GMF를 따로 구현할 예정.)

---



