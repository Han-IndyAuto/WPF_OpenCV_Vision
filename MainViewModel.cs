using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace IndyVision
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));


        private OpenCVService _cvServices;
        // 연속 실행 방지용 타이머 (Debounce Timer)
        private DispatcherTimer _autoApplyTimer;


        #region Proerpties
        // --- Properties ---
        // 화면에 보여지는 데이터 (Properties)
        // 여기가 MVVM 패턴의 꽃입니다. 데이터가 변하면 화면이 자동으로 바뀝니다.

        // 마우스 좌표 표시용 속성
        private string _mouseCoordinationInfo = "(X: 0, Y: 0)";
        public string MouseCoordinationInfo
        {
            get => _mouseCoordinationInfo;
            set
            {
                if (_mouseCoordinationInfo == value) return;

                _mouseCoordinationInfo = value;
                OnPropertyChanged();
            }
        }

        // 화면에 표시되는 최종 이미지
        // _displayImage: 화면에 실제로 보여줄 이미지 데이터가 담긴 변수(금고)입니다.
        private ImageSource _displayImage;
        public ImageSource DisplayImage
        {
            // 화면이 "이미지 줘!" 하면 금고에서 꺼내줍니다.
            get => _displayImage;
            set
            {
                _displayImage = value;
                // "이미지가 바뀌었습니다!"라고 방송해서 화면이 다시 그려지게 합니다.
                OnPropertyChanged();
            }
        }

        // 분석 결과 텍스트 (예: "5 Objects Detected")
        private string _analysisResult = "Ready";
        public string AnalysisResult
        {
            get => _analysisResult;
            set { _analysisResult = value; OnPropertyChanged(); }
        }

        // _showOriginal: "원본 보기" 체크박스 상태 (True/False)
        // 원본 보기 체크박스 바인딩
        private bool _showOriginal;
        public bool ShowOriginal
        {
            get => _showOriginal;
            set
            {
                _showOriginal = value;
                // 체크박스 상태가 바뀜을 알림
                OnPropertyChanged();

                // 체크 상태가 바뀌면 이미지를 다시 불러옴 (원본 vs 결과)
                // [중요] 체크박스를 껐다 켰다 할 때마다 즉시 이미지를 바꿔 끼워줍니다.
                // (체크됨: 원본 보여줘 / 체크해제: 결과 보여줘)
                UpdateDisplay();
            }
        }

        // 콤보박스에 연결될 리스트
        // ObservableCollection: UI와 대화하는 똑똑한 리스트
        // 일반 List<string>: 데이터를 담을 수는 있지만, 데이터가 추가되거나 삭제되어도 화면(ListBox, ComboBox)은 그 사실을 모릅니다. 그래서 화면이 갱신되지 않습니다.
        // ObservableCollection<string>: 데이터가 추가(Add)되거나 삭제(Remove)되면, **"나 내용물 바뀌었어! 화면 다시 그려!"**라고 UI에게 즉시 알림을 보냅니다.
        // AlgorithmList: 사용자가 화면에서 보게 될 **"알고리즘 메뉴 목록"**을 담고 있는 그릇
        public ObservableCollection<string> AlgorithmList { get; set; }

        //알고리즘 선택과 동적 파라미터(가장 중요한 로직)

        private string _selectedAlgorithm;
        public string SelectedAlgorithm
        {
            get => _selectedAlgorithm;
            set
            {
                _selectedAlgorithm = value;
                OnPropertyChanged();

                // [핵심 로직] 
                // 사용자가 "이진화"를 선택하면 -> 이진화용 슬라이더 설정(Params)을 만듭니다.
                // 사용자가 "모폴로지"를 선택하면 -> 모폴로지용 설정(Params)을 만듭니다.
                // 알고리즘 선택 시 해당 파라미터 객체 생성
                CreateParametersForAlgorithm(value);

                // 알고리즘을 바꾸면 즉시 한번 실행.
                //ApplyAlgorithm(null); // 선택 즉시 적용
            }
        }

        // 변수 선언: 부모 타입(AlgorithmParamsBase)으로 선언
        // 현재 선택된 알고리즘의 설정값 객체 (UI의 ContentControl과 바인딩)
        // _currentParameters: 현재 선택된 알고리즘의 설정값(객체)입니다.
        // 이 변수에 무엇이 들어가느냐에 따라 화면 오른쪽 아래 UI(슬라이더/입력창)가 바뀝니다.
        private AlgorithmParamsBase _currentParameters;
        public AlgorithmParamsBase CurrentParameters
        {
            get => _currentParameters;
            //set { _currentParameters = value; OnPropertyChanged(); }  // 원본

            // 수정: 파라미터 변경 시 이벤트 연결/해제 로직 추가
            set
            {
                // 기존 파라미터가 있다면 이벤트 연결 해제 (메모리 누수 방지)
                if (_currentParameters != null)
                    _currentParameters.PropertyChanged -= OnParameterChanged;

                _currentParameters = value;

                // 새 파라미터에 이벤트 연결
                if (_currentParameters != null)
                    _currentParameters.PropertyChanged += OnParameterChanged;

                OnPropertyChanged();
            }
        }

        #endregion

        public MainViewModel()
        {
            _cvServices = new OpenCVService();

            AlgorithmList = new ObservableCollection<string>
            {
                "ROI Selection (영역 설정)",
                "Gray 처리", // (OpenCV 서비스 로직에서 예외처리 혹은 구현 필요, 여기선 생략)
                "Threshold (이진화)",
                "Adaptive Threshold (적응형 이진화)",
                "Morphology (모폴로지)",
                "Edge Detection (엣지 검출)",
                "Blob Analysis (블롭 분석)",
                "TemplateMatching (TM)",
                "Geometric Model Finder (GMF)" // 이름 유지, 내부 로직은 Template Matching
            };

            _autoApplyTimer = new DispatcherTimer();
            _autoApplyTimer.Interval = TimeSpan.FromMilliseconds(150);
            _autoApplyTimer.Tick += AutoApplyTimer_Tick;

        }

        private void AutoApplyTimer_Tick(object sender, EventArgs e)
        {
            _autoApplyTimer.Stop(); // 타이머 중지
            ApplyAlgorithm(null);   // 실제 알고리즘 적용.
        }

        private void OnParameterChanged(object sender, PropertyChangedEventArgs e)
        {
            // 슬라이더를 움직이면 자동으로 ApplyAlgorithm을 실행합니다.
            //if (!string.IsNullOrEmpty(SelectedAlgorithm))
            //{
            //   ApplyAlgorithm(null);
            //}

            // 바로 실행하지 않고, 타이머를 리셋.
            // 사용자가 슬라이더를 계속 움직이는 중이면 타이머가 계속 0으로 초기화 되어 실행을 미룹니다.
            // 움직임을 멈추면, 타이머가 설정된 시간 뒤에 Tick 이벤트가 발생하여 ApplyAlgorithm이 호출됩니다.
            _autoApplyTimer.Stop();
            _autoApplyTimer.Start();
        }

        // 선택된 이름(문자열)에 맞춰서 적절한 설정 객체(클래스)를 생성하는 공장입니다.
        private void CreateParametersForAlgorithm(string algoName)
        {
            // 선택된 이름에 따라 적절한 설정 클래스 생성
            switch (algoName)
            {
                case "ROI Selection (영역 설정)":
                    CurrentParameters = new RoiParams();
                    break;

                case "Threshold (이진화)":
                    // 이진화 설정을 담을 그릇을 새로 만듭니다. (기본값 128 등 포함)
                    CurrentParameters = new ThresholdParams();
                    break;

                case "Adaptive Threshold (적응형 이진화)":
                    CurrentParameters = new AdaptiveThresholdParams();
                    break;

                case "Morphology (모폴로지)":
                    CurrentParameters = new MorphologyParams();
                    break;

                case "Edge Detection (엣지 검출)":
                    CurrentParameters = new EdgeParams();
                    break;

                case "Blob Analysis (블롭 분석)":
                    CurrentParameters = new BlobParams();
                    break;

                case "Geometric Model Finder (GMF)":
                    CurrentParameters = new GmfParams();
                    break;

                case "TemplateMatching (TM)":    // TemplateMatching (TM)
                    CurrentParameters = new TemplateMatchParams();
                    break;

                default:
                    CurrentParameters = null; // 설정이 필요 없는 경우
                    break;
            }
        }

        /// <summary>
        /// ROI 자르기 및 저장 메서드 (View에서 호출할 함수)
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="w"></param>
        /// <param name="h"></param>
        public void CropImage(int x, int y, int w, int h)
        {
            try
            {
                _cvServices.CropImage(x, y, w, h);
                ShowOriginal = true;    // 잘린 이미지가 원본이 되므로 원본 보기로 전환.
                UpdateDisplay();
                AnalysisResult = $"이미지 자르기 완료 (크기: {w} x {h})";
            }
            catch (Exception ex)
            {
                // 예외 처리: 로그 기록 또는 사용자 알림
                Console.WriteLine($"AnalysisResult = Error: {ex.Message}");
            }
        }

        public void SaveRoiImage(string path, int x, int y, int w, int h)
        {
            try
            {
                _cvServices.SaveRoiImage(path, x, y, w, h);
                AnalysisResult = $"ROI 저장 완료: {path}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AnalysisResult = Error: {ex.Message}");
            }
        }


        // 상황에 따라 원본을 보여줄지, 처리된 결과를 보여줄지 결정하는 '교통정리' 함수입니다.
        private void UpdateDisplay()
        {
            if (ShowOriginal)
                // 체크박스가 켜져있으면 -> MilService에게 "원본 내놔"라고 함
                DisplayImage = _cvServices.GetOriginalImage();
            else
                // 꺼져있으면 -> "결과물 내놔"라고 함
                DisplayImage = _cvServices.GetProcessedImage();
        }


        // ROI로 저장해 둔 작은 이미지를 불러와 모델 정의 모드로 진입.
        private void LoadModel(object obj)
        {
            OpenFileDialog dlg = new OpenFileDialog { Filter = "Image Files|*.bmp;*.jpg;*.png;*.tif" };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    // GMF 모델용 이미지를 로드하고, 화면에 보여줌 (모델링 모드 진입)
                    //_cvServices.LoadGmfModelImage(dlg.FileName);
                    _cvServices.LoadModelImage(dlg.FileName);

                    // 화면 갱신
                    ShowOriginal = false;
                    UpdateDisplay();

                    // GMF 파라미터가 있으면 초기 미리보기 실행.
                    if (CurrentParameters is GmfParams gmfParams)
                    {
                        //_cvServices.PreviewGmfModel(gmfParams);
                        _cvServices.PreviewModel(gmfParams);
                        UpdateDisplay();
                    }

                    else if(CurrentParameters is TemplateMatchParams tmParams)
                    {
                        //_cvServices.PreviewGmfModel(tmParams);
                        _cvServices.PreviewModel(tmParams);
                    }

                    AnalysisResult = "모델 이미지 로드됨. 속성을 조절하여 모델을 정의하세요.";
                }
                catch (Exception ex)
                {
                    AnalysisResult = "Error: " + ex.Message;
                }
            }
        }


        private void TrainModel(object obj)
        {
            if (CurrentParameters is GmfParams gmfParams)
            {
                try
                {
                    //_cvServices.TrainGmfModel(gmfParams);
                    _cvServices.TrainModel(gmfParams);
                    AnalysisResult = "모델 등록 완료! 이제 검사 이미지를 열고 '적용'을 누르세요";

                    // 다시 검사 원본 이미지 보기로 전환.
                    ShowOriginal = true;
                    UpdateDisplay();
                }
                catch (Exception ex)
                {
                    AnalysisResult = "Train Error: " + ex.Message;
                }
            }
            else if (CurrentParameters is TemplateMatchParams tmParam)
            {
                try
                {
                    //_cvServices.TrainGmfModel(tmParam);
                    _cvServices.TrainModel(tmParam);
                    AnalysisResult = "모델 등록 완료! 이제 검사 이미지를 열고 '적용'을 누르세요";

                    // 다시 검사 원본 이미지 보기로 전환.
                    ShowOriginal = true;
                    UpdateDisplay();
                }
                catch (Exception ex)
                {
                    AnalysisResult = "Train Error: " + ex.Message;
                }
            }

        }



        // [파일 열기] 버튼을 눌렀을 때
        private void LoadImage(object obj)
        {
            // 윈도우 파일 탐색기를 엽니다.
            OpenFileDialog dlg = new OpenFileDialog { Filter = "Image Files|*.bmp;*.jpg;*.png" };
            // 파일을 선택하고 확인을 눌렀다면
            if (dlg.ShowDialog() == true)
            {
                // 1. MilService에게 파일을 로드하라고 시킵니다.
                _cvServices.LoadImage(dlg.FileName);
                // 2. 막 로드했으니 사용자가 원본을 확인하도록 "원본 보기"를 켭니다.
                ShowOriginal = true; // 로드 직후엔 원본 보여주기
                // 3. 화면 갱신
                UpdateDisplay();
            }
        }

        // [적용] 버튼을 눌렀을 때
        private void ApplyAlgorithm(object obj)
        {
            // 알고리즘 선택을 안 했으면 아무것도 안 함
            if (string.IsNullOrEmpty(SelectedAlgorithm)) return;

            // GMF 모드일때, 슬라이더를 움직이면 검사가 아니라 모델 미리보기를 적용
            // String.Contain("검색어") 함수는 긴 문장안에 검색어가 포함되어 있는지 물어 보는 함수.
            if (SelectedAlgorithm.Contains("GMF") && _cvServices.IsModelDefinitionMode)
            {
                if (CurrentParameters is GmfParams gmfParams)
                {
                    //_cvServices.PreviewGmfModel(gmfParams); // 모델 윤곽선 미리보기
                    _cvServices.PreviewModel(gmfParams); // 모델 윤곽선 미리보기
                    UpdateDisplay();
                    return; // 검사는 하지 않고 리턴
                }
            }
            else if(SelectedAlgorithm.Contains("TM") && _cvServices.IsModelDefinitionMode)
            {
                if(CurrentParameters is TemplateMatchParams tmParams)
                {
                    //_cvServices.PreviewGmfModel(tmParams);
                    _cvServices.PreviewModel(tmParams);
                    UpdateDisplay();
                    return;
                }
            }

            // 일반 검사 로직 (기존과 동일)
            try
            {
                // [수정] ProcessImage가 결과를 반환하도록 변경하거나, 호출 후 결과를 받아옴
                string result = _cvServices.ProcessImage(SelectedAlgorithm, CurrentParameters);
                AnalysisResult = result;

                // 처리가 끝났으니 결과를 보여주기 위해 "원본 보기"를 끕니다.
                ShowOriginal = false; // 적용 후엔 결과 보기로 자동 전환
                                      // 화면 갱신 (결과 이미지가 뜸)
                UpdateDisplay();
            }
            catch (Exception ex)
            {
                AnalysisResult = "처리 중 에러: " + ex.Message;
            }
        }

        // 프로그램 종료 시 호출되어 메모리를 청소합니다.
        public void Cleanup() => _cvServices.Cleanup();


        // --- Commands ---
        // 버튼과 연결되는 끈(Command)입니다.
        public ICommand LoadImageCommand => new RelayCommand(LoadImage);
        public ICommand ApplyAlgorithmCommand => new RelayCommand(ApplyAlgorithm);

        public ICommand LoadModelCommand => new RelayCommand(LoadModel);
        public ICommand TrainModelCommand => new RelayCommand(TrainModel);

    }


    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        public RelayCommand(Action<object> execute) => _execute = execute;
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _execute(parameter);
        public event EventHandler CanExecuteChanged;
    }
}
