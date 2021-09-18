﻿using Fasetto.Word;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Text_Grab.Controls;
using Text_Grab.Properties;
using Text_Grab.Utilities;
using Windows.Media.Ocr;

namespace Text_Grab.Views
{
    /// <summary>
    /// Interaction logic for PersistentWindow.xaml
    /// </summary>
    public partial class GrabFrame : Window
    {
        private bool isDrawing = false;
        private OcrResult ocrResultOfWindow;
        private ObservableCollection<WordBorder> wordBorders = new ObservableCollection<WordBorder>();
        private DispatcherTimer reDrawTimer = new DispatcherTimer();
        private bool isSelecting;
        private Point clickedPoint;
        private Border selectBorder = new Border();
        private System.Windows.Point GetMousePos() => this.PointToScreen(Mouse.GetPosition(this));

        public bool IsfromEditWindow { get; set; } = false;

        public GrabFrame()
        {
            InitializeComponent();

            WindowResizer resizer = new WindowResizer(this);
            reDrawTimer.Interval = new TimeSpan(0, 0, 0, 0, 1200);
            reDrawTimer.Tick += ReDrawTimer_Tick;

            RoutedCommand newCmd = new RoutedCommand();
            _ = newCmd.InputGestures.Add(new KeyGesture(Key.Escape));
            _ = CommandBindings.Add(new CommandBinding(newCmd, Escape_Keyed));
        }

