using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;

namespace IndyVision
{
    public class OpenCVService
    {
        // create Image instance
        private Mat _srcImage;  // 원본
        private Mat _destImage;  // 처리용

        // 화면 표시용 캐시
        // 화면(UI)에 보여주기 위한 WPF 전용 이미지 객체 (캐시)
        private ImageSource _cachedOriginal;
        private ImageSource _cachedProcessed;

        // GMF(패턴 매칭) 모델 이미지
        private Mat _modelImage;

        // 현재 모델을 등록/설정 중인지 여부를 나타내는 플래그
        public bool IsModelDefinitionMode { get; private set; } = false;


        public OpenCVService()
        { 
            // MIL과 달리 별도의 System 할당이 필요 없음.
        }

        // 이미지 로드
        public void LoadImage(string filePath)
        {
            // 메모리 정리
            CleanupImages();

            // 1. 이미지 읽기 (컬로로 읽기: ImageMode.Color)
            // MIL의 MIL.MbufRestore
            _srcImage = Cv2.ImRead(filePath, ImreadModes.Color);

            if(_srcImage.Empty())
                throw new Exception("이미지를 불러올 수 없습니다.");

            // 2. 작업용 사본 생성.
            _destImage = _srcImage.Clone();

            // 3. 화면 표시용 비트맵 생성
            // ToWriteableBitmap(): OpenCvSharp.WpfExtensions에 있는 기능으로, 
            // OpenCV의 Mat 데이터를 WPF Image 컨트롤이 이해할 수 있는 포맷으로 변환합니다.
            _cachedOriginal = _srcImage.ToWriteableBitmap();
            _cachedProcessed = _destImage.ToWriteableBitmap();

        }

