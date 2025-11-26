# 📄 WPF OpenCV 라이브러리를 이용한 비전프로그램 개발
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