        private void Escape_Keyed(object sender, ExecutedRoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text) == false && SearchBox.Text != "Search For Text...")
                SearchBox.Text = "";
            else if (RectanglesCanvas.Children.Count > 0)
                ResetGrabFrame();
            else
                Close();
        }

        private async void ReDrawTimer_Tick(object sender, EventArgs e)
        {
            reDrawTimer.Stop();
            ResetGrabFrame();

            TextBox searchBox = SearchBox;

            if (searchBox != null)
                await DrawRectanglesAroundWords(searchBox.Text);
        }

        private void OnCloseButtonClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void GrabBTN_Click(object sender, RoutedEventArgs e)
        {
            string frameText = "";

            if (wordBorders.Where(w => w.IsSelected == true).ToList().Count == 0)
            {
                Point windowPosition = this.GetAbsolutePosition();
                DpiScale dpi = VisualTreeHelper.GetDpi(this);
                System.Drawing.Rectangle rectCanvasSize = new System.Drawing.Rectangle
                {
                    Width = (int)((ActualWidth + 2) * dpi.DpiScaleX),
                    Height = (int)((Height - 64) * dpi.DpiScaleY),
                    X = (int)((windowPosition.X - 2) * dpi.DpiScaleX),
                    Y = (int)((windowPosition.Y + 24) * dpi.DpiScaleY)
                };
                frameText = await ImageMethods.GetRegionsText(null, rectCanvasSize);
            }

            if (wordBorders.Count > 0)
            {
                List<WordBorder> selectedBorders = wordBorders.Where(w => w.IsSelected == true).ToList();

                if (selectedBorders.Count == 0)
                    selectedBorders.AddRange(wordBorders);

                List<string> lineList = new List<string>();
                StringBuilder outputString = new StringBuilder();
                int lastLineNum = selectedBorders.FirstOrDefault().LineNumber;
                foreach (WordBorder border in selectedBorders)
                {
                    if (border.LineNumber != lastLineNum)
                    {
                        outputString.Append(string.Join(' ', lineList));
                        outputString.Append(Environment.NewLine);
                        lineList.Clear();
                        lastLineNum = border.LineNumber;

                    }
                    lineList.Add(border.Word);
                }
                outputString.Append(string.Join(' ', lineList));

                frameText = outputString.ToString();
            }


            if (IsfromEditWindow == false
                && string.IsNullOrWhiteSpace(frameText) == false)
            {
                Clipboard.SetText(frameText);

                if (Settings.Default.ShowToast)
                    NotificationUtilities.ShowToast(frameText);
            }

            if (IsfromEditWindow == true && string.IsNullOrWhiteSpace(frameText) == false)
                WindowUtilities.AddTextToOpenWindow(frameText);
        }

        private void ResetGrabFrame()
        {
            ocrResultOfWindow = null;
            RectanglesCanvas.Children.Clear();
            wordBorders.Clear();
            MatchesTXTBLK.Text = "Matches: 0";
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            if (IsLoaded == false)
                return;

            ResetGrabFrame();

            reDrawTimer.Start();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (IsLoaded == false)
                return;

            ResetGrabFrame();

            reDrawTimer.Start();
        }

        private void GrabFrameWindow_Deactivated(object sender, EventArgs e)
        {
            ResetGrabFrame();
        }

        private async Task DrawRectanglesAroundWords(string searchWord)
        {
            if (isDrawing == true)
                return;

            isDrawing = true;

            RectanglesCanvas.Children.Clear();
            wordBorders.Clear();

            Point windowPosition = this.GetAbsolutePosition();
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            System.Drawing.Rectangle rectCanvasSize = new System.Drawing.Rectangle
            {
                Width = (int)((ActualWidth + 2) * dpi.DpiScaleX),
                Height = (int)((ActualHeight - 64) * dpi.DpiScaleY),
                X = (int)((windowPosition.X - 2) * dpi.DpiScaleX),
                Y = (int)((windowPosition.Y + 24) * dpi.DpiScaleY)
            };

            if (ocrResultOfWindow == null || ocrResultOfWindow.Lines.Count == 0)
                ocrResultOfWindow = await ImageMethods.GetOcrResultFromRegion(rectCanvasSize);

            int numberOfMatches = 0;
            int lineNumber = 0;

            foreach (OcrLine ocrLine in ocrResultOfWindow.Lines)
            {
                foreach (OcrWord ocrWord in ocrLine.Words)
                {
                    string wordString = ocrWord.Text;

                    if (Settings.Default.CorrectErrors)
                        wordString = wordString.TryFixEveryWordLetterNumberErrors();

                    WordBorder wordBorderBox = new WordBorder
                    {
                        Width = (ocrWord.BoundingRect.Width / dpi.DpiScaleX) + 6,
                        Height = (ocrWord.BoundingRect.Height / dpi.DpiScaleY) + 6,
                        Word = wordString,
                        ToolTip = wordString,
                        LineNumber = lineNumber,
                        IsFromEditWindow = IsfromEditWindow
                    };

                    if ((bool)ExactMatchChkBx.IsChecked)
                    {
                        if (wordString.Equals(searchWord, StringComparison.CurrentCulture))
                        {
                            wordBorderBox.Select();
                            numberOfMatches++;
                        }
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(searchWord)
                            && wordString.Contains(searchWord, StringComparison.CurrentCultureIgnoreCase))
                        {
                            wordBorderBox.Select();
                            numberOfMatches++;
                        }
                    }

                    wordBorders.Add(wordBorderBox);
                    _ = RectanglesCanvas.Children.Add(wordBorderBox);
                    Canvas.SetLeft(wordBorderBox, (ocrWord.BoundingRect.Left / dpi.DpiScaleX) - 3);
                    Canvas.SetTop(wordBorderBox, (ocrWord.BoundingRect.Top / dpi.DpiScaleY) - 3);
                }

                lineNumber++;
            }

            if (ocrResultOfWindow != null && ocrResultOfWindow.TextAngle != null)
            {
                RotateTransform transform = new RotateTransform((double)ocrResultOfWindow.TextAngle)
                {
                    CenterX = (Width - 4) / 2,
                    CenterY = (Height - 60) / 2
                };
                RectanglesCanvas.RenderTransform = transform;
            }
            MatchesTXTBLK.Text = $"Matches: {numberOfMatches}";
            isDrawing = false;
        }

        private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsLoaded == false)
                return;

            if (sender is TextBox searchBox)
                await DrawRectanglesAroundWords(searchBox.Text);
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox searchBox = sender as TextBox;
            searchBox.Text = "";
        }

        private async void ExactMatchChkBx_Click(object sender, RoutedEventArgs e)
        {
            TextBox searchBox = SearchBox;

            if (searchBox != null)
                await DrawRectanglesAroundWords(searchBox.Text);
        }

        private void ClearBTN_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
        }

        private async void RefreshBTN_Click(object sender, RoutedEventArgs e)
        {
            TextBox searchBox = SearchBox;
            ResetGrabFrame();

            await Task.Delay(200);

            if (searchBox != null)
                await DrawRectanglesAroundWords(searchBox.Text);
        }

        private void GrabFrameWindow_Activated(object sender, EventArgs e)
        {
            reDrawTimer.Start();
        }

        private void RectanglesCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            isSelecting = true;
            RectanglesCanvas.CaptureMouse();
            CursorClipper.ClipCursor(RectanglesCanvas);
            clickedPoint = e.GetPosition(RectanglesCanvas);
            selectBorder.Height = 1;
            selectBorder.Width = 1;

            try { RectanglesCanvas.Children.Remove(selectBorder); } catch (Exception) { }

            selectBorder.BorderThickness = new Thickness(2);
            Color borderColor = Color.FromArgb(255, 40, 118, 126);
            selectBorder.BorderBrush = new SolidColorBrush(borderColor);
            Color backgroundColor = Color.FromArgb(15, 40, 118, 126);
            selectBorder.Background = new SolidColorBrush(backgroundColor);
            _ = RectanglesCanvas.Children.Add(selectBorder);
            Canvas.SetLeft(selectBorder, clickedPoint.X);
            Canvas.SetTop(selectBorder, clickedPoint.Y);
        }

        private void RectanglesCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            isSelecting = false;
            CursorClipper.UnClipCursor();
            RectanglesCanvas.ReleaseMouseCapture();

            try { RectanglesCanvas.Children.Remove(selectBorder); } catch { }

            CheckSelectBorderIntersections(true);
        }

        private void RectanglesCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isSelecting)
            {
                return;
            }

            Point movingPoint = e.GetPosition(RectanglesCanvas);
            SelectionRectangleHelper.DrawSelectionRectangle(selectBorder, clickedPoint, movingPoint);

            CheckSelectBorderIntersections();
        }

        private void CheckSelectBorderIntersections(bool finalCheck = false)
        {
            Rect rectSelect = new Rect(Canvas.GetLeft(selectBorder), Canvas.GetTop(selectBorder), selectBorder.Width, selectBorder.Height);

            bool clickedEmptySpace = true;
            bool smallSelction = false;
            if (rectSelect.Width < 10 && rectSelect.Height < 10)
                smallSelction = true;

            foreach (WordBorder wordBorder in wordBorders)
            {
                Rect wbRect = new Rect(Canvas.GetLeft(wordBorder), Canvas.GetTop(wordBorder), wordBorder.Width, wordBorder.Height);

                if (rectSelect.IntersectsWith(wbRect))
                {
                    clickedEmptySpace = false;

                    if (smallSelction == false)
                    {
                        wordBorder.Select();
                        wordBorder.WasRegionSelected = true;
                    }

                }
                else
                {
                    if (wordBorder.WasRegionSelected == true)
                        wordBorder.Deselect();
                }

                if (finalCheck == true)
                    wordBorder.WasRegionSelected = false;
            }

            if (clickedEmptySpace == true
                && smallSelction == true
                && finalCheck == true)
            {
                foreach (WordBorder wb in wordBorders)
                    wb.Deselect();
            }
        }
    }
}
