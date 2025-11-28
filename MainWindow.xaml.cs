using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace IndyVision
{
    // 그리기 모드 열거형.
    public enum DrawingMode
    {
        None,
        Roi,
        Line,
        Circle,
        Rectangle
    }

    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window
    {
        // 이미지 이동 [Pan]을 위한 변수들.
        private Point _origin;              // 이동 시작 시점의 이미지 위치 (X, Y)
        private Point _start;               // 이동 시작 시점의 마우스 포인터 위칭 (X, Y)
        private bool _isDragging = false;   // 현재 마우스로 끌고 있는지 여부 (true/false)

        // ROI 관련 변수
        private bool _isRoiDrawing = false; // 현재 ROI를 그리고 있는지 여부.
        private Point _roiStartPoint;       // 이미지 기준 좌표: ROI 사각형을 그리기 시작한 점 (클릭한 곳)
        private Rect _currentRoiRect;       // 최종적으로 계산된 ROI 영역 (X, Y, W, H)

        // 그리기 관련 변수
        private DrawingMode _currentDrawMode = DrawingMode.None;
        private Point _drawStartPoint;      // 그리기 시작점 (이미지 좌표)
        private Shape _tempShape;           // 그리기 도중 보여줄 임시 도형.

        public MainWindow()
        {
            InitializeComponent();          // XAML에 그려진 UI 요소들을 메모리에 로드하고 화면에 띄웁니다.
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
                this.WindowState = WindowState.Normal;
            else
                this.WindowState = WindowState.Maximized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            if (MaximizeButton == null) return;

            if (this.WindowState == WindowState.Maximized)
            {
                // WindowKey + . 를 눌렀을 때 아이콘을 선택하여 넣을 수 있음.
                MaximizeButton.Content = "❐"; // 복원 아이콘 (겹친 네모)
                MaximizeButton.ToolTip = "Restore Down";
            }
            else
            {
                MaximizeButton.Content = "□"; // 최대화 아이콘 (네모)
                MaximizeButton.ToolTip = "Maximize";
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 창이 닫힐 때(Closing), ViewModel에게 "청소할 거 있으면 해라"고 신호를 줍니다.
            var vm = this.DataContext as MainViewModel;
            // MIL 라이브러리 메모리 해제 등을 수행
            vm?.Cleanup();
        }

        // 1. 이미지가 로드되면 화면 맞춤 실행
        private void ImgView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // "화면 그리는 일이 다 끝나고 한가해지면(ContextIdle), FitImageToScreen을 실행해라"
            // 즉시 실행하면 UI가 꼬일 수 있어서 안전하게 예약 실행.
            Dispatcher.InvokeAsync(() => FitImageToScreen(), DispatcherPriority.ContextIdle);
        }

        // 2. 창 크기가 변하면 화면 맞춤 실행
        private void ZoomBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (ImgView.Source != null) FitImageToScreen();
        }

        // 3. 프로그램 시작 시 화면 맞춤 실행
        private void ZoomBorder_Loaded(object sender, RoutedEventArgs e)
        {
            // ZoomBorder_Loaded 이벤트는 컨트롤이 막 로드 되는 시점에 발생.
            // 화면의 크기나 레이아웃 계산이 완벽하게 끝나지 않을 수있으므로, 
            // FitImageToScreen()을 즉시 실행하면 잘못된 크기로 계산되거나, 화면 갱신이 되지 않기 때문에
            // 화면 렌더링이 완전히 끝이난 후 안전한 타이밍에 이미지를 화면에 맞추기 위해 비동기적으로 
            // 화면 그리기나 다른 급한 일들이 모두 끝나고 UI 쓰레드가 한가할때 실행해 달라는 의미.
            Dispatcher.InvokeAsync(() => FitImageToScreen(), DispatcherPriority.ContextIdle);
        }


        // ---------------------------------------------------------
        // [화면 맞춤 함수] (디버깅 코드 제거됨)
        // 이미지를 화면 크기에 맞춰서(Fit), 여백을 조금 두고(95%), 정중앙에 배치.
        // 1. 현재 화면과 이미지 크기를 잽니다.
        // 2. 가로/세로 비율 중 더 많이 줄여야 하는 쪽을 기준으로 배율을 정합니다.
        // 3. 이미지가 화면보다 작으면 굳이 늘리지 않습니다.
        // 4. 보기 좋게 95% 크기로 살짝 줄입니다.
        // 5. 화면 정중아으로 이동.
        // ---------------------------------------------------------
        public void FitImageToScreen()
        {
            /*
             * 레이 아웃 강제 업데이트
             * WPF는 레이아웃 변경을 즉시 반영하지 않고 나중에 한꺼번에 처리하는 경우가 있습니다.
             * 하지만 지금 당장 정확한 화면 크기(ActualWidth/Height)를 알아야 하기 때문에,
             * 강제로 지금 당장 화면을 갱신해라고 명령하여 최신 크기 값을 가져옵니다.
             */
            ZoomBorder.UpdateLayout();
            ImgView.UpdateLayout();

            /*
             * [2] 예외 상황 체크 (방어 코드)
             * 1. 이미지가 아직 로드되지 않았거나 (ImgView.Source == null)
             * 2. 이미지를 감싸는 테두리(ZoomBorder)의 너비나 높이가 0이라면 (아직 화면에 안 떴거나 최소화된 상태)
             * 계산을 할 수 없으므로 그냥 함수를 종료합니다.
             */
            if (ImgView.Source == null || ZoomBorder.ActualWidth == 0 || ZoomBorder.ActualHeight == 0)
                return;

            // [3] 이미지 소스 가져오기
            // ImgView에 있는 이미지를 BitmapSource 형태로 가져옵니다.
            // 이렇게 해야 이미지의 실제 픽셀 크기(Width, Height)를 알 수 있습니다.
            var imageSource = ImgView.Source as BitmapSource;

            // [4] 이미지 유효성 체크
            // 이미지가 없거나 크기가 0인 비정상 이미지라면 함수를 종료합니다.
            if (imageSource == null || imageSource.Width == 0 || imageSource.Height == 0) return;

            // [5] 변환 초기화 (Reset)
            // 기존에 적용되어 있던 확대/이동 값을 모두 초기화합니다.
            // ScaleX, ScaleY = 1.0 (1배율, 원본 크기)
            // TranslateX, TranslateY = 0 (이동 없음, 원점)
            imgScale.ScaleX = 1.0;      // 배율 1배
            imgScale.ScaleY = 1.0;
            imgTranslate.X = 0;         // 위치 0,0
            imgTranslate.Y = 0;

            // [6] 배율 계산 (Scale Factor Calculation)
            // 화면 너비 대비 이미지 너비의 비율 (scaleX)
            // 화면 높이 대비 이미지 높이의 비율 (scaleY)
            // 예: 화면 1000px / 이미지 2000px = 0.5 (절반으로 줄여야 함)
            double scaleX = ZoomBorder.ActualWidth / imageSource.Width;
            double scaleY = ZoomBorder.ActualHeight / imageSource.Height;

            // [7] 최종 배율 결정 (Min 함수 사용)
            // 가로 비율과 세로 비율 중 '더 작은 값'을 선택합니다.
            // 그래야 이미지가 화면 밖으로 잘리지 않고 전체가 다 들어옵니다 (Letterboxing 방식).
            double scale = Math.Min(scaleX, scaleY);

            // [8] 확대 금지 (Optional)
            // 계산된 배율이 1.0보다 크다면(즉, 이미지가 화면보다 작아서 확대해야 한다면),
            // 억지로 늘리지 않고 1.0(원본 크기)으로 고정합니다. (이미지 깨짐 방지)
            if (scale > 1.0) scale = 1.0; // 확대 금지

            // [9] 여백 주기 (95%)
            // 화면에 너무 꽉 차면 답답해 보일 수 있으므로,
            // 계산된 크기의 95%만 사용하여 약간의 여백을 둡니다.
            imgScale.ScaleX = scale * 0.95;
            imgScale.ScaleY = scale * 0.95;

            // [10] 중앙 정렬을 위한 위치 계산
            // 최종적으로 줄어든 이미지의 너비와 높이를 계산합니다.
            double finalWidth = imageSource.Width * imgScale.ScaleX;
            double finalHeight = imageSource.Height * imgScale.ScaleY;

            // (화면 너비 - 이미지 너비) / 2 공식을 사용하여
            // 남은 여백의 절반만큼 이동시키면 정중앙에 위치하게 됩니다.
            imgTranslate.X = (ZoomBorder.ActualWidth - finalWidth) / 2;
            imgTranslate.Y = (ZoomBorder.ActualHeight - finalHeight) / 2;

            /*
            // [디버깅] 계산된 결과를 윈도우 제목에 표시 (성공 여부 확인용)
            this.Title = $"결과: W={imageSource.Width}, H={imageSource.Height}, " +
                         $"Border={ZoomBorder.ActualWidth:F0}x{ZoomBorder.ActualHeight:F0}, " +
                         $"Scale={scale:F4}, TransX={imgTranslate.X:F0}, TransY={imgTranslate.Y:F0}";
            */
        }

        // ---------------------------------------------------------
        // [마우스 조작] 줌 & 팬
        // ---------------------------------------------------------
        private void ZoomBorder_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (ImgView.Source == null) return;

            // 현재 마우스 위치를 기준으로 확대/축소하기 위해 좌표를 구합니다.
            Point p = e.GetPosition(ZoomBorder);

            // 휠을 올리면(Delta > 0) 1.2배 확대, 내리면 1.2배 축소
            double zoom = e.Delta > 0 ? 1.2 : (1.0 / 1.2);

            // 배율 적용
            imgScale.ScaleX *= zoom;
            imgScale.ScaleY *= zoom;

            // [위치 보정] 마우스 포인터 위치(p)가 줌 후에도 같은 곳에 있도록 이동시킵니다.
            // (이 수식은 줌 기능의 표준 공식입니다)
            imgTranslate.X = p.X - (p.X - imgTranslate.X) * zoom;
            imgTranslate.Y = p.Y - (p.Y - imgTranslate.Y) * zoom;
        }

        private void ZoomBorder_MouseMove(object sender, MouseEventArgs e)
        {

            // ROI 그리기 중일때
            if (_isRoiDrawing)
            {
                Point currentPos = e.GetPosition(ImgView);
                var bitmap = ImgView.Source as BitmapSource;

                // 이미지 범위 제한 (Clamp)
                if (currentPos.X < 0) currentPos.X = 0;
                if (currentPos.Y < 0) currentPos.Y = 0;
                if (currentPos.X > bitmap.PixelWidth) currentPos.X = bitmap.PixelWidth;
                if (currentPos.Y > bitmap.PixelHeight) currentPos.Y = bitmap.PixelHeight;

                // 시각적 사각형 업데이트
                // 시작점(_roiStartPoint)과 현재점(currentPos)으로 사각형을 업데이트
                UpdateRoiVisual(_roiStartPoint, currentPos);

            }
            else if (_currentDrawMode != DrawingMode.None && _tempShape != null && e.LeftButton == MouseButtonState.Pressed)
            {
                // 화면상의 마우스 좌표를 실제 원본 이미지의 픽셀 좌표로 변환하기 위해 e.GetPosition(ImgView) 사용.
                // 만약 e.GetPosition(this) or e.GetPosition(ZoomBorder)를 사용하면, 윈도우 기준 또는 테두리 기준이 되어
                // 이미지가 확대되었을 때 화면상의 좌표만 구해짐.
                // WPF 내부적으로 현재 적용된 확대비율과 이동거리를 역으로 계산해서, 사용자가 클릭한 곳이 원본 이미지의 몇 번째 픽셀인지
                // 정확한 좌표를 반환해 줌.
                Point currentPos = e.GetPosition(ImgView);  // 이미지 기준 좌표.

                if(_currentDrawMode == DrawingMode.Line)
                {
                    var line = _tempShape as Line;
                    line.X2 = currentPos.X;
                    line.Y2 = currentPos.Y;
                }
                else if(_currentDrawMode == DrawingMode.Circle || _currentDrawMode == DrawingMode.Rectangle)
                {
                    // 시작점과 현재점으로 Top-Left와 Width/Height 계산.
                    double x = Math.Min(_drawStartPoint.X, currentPos.X);
                    double y = Math.Min(_drawStartPoint.Y, currentPos.Y);
                    double w = Math.Abs(currentPos.X - _drawStartPoint.X);
                    double h = Math.Abs(currentPos.Y - _drawStartPoint.Y);

                    Canvas.SetLeft(_tempShape, x);
                    Canvas.SetTop(_tempShape, y);
                    _tempShape.Width = w;
                    _tempShape.Height = h;
                }
            }
            else if (_isDragging)       // 이미지 이동 중일 때
            {
                var border = sender as Border;
                Point v = e.GetPosition(border);    // 현재 마우스 위치

                // 이동 거리 = 현재위치(v) - 시작위치(_start)
                // 새 이미지 위치 = 원래위치(_origin) + 이동거리
                imgTranslate.X = _origin.X + (v.X - _start.X);
                imgTranslate.Y = _origin.Y + (v.Y - _start.Y);
            }

            // 마우스 좌표 표시 로직
            // MVVM 패턴에서 View(화면)가 ViewModel(데이터/로직)에 접근하기 위한 전형적인 코드.
            // this: 현재 코드(MainWindow.xaml.cs)가 속한 MainWindow 창 자체를 의미.
            // .DataContext: WPF의 핵심 속성으로 이 창(View)이 어떤 데이터 덩어리를 바라보고 있는지 저장하는 변수.
            //  MainWindow.xaml에서 <local:MainViewModel/> 로 설정해 두었기 때문에, 여기에는 MainViewModel 객체가 들어있음.
            //  하지만, 컴퓨터는 이변수를 구체적인 MainViewModel 이 아니라, 그냥 범용 객체(object)로 알고있으며,
            //  as MainViewModel: 형변환으로, 범용 객체인줄 알았는데, 알고보니 MainViewModel 이지? 그렇다면 MainViewModel 로 바꿔달라고 명령하는 것입니다.
            //  이렇게 해야 MainViewModel 안에 있는 속성들 (MouseCoordinationInfo, DisplayImage 등)을 코드에서 사용할수 있습니다.
            // 마지막으로 변환된 객체를 vm 이라는 변수에 담아둡니다.
            // 이코드가 없으면, MainViewModel 안에 있는 MouseCoordinationInfo (좌표문자열) 속성에 접근할 수 없습니다.
            // 마우스가 움직임(MouseMove) -> 좌표 계산 (View에서 함) -> "이 좌표 값을 화면에 띄워줘!" 하고 ViewModel에게 전달해야 함.
            var vm = this.DataContext as MainViewModel;
            if (vm != null)
            {
                // 이미지가 로드되어 있는지 확인.
                if (ImgView.Source is BitmapSource bitmap)
                {
                    // 이미지 컨트롤(ImgView) 기준의 좌표를 가져옴.
                    // RenderTransform(줌/이동)과 상관 없이 원본 이미지 기준의 픽셀 좌표를 구함.
                    Point p = e.GetPosition(ImgView);

                    int currentX = (int)p.X;
                    int currentY = (int)p.Y;

                    // 마우스가 이미지 영역 안에 있을때만 표시.
                    if (currentX >= 0 && currentX < bitmap.PixelWidth &&
                        currentY >= 0 && currentY < bitmap.PixelHeight)
                    {
                        vm.MouseCoordinationInfo = $"(X: {currentX}, Y: {currentY})";
                    }
                    else
                    {
                        // 이미지 영역 밖
                        vm.MouseCoordinationInfo = "(X: 0, Y: 0)";
                    }
                }
            }
        }

        private void ZoomBorder_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ImgView.Source == null) return;

            bool isClickedOnRoi = false;
            if(RoiRect.Visibility == Visibility.Visible && _currentRoiRect.Width > 0 && _currentRoiRect.Height > 0)
            {
                Point mousePt = e.GetPosition(ImgView);
                if(_currentRoiRect.Contains(mousePt))
                {
                    isClickedOnRoi = true;
                }
            }

            if (isClickedOnRoi)
            {
                return;
            }
            else
            {
                // this.FindResource("DrawContextMenu") as Context 코드는 WPF에서 리소스에 정의된 특정 객체를 코드에서 찾아오는 방법.
                // this : 현재 코드가 실행되고 있는 클래싀 인스턴스를 의미.
                // WPF 가 제공하는 메서드로, 내 부모님의 주머니를 뒤져서 특정 이름(key)를 가진 물건을 찾아와라.
                // FindResource 메서드는 어떤 객체를 찾을 지 모르기 때문에 가장 범용적인 object 형태로 변환합니다.
                // 만약 찾는다면, as ContextMenu는 이미 알고 있는 ContextMenu 타입으로 취급(형변환) 해달라는 것.
                ContextMenu menu = this.FindResource("DrawingContextMenu") as ContextMenu;
                //ContextMenu menu = this.Resources["DrawingContextMenu"] as ContextMenu;
                if (menu != null)
                {
                    // 우클릭 메뉴(ContextMenu)가 화면 어디에 나타날지 결정하는 기준 설정.
                    // PlacementTarget: 메뉴를 어디에 붙일 것인가 정하는 속성. (이 값을 설정하지 않으면 메뉴가 화면에 안보일수 있음)
                    //  이 값을 설정하면, 메뉴가 해당 컨트롤의 바로 옆이나 위에 나타나게 됨.
                    // sender : 이벤트를 발생시킨 주인공으로 우클릭을 당한 ZoomBorder가 됨.
                    //  sender 는 기본적으로 object 타입이라, 이것을 화면에 표시되는 요소인 UIElement 로 형변환 시켜 줌.
                    // "이 메뉴(menu)를 띄울때, 기준 위치를 지금 우클릭된 녀석(sender)으로 삼아라.
                    menu.PlacementTarget = sender as UIElement;
                    menu.IsOpen = true;
                }
                // 이벤트를 "여기서 처리 완료했다"고 표시하여, 더 이상 이벤트가 부모 요소나 다른 핸들러로 전파되는 것을 막는 역할.
                // "내가 의도한 팝업 메뉴를 띄웠으니, 더 이상 다른 우클릭 관련 동작은 하지 마라"**라고 시스템에 알리는 것
                e.Handled = true;
            }

            
            /*
            var vm = this.DataContext as MainViewModel;

            // 이미지가 없으면 무시
            if (ImgView.Source == null) return;

            // 1. ROI 모드인지 확인.
            bool isRoiMode = vm != null && vm.SelectedAlgorithm != null && vm.SelectedAlgorithm.Contains("ROI");

            if(isRoiMode)
            {
                FitImageToScreen();
                e.Handled = true; 
            }
            */

        }

        private void ZoomBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 왼쪽 버튼 클릭
            if (e.ChangedButton == MouseButton.Left && ImgView.Source != null)
            {
                Point mousePos = e.GetPosition(ImgView);
                var bitmap = ImgView.Source as BitmapSource;

                // [수정완료] 기존 vm.SelectedAlgorithm 체크 로직을 제거하고 DrawingMode로 통일

                // 1. ROI 그리기 모드
                if (_currentDrawMode == DrawingMode.Roi)
                {
                    // 이미지 내부인지 확인
                    if (mousePos.X >= 0 && mousePos.X < bitmap.PixelWidth && mousePos.Y >= 0 && mousePos.Y < bitmap.PixelHeight)
                    {
                        _isRoiDrawing = true;
                        _roiStartPoint = mousePos;

                        RoiRect.Visibility = Visibility.Visible;
                        RoiRect.Width = 0;
                        RoiRect.Height = 0;
                        UpdateRoiVisual(mousePos, mousePos);

                        /*
                         * Mouse 이벤트를 ImgCanvas 컨트롤이 독점하도록 강제.
                         * 그리기 프로그램이나, 드래그 기능에 필수적인 기능으로, 
                         * 상황: 사용자가 사각형(ROI)을 그리기 위해 마우스를 클릭한 상태로 드래그하다가, 실수로 이미지 영역 밖으로 마우스가 나가는 경우가 자주 발생합니다.
                         * 이 코드가 없다면: 마우스가 영역 밖으로 나가는 순간, ImgCanvas는 더 이상 "마우스가 움직이고 있다"는 사실을 모르게 됩니다. 
                         * 결과적으로 사각형 그리기가 뚝 끊기거나, 마우스 버튼을 떼도 그리기 모드가 종료되지 않는 버그가 발생합니다.
                         * 이 코드가 있으면: 마우스가 모니터 끝까지 가더라도 ImgCanvas는 계속해서 "아, 아직 드래그 중이구나"라고 인식하고 사각형 크기를 계속 조절할 수 있게 됩니다.
                         * 마우스를 납치했으면, 일이 끝났을 때 반드시 놓아줘야 합니다. 
                         * 그래서 MouseUp (마우스 버튼을 뗄 때) 이벤트에는 항상 ImgCavas.ReleaseMouseCapture(); 코드가 쌍으로 사용.
                         */
                        ImgCanvas.CaptureMouse();
                    }
                }
                // 2. 다른 도형 그리기 모드
                else if (_currentDrawMode != DrawingMode.None)
                {
                    _drawStartPoint = mousePos;

                    if (_currentDrawMode == DrawingMode.Line)
                    {
                        _tempShape = new Line
                        {
                            Stroke = Brushes.Yellow,
                            StrokeThickness = 2,
                            X1 = _drawStartPoint.X,
                            Y1 = _drawStartPoint.Y,
                            X2 = _drawStartPoint.X,
                            Y2 = _drawStartPoint.Y
                        };
                    }
                    else if (_currentDrawMode == DrawingMode.Circle)
                    {
                        _tempShape = new Ellipse
                        {
                            Stroke = Brushes.Lime,
                            StrokeThickness = 2,
                            Width = 0,
                            Height = 0
                        };
                        Canvas.SetLeft(_tempShape, _drawStartPoint.X);
                        Canvas.SetTop(_tempShape, _drawStartPoint.Y);
                    }
                    else if (_currentDrawMode == DrawingMode.Rectangle)
                    {
                        _tempShape = new Rectangle
                        {
                            Stroke = Brushes.Cyan,
                            StrokeThickness = 2,
                            Width = 0,
                            Height = 0
                        };
                        Canvas.SetLeft(_tempShape, _drawStartPoint.X);
                        Canvas.SetTop(_tempShape, _drawStartPoint.Y);
                    }

                    if (_tempShape != null)
                    {
                        OverlayCanvas.Children.Add(_tempShape);
                        ZoomBorder.CaptureMouse();
                    }
                }
            }
            // 가운데 버튼 (이동)
            else if (e.ChangedButton == MouseButton.Middle && ImgView.Source != null)
            {
                var border = sender as Border;
                border.CaptureMouse();
                _start = e.GetPosition(border);
                _origin = new Point(imgTranslate.X, imgTranslate.Y);
                _isDragging = true;
                Cursor = Cursors.SizeAll;
            }
        }

        private void ZoomBorder_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // ROI 그리기 종료
            if (_isRoiDrawing)
            {
                _isRoiDrawing = false;
                ImgCanvas.ReleaseMouseCapture();   // 납치했던 마우스 놓아줌 

                // 최종 ROI 좌표 저장 (나중에 자르기/저장 할 때 씀)
                // 정규화 (음수 크기 방지)
                double x = Math.Min(_roiStartPoint.X, RoiRect.Tag is Point p ? p.X : 0); // Tag에 끝점 저장했다고 가정하거나 다시 계산
                                                                                         // -> 단순화를 위해 UpdateRoiVisual에서 _currentRoiRect를 갱신하도록 함
            }

            // 도형 그리기
            if(_currentDrawMode != DrawingMode.None && _tempShape != null)
            {
                ZoomBorder.ReleaseMouseCapture();

                // 직선인 경우: 거리 계산 및 텍스트 표시
                if(_currentDrawMode == DrawingMode.Line && _tempShape is Line line)
                {
                    double dist = Math.Sqrt(Math.Pow(line.X2 - line.X1, 2) + Math.Pow(line.Y2 - line.Y1, 2));

                    // 텍스트블럭 생성
                    TextBlock tb = new TextBlock
                    {
                        Text = $"{dist:F1}px",
                        Foreground = Brushes.Yellow,
                        Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)), // 반투명 배경
                        Padding = new Thickness(2),
                        FontSize = 14,
                        FontWeight = FontWeights.Bold
                    };

                    // 위치 설정 (끝점)
                    Canvas.SetLeft(tb, line.X2);
                    Canvas.SetTop(tb, line.Y2);

                    OverlayCanvas.Children.Add(tb);
                }
                // 그리기 완료 후 상태 초기화 (연속 그리기를 원할 경우, 이 줄 제거)
                _currentDrawMode = DrawingMode.None;
                _tempShape = null;
                Cursor = Cursors.Arrow;

            }

            // [변경] 드래그 중이었고, 뗀 버튼이 가운데 버튼이라면 이동 종료
            if (_isDragging && e.ChangedButton == MouseButton.Middle)
            {
                var border = sender as Border;
                border.ReleaseMouseCapture();   // 마우스 놓아줌
                _isDragging = false;
                Cursor = Cursors.Arrow;         // 커서 모양 복구.
            }
        }

        // [수정됨] 좌표 변환 로직 수정
        // 시작점과 끝점 두 개를 받아서 "화면에 어떻게 그려야 할지" 계산하는 함수
        private void UpdateRoiVisual(Point start, Point end)
        {
            // 두 점 중 작은 값이 왼쪽/위쪽(X, Y), 차이값이 너비/높이(W, H)
            double x = Math.Min(start.X, end.X);
            double y = Math.Min(start.Y, end.Y);
            double w = Math.Abs(end.X - start.X);
            double h = Math.Abs(end.Y - start.Y);

            // 1. 논리적 ROI 데이터 저장 (이미지 기준 픽셀 - 나중에 자를 때 씀)
            _currentRoiRect = new Rect(x, y, w, h);

            // 2. 화면 표시용 좌표 변환 (Zoom/Pan 적용)
            // 이미지가 확대되어 있으면 사각형도 확대된 위치에 그려야 함
            // 공식: 이미지좌표 * 배율 + 이동거리
            double screenX = x * imgScale.ScaleX + imgTranslate.X;
            double screenY = y * imgScale.ScaleY + imgTranslate.Y;
            double screenW = w * imgScale.ScaleX;
            double screenH = h * imgScale.ScaleY;

            // [핵심 수정 사항]
            // 문제 원인: RoiRect.RenderTransform = ImgView.RenderTransform; 
            // 이 코드가 있으면 '화면 좌표'가 아니라 '이미지 좌표'를 넣어야 하는데, 
            // Canvas.Left 위치 계산과 Scale 적용 방식이 충돌하여 위치가 어긋납니다.

            // 해결책: RenderTransform을 공유하지 말고, 
            // 위에서 계산한 'screenX/Y/W/H' (최종 화면 좌표)를 직접 대입하는 것이 가장 정확하고 깔끔합니다.

            // [중요] 빨간 사각형(Rectangle) 속성 변경
            RoiRect.RenderTransform = null; // 중요: 기존 이미지의 Transform을 따라가지 않도록 해제 : 충돌 방지용 초기화

            RoiRect.Width = screenW;        // 계산된 화면 크기 적용
            RoiRect.Height = screenH;
            Canvas.SetLeft(RoiRect, screenX);   // 캔버스 위에서의 X 위치
            Canvas.SetTop(RoiRect, screenY);    // 캔버스 위에서의 Y 위치
        }

        private void MenuItem_Crop_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRoiRect.Width <= 0 || _currentRoiRect.Height <= 0) return;

            var vm = this.DataContext as MainViewModel;
            if (vm != null)
            {
                // 정수형으로 변환하여 전달
                vm.CropImage((int)_currentRoiRect.X, (int)_currentRoiRect.Y, (int)_currentRoiRect.Width, (int)_currentRoiRect.Height);

                // 잘라낸 후 사각형 숨기기
                RoiRect.Visibility = Visibility.Collapsed;
                // 뷰 리셋 (선택 사항)
                FitImageToScreen();     // 잘린 이미지에 맞춰 화면 갱신
            }
        }

        private void MenuItem_Save_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRoiRect.Width <= 0 || _currentRoiRect.Height <= 0) return;

            var vm = this.DataContext as MainViewModel;
            if (vm != null)
            {
                Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog();
                dlg.Filter = "Bitmap (*.bmp)|*.bmp|JPEG (*.jpg)|*.jpg|PNG (*.png)|*.png";
                dlg.FileName = "ROI_Image";

                if (dlg.ShowDialog() == true)
                {
                    vm.SaveRoiImage(dlg.FileName, (int)_currentRoiRect.X, (int)_currentRoiRect.Y, (int)_currentRoiRect.Width, (int)_currentRoiRect.Height);

                    // [추가] 저장이 완료되면 사각형을 숨깁니다.
                    RoiRect.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void Menu_DrawLine_Click(object sender, RoutedEventArgs e)
        {
            _currentDrawMode = DrawingMode.Line;
            Cursor = Cursors.Pen;
        }

        private void Menu_DrawCircle_Click(object sender, RoutedEventArgs e)
        {
            _currentDrawMode = DrawingMode.Circle;
            Cursor = Cursors.Cross;
        }

        private void Menu_DrawRect_Click(object sender, RoutedEventArgs e)
        {
            _currentDrawMode = DrawingMode.Rectangle;
            Cursor = Cursors.Cross;
        }

        private void Menu_Clear_Click(object sender, RoutedEventArgs e)
        {
            // 그려진 모든 도형 삭제
            OverlayCanvas.Children.Clear();
            _currentDrawMode = DrawingMode.None;
            Cursor = Cursors.Arrow;
        }

        private void Menu_Fit_Click(object sender, RoutedEventArgs e)
        {
            FitImageToScreen();
        }

        private void Menu_DrawRoi_Click(object sender, RoutedEventArgs e)
        {
            _currentDrawMode = DrawingMode.Roi;
            Cursor = Cursors.Cross;
        }

        
    }
}
