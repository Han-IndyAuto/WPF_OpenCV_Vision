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
        private ImageSource _cachedOriginal;
        private ImageSource _cachedProcessed;

        // GMF(패턴 매칭) 모델 이미지
        private Mat _modelImage;
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
                case "Threshold (이진화)":
                    if(parameters is ThresholdParams thParams)
                    {
                        // 컬러 -> 그레이 스케일 변환 필수
                        using(Mat gray = new Mat())
                        {
                            Cv2.CvtColor(_srcImage, gray, ColorConversionCodes.BGR2GRAY);
                            // MIL: MimBinarize(Range) -> OpenCV: InRange 또는 Threshold
                            // 여기서는 범위 이진화를 위해 InRange 사용
                            // 스칼라 값: (Lower, Upper)
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
                                using (Mat gradX = new Mat())
                                using (Mat gradY = new Mat())
                                using (Mat absX = new Mat())
                                using (Mat absY = new Mat())
                                {
                                    // X, Y 방향 미분
                                    Cv2.Sobel(gray, gradX, MatType.CV_16S, 1, 0, 3);
                                    Cv2.Sobel(gray, gradY, MatType.CV_16S, 0, 1, 3);

                                    Cv2.ConvertScaleAbs(gradX, absX);
                                    Cv2.ConvertScaleAbs(gradY, absY);

                                    Cv2.AddWeighted(absX, 0.5, absY, 0.5, 0, _destImage);
                                }
                            }
                        }

                        // 엣지 강도 필터링 (Smoothness 활용)
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

                            AdaptiveThresholdTypes adaptType = AdaptiveThresholdTypes.MeanC; // 또는 GaussianC
                            ThresholdTypes thType = adaptParams.Mode.Contains("Bright") ? ThresholdTypes.Binary : ThresholdTypes.BinaryInv;

                            // BlockSize는 반드시 홀수
                            int blockSize = adaptParams.WindowSize % 2 == 0 ? adaptParams.WindowSize + 1 : adaptParams.WindowSize;
                            if (blockSize < 3) blockSize = 3;

                            Cv2.AdaptiveThreshold(gray, _destImage, 255, adaptType, thType, blockSize, adaptParams.Offset);
                        }
                    }
                    break;

                case "Blob Analysis (블롭 분석)":
                    if (parameters is BlobParams blobParams)
                    {
                        // 1. 전처리: 이진화
                        Mat binary = new Mat();
                        using (Mat gray = new Mat())
                        {
                            Cv2.CvtColor(_srcImage, gray, ColorConversionCodes.BGR2GRAY);
                            Cv2.InRange(gray, new Scalar(blobParams.ThresholdMin), new Scalar(blobParams.ThresholdMax), binary);
                        }

                        // 2. 레이블링 (Connected Components)
                        // stats: [x, y, width, height, area]
                        // centroids: [cx, cy]
                        Mat labels = new Mat();
                        Mat stats = new Mat();
                        Mat centroids = new Mat();

                        int labelCount = Cv2.ConnectedComponentsWithStats(binary, labels, stats, centroids);

                        // 3. 결과 그리기 (컬러 변환)
                        Cv2.CvtColor(binary, _destImage, ColorConversionCodes.GRAY2BGR);

                        int validCount = 0;
                        // 라벨 0은 배경이므로 1부터 시작
                        for (int i = 1; i < labelCount; i++)
                        {
                            int area = stats.At<int>(i, 4);
                            if (area >= blobParams.MinArea)
                            {
                                validCount++;
                                if (blobParams.DrawBox)
                                {
                                    int x = stats.At<int>(i, 0);
                                    int y = stats.At<int>(i, 1);
                                    int w = stats.At<int>(i, 2);
                                    int h = stats.At<int>(i, 3);

                                    // 빨간 박스
                                    Cv2.Rectangle(_destImage, new Rect(x, y, w, h), Scalar.Red, 2);

                                    // 파란 점 (중심)
                                    int cx = (int)centroids.At<double>(i, 0);
                                    int cy = (int)centroids.At<double>(i, 1);
                                    Cv2.Circle(_destImage, cx, cy, 3, Scalar.Blue, -1);

                                    // 텍스트
                                    Cv2.PutText(_destImage, $"A:{area}", new Point(x, y - 5), HersheyFonts.HersheySimplex, 0.5, Scalar.Green, 1);
                                }
                            }
                        }

                        resultMessage = $"검출 성공: {validCount}개 (전체 {labelCount - 1}개 중)";

                        binary.Dispose();
                        labels.Dispose();
                        stats.Dispose();
                        centroids.Dispose();
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
        public void LoadGmfModelImage(string filePath)
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
        public void PreviewGmfModel(object param)
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

                    // Canny Edge
                    //double thresh1 = param.Smoothness;
                    //double thresh2 = param.Smoothness * 2;
                Cv2.Canny(gray, edges, thresh1, thresh2);

                _destImage.SetTo(Scalar.Lime, edges);
            }

                _cachedProcessed = _destImage.ToWriteableBitmap();
        }

        //public void TrainGmfModel(GmfParams param)
        public void TrainGmfModel(object param)
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
