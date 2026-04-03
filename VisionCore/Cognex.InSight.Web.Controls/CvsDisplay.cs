using Newtonsoft.Json.Linq;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Cognex.InSight.Remoting.Serialization;

namespace Cognex.InSight.Web.Controls
{
    public partial class CvsDisplay : UserControl
    {
        private CvsInSight _inSight;
        private bool _usesXYCoordinates = false;

        // The graphics, if any
        private CvsCogShape[] _graphics = new CvsCogShape[0];
        private CvsCogShape[] _nextGraphics = new CvsCogShape[0];

        // WPF 그래픽 On/Off 스위치
        public bool IsDrawingEnabled { get; set; } = true;

        public CvsDisplay()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Sets the CvsInSight for this Spreadsheet.
        /// </summary>
        public void SetInSight(CvsInSight inSight)
        {
            _inSight = inSight;
        }

        public void InitDisplay()
        {
            picBox.ImageLocation = "";
            _graphics = new CvsCogShape[0]; // Clear any old graphics
            _nextGraphics = new CvsCogShape[0];
        }

        public async Task OnConnected()
        {
            _usesXYCoordinates = _inSight.UsesXYCoordinates;
            await UpdateResults();
        }

        public async Task UpdateResults()
        {
            if (_inSight == null)
                return;

            // Get the graphics that may be rendered later in the OnPaint
            try
            {
                if (_inSight.LiveMode)
                {
                    _nextGraphics = new CvsCogShape[0];
                }
                else
                {
                    _nextGraphics = await _inSight.GetGraphicsAsync();
                }
            }
            catch (Exception ex)
            {
                _nextGraphics = new CvsCogShape[0];
                Debug.WriteLine($"UpdateResults Exception: {ex.Message}");
            }

            // Note: This event will arrive on a worker thread.
            // Before a windows control is directly updated, invoke to the main SynchronizationContext
            picBox.Invoke((Action)delegate
            {
                int imageWidth = 0;
                int imageHeight = 0;
                CvsCogViewPort viewPort = _inSight?.ViewPort;

                if (viewPort != null)
                {
                    imageWidth = viewPort.Width;
                    imageHeight = viewPort.Height;
                }

                string url = _inSight.GetMainImageUrl(imageWidth, imageHeight);

                if (picBox.ImageLocation != url)
                {
                    picBox.LoadAsync(url);
                }
                else
                {
                    Task.Run(() => SendReady());
                }
            });
        }

        private async void SendReady()
        {
            if (!_inSight.LiveMode)
            {
                try
                {
                    await _inSight.SendReady().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SendReady Exception: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// This method provides an example of how to render the graphics over the picture box image.
        /// </summary>
        private void picBox_Paint(object sender, PaintEventArgs e)
        {
            Graphics gr = e.Graphics;
            PictureBox pb = sender as PictureBox;
            Image img = pb.Image;

            // 1. 그래픽 On/Off 체크 및 이미지 유무 확인
            if (!IsDrawingEnabled || img == null)
            {
                gr.Clear(pb.BackColor);
                return;
            }

            CvsCogViewPort viewPort = _inSight?.ViewPort;
            if (viewPort == null)
            {
                gr.Clear(pb.BackColor);
                return;
            }

            // 2. [수정] 윈도우가 지정한 일부 영역이 아닌 전체 영역 크기를 참조 (ClientSize)
            int picWidth = pb.ClientSize.Width;
            int picHeight = pb.ClientSize.Height;

            float xMax = img.Width;
            float yMax = img.Height;

            int startX = 0;
            int startY = 0;
            int endX = img.Width;
            int endY = img.Height;

            float imageRatio = xMax / (float)yMax;
            float containerRatio = picWidth / (float)picHeight;

            float scaleFactor;

            // 3. 비율에 따른 Scale 및 Filler(여백) 계산 (Zoom 모드 구현)
            if (imageRatio >= containerRatio)
            {
                // 가로로 긴 이미지 (상하 여백 발생)
                scaleFactor = picWidth / (float)xMax;
                float scaledHeight = yMax * scaleFactor;
                float filler = Math.Abs(picHeight - scaledHeight) / 2;

                startX = (int)(startX * scaleFactor);
                endX = (int)(endX * scaleFactor);
                startY = (int)((startY) * scaleFactor + filler);
                endY = (int)((endY) * scaleFactor + filler);
            }
            else
            {
                // 세로로 긴 이미지 (좌우 여백 발생)
                scaleFactor = picHeight / (float)yMax;
                float scaledWidth = xMax * scaleFactor;
                float filler = Math.Abs(picWidth - scaledWidth) / 2;

                startX = (int)((startX) * scaleFactor + filler);
                endX = (int)((endX) * scaleFactor + filler);
                startY = (int)(startY * scaleFactor);
                endY = (int)(endY * scaleFactor);
            }

            // 해상도 팩터 적용 (아까 1로 설정하셨던 부분 유지)
            const int resolutionFactor = 1;
            scaleFactor = scaleFactor / resolutionFactor;

            // 좌표 컨텍스트 생성 및 그래픽 그리기
            DisplayContext dc = new DisplayContext(_usesXYCoordinates, scaleFactor, startX, startY, this.Bounds);

            if (!_inSight.LiveMode)
            {
                GraphicsHelper.DrawGraphics(gr, _graphics, dc);
            }

            // 결과 배너 표시 체크
            JToken results = _inSight.Results;
            JToken token = results.SelectToken("queuedResult");
            if ((token != null) && (token.Value<Boolean>() == true))
            {
                DisplayQueuedResultBanner(gr);
            }
        }

        private void DisplayQueuedResultBanner(Graphics gr)
        {
            RectangleF rect = new RectangleF(new PointF(0, 0), new SizeF(this.ClientRectangle.Width, 20));
            gr.FillRectangle(Brushes.Yellow, rect);

            Font font = new Font("Arial", 9);
            string label = "Showing Queued Result";
            StringFormat sr = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            gr.DrawString(label, font, Brushes.Black, rect, sr);
        }

        private async void picBox_LoadCompleted(object sender, AsyncCompletedEventArgs e)
        {
            try
            {
                _graphics = _nextGraphics;
                if (_inSight.Connected)
                {
                    await _inSight.SendReady().ConfigureAwait(false);
                }
                // 이미지가 로드되면 화면 강제 갱신
                picBox.Invalidate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SendReady Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// WPF에서 호출하여 그래픽을 켜고 끄는 인터페이스
        /// </summary>
        public void ToggleOverlay(bool isVisible)
        {
            IsDrawingEnabled = isVisible;

            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => picBox.Invalidate()));
            }
            else
            {
                picBox.Invalidate();
            }
        }
    }
}