        public string ProcessImage(string algorithm, AlgorithmParamsBase parameters)
        {

            if (_srcImage == null || _srcImage.IsDisposed) return "이미지 없음";

            string resultMessage = "Processing Complete";

            //항상 원본이미지에서 시작 (누적 처리 방지)
            if(_destImage != null) _destImage.Dispose();
            _destImage = _srcImage.Clone();

            // 알고리즘 처리
            switch(algorithm)
            {
                case "Gray 처리":
                    if (_srcImage.Channels() == 1) return "This Image is Gray Image.";
                    else
                    {
                        Cv2.CvtColor(_srcImage, _destImage, ColorConversionCodes.BGR2GRAY);
                        resultMessage = "Image Convert Completed to Gray-Image";
                    }
                    break;
                case "Threshold (이진화)":
                    if(parameters is ThresholdParams thParams)
                    {
                        // 컬러 -> 그레이 스케일 변환 필수
                        // using 블록: gray 변수는 이 블록이 끝나면 즉시 메모리 해제됨
                        using (Mat gray = new Mat())
                        {
                            Cv2.CvtColor(_srcImage, gray, ColorConversionCodes.BGR2GRAY);

                            // MIL: MimBinarize(Range) -> OpenCV: InRange 또는 Threshold
                            // 여기서는 범위 이진화를 위해 InRange 사용
                            // 스칼라 값: (Lower, Upper)
                            // Cv2.InRange(입력, 하한값, 상한값, 출력)
                            // - 픽셀값이 하한(ThresholdValue) ~ 상한(ThresholdMax) 사이면 흰색(255), 아니면 검은색(0)으로 만듭니다.
                            // - 특정 밝기 영역만 추출할 때 유용합니다.
                            Cv2.InRange(gray, new Scalar(thParams.ThresholdValue), new Scalar(thParams.ThresholdMax), _destImage);
                        }
                    }
                    break;

                case "Morphology (모폴로지)":
                    if (parameters is MorphologyParams morParams)
                    {
                        // 이진화 선행 (Otsu 알고리즘으로 자동 이진화 예시)
                        using (Mat gray = new Mat())
                        {
                            Cv2.CvtColor(_srcImage, gray, ColorConversionCodes.BGR2GRAY);
                            Cv2.Threshold(gray, _destImage, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                        }

                        // 커널 모양 생성 (사각형)
                        MorphShapes shape = MorphShapes.Rect;
                        // KernelSize는 홀수여야 함
                        int kSize = morParams.KernelSize % 2 == 0 ? morParams.KernelSize + 1 : morParams.KernelSize;

                        using (Mat kernel = Cv2.GetStructuringElement(shape, new Size(kSize, kSize)))
                        {
                            MorphTypes morType = MorphTypes.Erode;
                            if (morParams.OperationMode == "Dilate") morType = MorphTypes.Dilate;
                            else if (morParams.OperationMode == "Open") morType = MorphTypes.Open;
                            else if (morParams.OperationMode == "Close") morType = MorphTypes.Close;

                            // 반복 횟수 적용
                            Cv2.MorphologyEx(_destImage, _destImage, morType, kernel, iterations: morParams.Iterations);
                        }
                    }
                    break;

                case "Edge Detection (엣지 검출)":
                    if (parameters is EdgeParams edgeParams)
                    {
                        using (Mat gray = new Mat())
                        {
                            Cv2.CvtColor(_srcImage, gray, ColorConversionCodes.BGR2GRAY);

                            if (edgeParams.Method.Contains("Laplacian"))
                            {
                                Cv2.Laplacian(gray, _destImage, MatType.CV_8U, ksize: 3);
                            }
                            else // Sobel (Prewitt는 OpenCV 기본 함수에 없으므로 Sobel로 대체)
                            {
                                // using 블록은 **"이 블록 안에서만 이 객체를 사용하고, 블록이 끝나면 즉시 폐기(Dispose)해라"**라는 의미
                                // 이미지 처리용 임시 변수(Mat)들은 용량이 크기 때문에,
                                // using을 사용하여 **"다 썼으면 바로바로 버려라"**라고 명시하는 것이며,
                                // 여러 줄을 겹쳐 쓰는 것은 코드를 깔끔하게 유지하면서 한꺼번에 관리하기 위함
                                // 소벨: 1차 미분을 이용. X축, Y축 각각 구해서 합침.
                                using (Mat gradX = new Mat())
                                using (Mat gradY = new Mat())
                                using (Mat absX = new Mat())
                                using (Mat absY = new Mat())
                                {
                                    // X, Y 방향 미분
                                    // 1. X방향 미분(dx = 1, dy = 0)
                                    // MatType.CV_16S: 미분 값은 음수가 나올 수 있으므로 16비트 정수형(Signed)을 사용해야 함.
                                    // 그냥 8비트를 쓰면 음수 값이 0으로 잘려서 정보가 손실됨.
                                    Cv2.Sobel(gray, gradX, MatType.CV_16S, 1, 0, 3);
                                    Cv2.Sobel(gray, gradY, MatType.CV_16S, 0, 1, 3);

                                    // **"계산용 데이터(음수 포함 16비트)를 눈으로 볼 수 있는 그림(양수 전용 8비트)으로 변환"**하여,
                                    // "변화의 방향(양/음)은 무시하고 변화의 강도(엣지)만 남기는" 필수적인 과정
                                    Cv2.ConvertScaleAbs(gradX, absX);
                                    Cv2.ConvertScaleAbs(gradY, absY);

                                    // 4. 두 방향 합치기 (가중치 0.5씩 줘서 평균)
                                    Cv2.AddWeighted(absX, 0.5, absY, 0.5, 0, _destImage);
                                }
                            }
                        }

                        // 엣지 강도 필터링 (Smoothness 활용)
                        // 사용자가 설정한 부드러움(Smoothness) 값보다 약한 엣지는 지웁니다.
                        if (edgeParams.Smoothness > 0)
                        {
                            Cv2.Threshold(_destImage, _destImage, edgeParams.Smoothness, 255, ThresholdTypes.Binary);
                        }
                    }
                    break;

                case "Adaptive Threshold (적응형 이진화)":
                    if (parameters is AdaptiveThresholdParams adaptParams)
                    {
                        using (Mat gray = new Mat())
                        {
                            Cv2.CvtColor(_srcImage, gray, ColorConversionCodes.BGR2GRAY);

                            // 적응형 방식: 평균(MeanC) 또는 가우시안(GaussianC)
                            AdaptiveThresholdTypes adaptType = AdaptiveThresholdTypes.MeanC; // 또는 GaussianC
                            // 모드: 밝은 물체를 찾을지(Binary), 어두운 물체를 찾을지(BinaryInv)
                            ThresholdTypes thType = adaptParams.Mode.Contains("Bright") ? ThresholdTypes.Binary : ThresholdTypes.BinaryInv;

                            // BlockSize는 반드시 홀수
                            int blockSize = adaptParams.WindowSize % 2 == 0 ? adaptParams.WindowSize + 1 : adaptParams.WindowSize;
                            if (blockSize < 3) blockSize = 3;

                            // Cv2.AdaptiveThreshold(입력, 출력, 최대값, 적응형메서드, 임계타입, 블록크기, 상수C)
                            // - 이미지를 작은 블록으로 나누어 각 구역마다 다른 임계값을 적용합니다.
                            // - 조명이 불균일한 이미지에서 효과적입니다.
                            Cv2.AdaptiveThreshold(gray, _destImage, 255, adaptType, thType, blockSize, adaptParams.Offset);
                        }
                    }
                    break;

                case "Blob Analysis (블롭 분석)":
                    if (parameters is BlobParams blobParams)
                    {
                        // [수정됨] 안전한 자원 해제를 위해 모든 Mat 객체를 using으로 감쌉니다.
                        // 이렇게 하면 에러가 발생해도 using 블록을 나갈 때 자동으로 Dispose()가 호출되어 메모리 누수를 방지합니다.
                        using (Mat labels = new Mat())      // 각 픽셀의 라벨 번호가 저장될 행렬
                        using (Mat stats = new Mat())       // 각 덩어리의 통계 정보 (x, y, w, h, 면적), stats: [x, y, width, height, area]
                        using (Mat centroids = new Mat())   // 각 덩어리의 중심 좌표 (cx, cy)
                        using (Mat binary = new Mat())
                        {

                            // 1. 전처리: 이진화
                            //Mat binary = new Mat();
                            using (Mat gray = new Mat())
                            {
                                Cv2.CvtColor(_srcImage, gray, ColorConversionCodes.BGR2GRAY);
                                Cv2.InRange(gray, new Scalar(blobParams.ThresholdMin), new Scalar(blobParams.ThresholdMax), binary);

                                // 체크박스가 켜져 있다면(Invert == true), 흰색과 검은색을 뒤집습니다.
                                // 예: 흰 배경에 검은 글씨 -> 배경을 잡고 반전시키면 글씨가 흰색이 되어 검출됨.
                                if (blobParams.Invert)
                                {
                                    Cv2.BitwiseNot(binary, binary);
                                }
                            }

                            // 2. 레이블링 (Connected Components)
                            // stats: [x, y, width, height, area]
                            // centroids: [cx, cy]
                            //Mat labels = new Mat();     // 각 픽셀의 라벨 번호가 저장될 행렬
                            //Mat stats = new Mat();      // 각 덩어리의 통계 정보 (x, y, w, h, 면적)
                            //Mat centroids = new Mat();  // 각 덩어리의 중심 좌표 (cx, cy)

                            // 반환값: 찾은 라벨(덩어리)의 총 개수 (배경 포함)
                            // 이진화된 이미지에서 연결된 객체(Blob)를 찾아 라벨링하고,
                            // 각 객체의 통계 정보(위치, 크기, 면적)와 중심점을 한 번에 계산해주는 매우 유용한 함수.
                            /*
                             int labelCount = Cv2.ConnectedComponentsWithStats(
                                            InputArray image,       // 입력 이미지 (binary)
                                            OutputArray labels,     // 라벨맵 출력 (labels)
                                            OutputArray stats,      // 통계 정보 출력 (stats)
                                            OutputArray centroids,  // 중심점 출력 (centroids)
                                            PixelConnectivity connectivity = PixelConnectivity.Connectivity8, // (기본값) 연결성
                                            int ltype = MatType.CV_32S // (기본값) 라벨 자료형
                                            );
                             */
                            int labelCount = Cv2.ConnectedComponentsWithStats(binary, labels, stats, centroids);

                            // 3. 결과 그리기 (컬러 변환)
                            Cv2.CvtColor(binary, _destImage, ColorConversionCodes.GRAY2BGR);

                            int validCount = 0;
                            // 라벨 0은 배경이므로 1부터 시작
                            for (int i = 1; i < labelCount; i++)
                            {
                                int area = stats.At<int>(i, 4); // stats 행렬에서 면적(Area) 정보 가져오기 [컬럼 인덱스 4]
                                if (area >= blobParams.MinArea)
                                {
                                    validCount++;
                                    if (blobParams.DrawBox)
                                    {
                                        int x = stats.At<int>(i, 0);    // Left
                                        int y = stats.At<int>(i, 1);    // Top
                                        int w = stats.At<int>(i, 2);    // Width
                                        int h = stats.At<int>(i, 3);    // Height

                                        // 빨간 박스
                                        Cv2.Rectangle(_destImage, new Rect(x, y, w, h), Scalar.Red, 2);

                                        // 파란 점 (중심)
                                        // 중심점(Centroid) 가져오기
                                        int cx = (int)centroids.At<double>(i, 0);   // X 좌표
                                        int cy = (int)centroids.At<double>(i, 1);   // Y 좌표
                                        Cv2.Circle(_destImage, cx, cy, 3, Scalar.Blue, -1);

                                        // 텍스트
                                        Cv2.PutText(_destImage, $"A:{area}", new Point(x, y - 5), HersheyFonts.HersheySimplex, 0.5, Scalar.Green, 1);
                                    }
                                }
                            }

                            resultMessage = $"검출 성공: {validCount}개 (전체 {labelCount - 1}개 중)";
                        }

                        // 4. using 블록 끝나면 자동 Dispose()
                        //binary.Dispose();
                        //labels.Dispose();
                        //stats.Dispose();
                        //centroids.Dispose();
                    }
                    break;

                case "TemplateMatching (TM)":
                    if(parameters is TemplateMatchParams tmParams)
                    {
                        if (IsModelDefinitionMode) return "모델 정의 모드입니다";
                        if (_modelImage == null) return "모델이미지가 없습니다.";

                        using (Mat result = new Mat())
                        using (Mat graySrc = new Mat())
                        using (Mat grayModel = new Mat())
                        {
                            Cv2.CvtColor(_srcImage, graySrc, ColorConversionCodes.BGR2GRAY);

                            // 모델이 컬러라면 흑백 변환
                            if (_modelImage.Channels() == 3)
                                Cv2.CvtColor(_modelImage, grayModel, ColorConversionCodes.BGR2GRAY);
                            else
                                _modelImage.CopyTo(grayModel);

                            // 매칭 (NCC Normed)
                            Cv2.MatchTemplate(graySrc, grayModel, result, TemplateMatchModes.CCoeffNormed);

                            // 2. 결과 임계값 처리 (MinScore)
                            // OpenCV 결과는 0.0 ~ 1.0, GMF Params는 0 ~ 100
                            double threshold = tmParams.MinScore / 100.0;

                            // 결과를 컬러에 그리기 위해 복사
                            _srcImage.CopyTo(_destImage);

                            int foundCount = 0;

                            // "모두 찾기" 로직 (임계값 넘는 모든 위치 찾기)
                            while (true)
                            {
                                double minVal, maxVal;
                                Point minLoc, maxLoc;
                                Cv2.MinMaxLoc(result, out minVal, out maxVal, out minLoc, out maxLoc);

                                // [종료 조건 1] 점수가 임계값보다 낮으면 종료
                                if (maxVal < threshold)
                                    break;

                                // [종료 조건 2] 찾은 개수 제한
                                foundCount++;
                                if (!tmParams.FindAllOccurrences && foundCount > tmParams.MaxOccurrences)
                                    break;

                                // 3. 결과 그리기 (박스 표시)
                                Cv2.Rectangle(_destImage, new Rect(maxLoc.X, maxLoc.Y, _modelImage.Width, _modelImage.Height), Scalar.Red, 2);
                                Cv2.PutText(_destImage, $"{maxVal * 100:F1}%", new Point(maxLoc.X, maxLoc.Y - 5), HersheyFonts.HersheyScriptSimplex, 0.5, Scalar.YellowGreen, 1);

                                // [추가] 중심점 계산
                                int centerX = maxLoc.X + (_modelImage.Width / 2);
                                int centerY = maxLoc.Y + (_modelImage.Height / 2);

                                // 중심점에 점 그리기 (반지름: 2, 채우기 옵션: -1)
                                Cv2.Circle(_destImage, centerX, centerY, 2, Scalar.Red, -1);

                                // [추가] 하단: 중심 좌표 표기
                                string coordText = $"(X: {centerX}, Y: {centerY})";
                                Cv2.PutText(_destImage, coordText, new Point(maxLoc.X, maxLoc.Y + _modelImage.Height + 15), HersheyFonts.HersheySimplex, 0.5, Scalar.Lime, 1);

                                // 4. [핵심 수정] 중복 검출 방지 (Non-Maximum Suppression)
                                //    찾은 위치(maxLoc)를 '중심'으로 하여 템플릿 크기만큼의 영역을 지웁니다.
                                //    이전 코드: maxLoc 부터 시작 (오른쪽/아래만 지움 -> 왼쪽/위쪽 점수 남음 -> 무한루프)
                                //    수정 코드: maxLoc 주변(좌우상하)을 모두 포함하도록 마스크 영역 설정

                                // 마스킹할 영역의 좌상단 좌표 계산 (모델 크기의 절반만큼 뒤로 이동)
                                int maskX = maxLoc.X - _modelImage.Width / 2;
                                int maskY = maxLoc.Y - _modelImage.Height / 2;

                                // 마스킹 영역 설정 (모델 크기만큼)
                                Rect maskRect = new Rect(maskX, maskY, _modelImage.Width, _modelImage.Height);

                                // result 행렬의 전체 크기
                                Rect resultBounds = new Rect(0, 0, result.Width, result.Height);

                                // 교집합 계산 (이미지 밖으로 나가는 좌표 자동 잘림 처리)
                                // 왼쪽/위쪽 음수 좌표나 오른쪽/아래쪽 초과 좌표가 안전하게 처리됩니다.
                                Rect clippedRect = resultBounds.Intersect(maskRect);

                                if (clippedRect.Width > 0 && clippedRect.Height > 0)
                                {
                                    // 해당 영역을 -1.0(최소값)으로 채움 -> 다음 MinMaxLoc에서 검색 제외됨
                                    Cv2.Rectangle(result, clippedRect, Scalar.All(-1.0), -1);
                                }
                                else
                                {
                                    // 만약 영역 계산에 실패했다면 강제로 해당 픽셀만이라도 지워서 무한루프 방지
                                    result.Set(maxLoc.Y, maxLoc.X, -1.0f);
                                }
                            }
                            resultMessage = $"매칭 성공: {foundCount}개 (Score >= {tmParams.MinScore}%)";
                        }
                    }
                    break;

                case "Geometric Model Finder (GMF)":
                    // *참고*: MIL GMF(Geometric Model Finder)는 벡터 기반이지만, 
                    // OpenCV 기본 기능에서는 Template Matching(픽셀 기반)이 가장 유사합니다.
                    // 회전/크기 불변성을 완벽히 구현하려면 Feature Matching(SIFT/ORB)이 필요하지만, 
                    // 여기서는 Template Matching으로 구현합니다.
                    if (parameters is GmfParams gmfParams)
                    {
                        if (IsModelDefinitionMode) return "모델 정의 모드입니다. Train Model을 누르세요.";
                        if (_modelImage == null) return "모델 이미지가 없습니다.";

                        // 1. 템플릿 매칭 수행
                        using (Mat result = new Mat())
                        using (Mat graySrc = new Mat())
                        using (Mat grayModel = new Mat())
                        {
                            Cv2.CvtColor(_srcImage, graySrc, ColorConversionCodes.BGR2GRAY);

                            // 모델이 컬러라면 흑백 변환
                            if (_modelImage.Channels() == 3)
                                Cv2.CvtColor(_modelImage, grayModel, ColorConversionCodes.BGR2GRAY);
                            else
                                _modelImage.CopyTo(grayModel);

                            // 매칭 (NCC Normed)
                            Cv2.MatchTemplate(graySrc, grayModel, result, TemplateMatchModes.CCoeffNormed);

                            // 2. 결과 임계값 처리 (MinScore)
                            // OpenCV 결과는 0.0 ~ 1.0, GMF Params는 0 ~ 100
                            double threshold = gmfParams.MinScore / 100.0;

                            // 결과를 컬러에 그리기 위해 복사
                            _srcImage.CopyTo(_destImage);

                            int foundCount = 0;

                            // "모두 찾기" 로직 (임계값 넘는 모든 위치 찾기)
                            while (true)
                            {
                                double minVal, maxVal;
                                Point minLoc, maxLoc;
                                Cv2.MinMaxLoc(result, out minVal, out maxVal, out minLoc, out maxLoc);

                                // [종료 조건 1] 점수가 임계값보다 낮으면 종료
                                if (maxVal < threshold)
                                    break;

                                // [종료 조건 2] 찾은 개수 제한
                                foundCount++;
                                if (!gmfParams.FindAllOccurrences && foundCount > gmfParams.MaxOccurrences)
                                    break;

                                // 3. 결과 그리기 (박스 표시)
                                Cv2.Rectangle(_destImage, new Rect(maxLoc.X, maxLoc.Y, _modelImage.Width, _modelImage.Height), Scalar.Red, 2);
                                Cv2.PutText(_destImage, $"{maxVal * 100:F1}%", new Point(maxLoc.X, maxLoc.Y - 5), HersheyFonts.HersheySimplex, 0.5, Scalar.Blue, 1);

                                // 4. [핵심 수정] 중복 검출 방지 (Non-Maximum Suppression)
                                //    찾은 위치(maxLoc)를 '중심'으로 하여 템플릿 크기만큼의 영역을 지웁니다.
                                //    이전 코드: maxLoc 부터 시작 (오른쪽/아래만 지움 -> 왼쪽/위쪽 점수 남음 -> 무한루프)
                                //    수정 코드: maxLoc 주변(좌우상하)을 모두 포함하도록 마스크 영역 설정

                                // 마스킹할 영역의 좌상단 좌표 계산 (모델 크기의 절반만큼 뒤로 이동)
                                int maskX = maxLoc.X - _modelImage.Width / 2;
                                int maskY = maxLoc.Y - _modelImage.Height / 2;

                                // 마스킹 영역 설정 (모델 크기만큼)
                                Rect maskRect = new Rect(maskX, maskY, _modelImage.Width, _modelImage.Height);

                                // result 행렬의 전체 크기
                                Rect resultBounds = new Rect(0, 0, result.Width, result.Height);

                                // 교집합 계산 (이미지 밖으로 나가는 좌표 자동 잘림 처리)
                                // 왼쪽/위쪽 음수 좌표나 오른쪽/아래쪽 초과 좌표가 안전하게 처리됩니다.
                                Rect clippedRect = resultBounds.Intersect(maskRect);

                                if (clippedRect.Width > 0 && clippedRect.Height > 0)
                                {
                                    // 해당 영역을 -1.0(최소값)으로 채움 -> 다음 MinMaxLoc에서 검색 제외됨
                                    Cv2.Rectangle(result, clippedRect, Scalar.All(-1.0), -1);
                                }
                                else
                                {
                                    // 만약 영역 계산에 실패했다면 강제로 해당 픽셀만이라도 지워서 무한루프 방지
                                    result.Set(maxLoc.Y, maxLoc.X, -1.0f);
                                }
                            }
                            resultMessage = $"매칭 성공: {foundCount}개 (Score >= {gmfParams.MinScore}%)";
                        }
                    }
                    break;
            }

            // 결과 캐싱
            _cachedProcessed = _destImage.ToWriteableBitmap();
            return resultMessage;
        }

        // ROI 저장
        public void SaveRoiImage(string filePath, int x, int y, int w, int h)
        {
            if (_srcImage == null) return;
            Rect roi = new Rect(x, y, w, h);

            // 이미지 범위 체크
            if (roi.X < 0) roi.X = 0;
            if (roi.Y < 0) roi.Y = 0;
            if (roi.X + roi.Width > _srcImage.Width) roi.Width = _srcImage.Width - roi.X;
            if (roi.Y + roi.Height > _srcImage.Height) roi.Height = _srcImage.Height - roi.Y;

            using (Mat roiMat = new Mat(_srcImage, roi))
            {
                roiMat.SaveImage(filePath);
            }
        }

        public void CropImage(int x, int y, int w, int h)
        {
            if (_srcImage == null) return;
            Rect roi = new Rect(x, y, w, h);

            // 새 이미지를 ROI로 교체
            Mat newSrc = new Mat(_srcImage, roi).Clone();

            _srcImage.Dispose();
            _srcImage = newSrc;

            // Dest 초기화
            if (_destImage != null) _destImage.Dispose();
            _destImage = _srcImage.Clone();

            _cachedOriginal = _srcImage.ToWriteableBitmap();
            _cachedProcessed = _destImage.ToWriteableBitmap();
        }

        // GMF 모델 로드
        //public void LoadGmfModelImage(string filePath)
        public void LoadModelImage(string filePath)
        {
            if (_modelImage != null) _modelImage.Dispose();
            _modelImage = Cv2.ImRead(filePath, ImreadModes.Color);

            IsModelDefinitionMode = true;

            // 화면에 모델 보여주기 위해 Dest에 복사
            if (_destImage != null) _destImage.Dispose();
            _destImage = _modelImage.Clone();
            _cachedProcessed = _destImage.ToWriteableBitmap();
        }

        // GMF 미리보기 (여기서는 모델의 Canny Edge 보여주기)
        //public void PreviewGmfModel(GmfParams param)
        //public void PreviewGmfModel(object param)
        public void PreviewModel(object param)
        {
            if (_modelImage == null) return;

            if (_destImage != null) _destImage.Dispose();
            _destImage = new Mat();

            if (_modelImage.Channels() == 1)
                Cv2.CvtColor(_modelImage, _destImage, ColorConversionCodes.GRAY2BGR);
            else 
                _modelImage.CopyTo(_destImage);


            using (Mat gray = new Mat())
            using (Mat edges = new Mat())
            {
                if(_modelImage.Channels() == 3)
                    Cv2.CvtColor(_modelImage, gray, ColorConversionCodes.BGR2GRAY);
                else
                    _modelImage.CopyTo(gray);

                double thresh1 = 0;
                double thresh2 = 0;

                /*
                 * Original Code:
                if (param is GmfParams)
                {
                    GmfParams Gmp = param as GmfParams;
                    thresh1 = Gmp.Smoothness;
                    thresh2 = Gmp.Smoothness * 2;
                }
                else if (param is TemplateMatchParams)
                {
                    TemplateMatchParams Tmp = param as TemplateMatchParams;
                    thresh1 = Tmp.Smoothness;
                    thresh2 = Tmp.Smoothness * 2;
                }
                */

                // 파라미터 타입에 따라 임계값 설정
                // C# 패턴 매칭에 따른 변수 선언 및 할당
                double smoothness = 0;
                if (param is GmfParams gmf) smoothness = gmf.Smoothness;
                else if (param is TemplateMatchParams tm) smoothness = tm.Smoothness;

                thresh1 = smoothness;
                thresh2 = smoothness * 2;

                // Canny Edge
                //double thresh1 = param.Smoothness;
                //double thresh2 = param.Smoothness * 2;
                Cv2.Canny(gray, edges, thresh1, thresh2);

                _destImage.SetTo(Scalar.Lime, edges);
            }

                _cachedProcessed = _destImage.ToWriteableBitmap();
        }

        //public void TrainGmfModel(GmfParams param)
        //public void TrainGmfModel(object param)
        public void TrainModel(object param)
        {
            // OpenCV Template Matching은 별도의 학습(Training) 과정이 필요 없으므로
            // 모드만 종료하고 원본 화면으로 복귀
            IsModelDefinitionMode = false;

            // 화면 복구
            if (_destImage != null) _destImage.Dispose();
            _destImage = _srcImage.Clone();
            _cachedProcessed = _destImage.ToWriteableBitmap();
        }

        public ImageSource GetOriginalImage() => _cachedOriginal;
        public ImageSource GetProcessedImage() => _cachedProcessed;

        private void CleanupImages()
        {
            if (_srcImage != null) { _srcImage.Dispose(); _srcImage = null; }
            if (_destImage != null) { _destImage.Dispose(); _destImage = null; }
            // 캐시 이미지는 WPF가 관리하도록 둠 (또는 null 처리)
        }

        public void Cleanup()
        {
            CleanupImages();
            if (_modelImage != null) _modelImage.Dispose();
        }


    }
}
