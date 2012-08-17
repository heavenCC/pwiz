﻿/*
 * Original author: Brendan MacLean <brendanx .at. u.washington.edu>,
 *                  MacCoss Lab, Department of Genome Sciences, UW
 *
 * Copyright 2009 University of Washington - Seattle, WA
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using pwiz.MSGraph;
using pwiz.Skyline.Model;
using pwiz.Skyline.Model.DocSettings;
using pwiz.Skyline.Model.Results;
using pwiz.Skyline.Properties;
using ZedGraph;

namespace pwiz.Skyline.Controls.Graphs
{
    public class ChromGraphItem : AbstractChromGraphItem
    {
        private const string FONT_FACE = "Arial"; // Not L10N

        private static readonly Color COLOR_BEST_PEAK = Color.Black;
        private static readonly Color COLOR_RETENTION_TIME = Color.Gray;
        private static readonly Color COLOR_MSMSID_TIME = Color.Navy;
        private static readonly Color COLOR_ALIGNED_MSMSID_TIME = Color.LightBlue;
        private static readonly Color COLOR_RETENTION_WINDOW = Color.LightGoldenrodYellow;
        private static readonly Color COLOR_BOUNDARIES = Color.Gray;
        private static readonly Color COLOR_BOUNDARIES_BEST = Color.Black;

        private const int MIN_BOUNDARY_DISPLAY_WIDTH = 7;
        private const int MIN_BEST_BOUNDARY_HEIGHT = 20;

        public static Color ColorSelected { get { return Color.Red; } }

        // private static readonly Color _colorNone = Color.Gray;

        // private static readonly FontSpec _fontSpecNone = CreateFontSpec(_colorNone);

        private static FontSpec CreateFontSpec(Color color, float size)
        {
            return new FontSpec(FONT_FACE, size, color, false, false, false, Color.Empty, null, FillType.None) {Border = {IsVisible = false}};
        }

        private readonly double[] _measuredTimes;
        private readonly double[] _displayTimes;
        private readonly double[] _intensities;
        private readonly Color _color;
        private readonly FontSpec _fontSpec;
        private readonly int _width;

        private readonly Dictionary<int, int> _annotatedTimes = new Dictionary<int, int>();
        private readonly int[] _arrayLabelIndexes;
        private readonly double[] _dotProducts;
        private readonly double _bestProduct;
        private readonly bool _isFullScanMs;
        private readonly int _step;

        private int _bestPeakTimeIndex = -1;
        private PeakBoundsDragInfo _dragInfo;

        public ChromGraphItem(TransitionGroupDocNode transitionGroupNode,
                              TransitionDocNode transition,
                              ChromatogramInfo chromatogram,
                              TransitionChromInfo tranPeakInfo,
                              IRegressionFunction timeRegressionFunction,
                              bool[] annotatePeaks,
                              double[] dotProducts,
                              double bestProduct,
                              bool isFullScanMs,
                              int step,
                              Color color,
                              float fontSize,
                              int width)
        {
            TransitionGroupNode = transitionGroupNode;
            TransitionNode = transition;
            Chromatogram = chromatogram;
            TransitionChromInfo = tranPeakInfo;
            TimeRegressionFunction = timeRegressionFunction;

            _step = step;
            _color = color;
            _fontSpec = CreateFontSpec(color, fontSize);
            _width = width;

            _dotProducts = dotProducts;
            _bestProduct = bestProduct;
            _isFullScanMs = isFullScanMs;

            _arrayLabelIndexes = new int[annotatePeaks.Length];

            if (chromatogram == null)
            {
                _measuredTimes = new double[0];
                _displayTimes = _measuredTimes;
                _intensities = new double[0];
            }
            else
            {
                // Cache values early to avoid accessing slow enumerators
                // which show up under profiling.
                Chromatogram.AsArrays(out _measuredTimes, out _intensities);
                if (TimeRegressionFunction == null)
                {
                    _displayTimes = _measuredTimes;
                }
                else
                {
                    _displayTimes = _measuredTimes.Select(TimeRegressionFunction.GetY).ToArray();
                }
                // Add peak times to hash set for labeling
                int iLastStart = 0;
                for (int i = 0; i < chromatogram.NumPeaks; i++)
                {
                    int maxIndex = -1;
                    if (annotatePeaks[i])
                    {
                        ChromPeak peak = chromatogram.GetPeak(i);
                        if (!peak.IsForcedIntegration)
                            maxIndex = GetMaxIndex(peak.StartTime, peak.EndTime, ref iLastStart);
                    }
                    _arrayLabelIndexes[i] = maxIndex;
                    if (maxIndex != -1 && !_annotatedTimes.ContainsKey(maxIndex))
                        _annotatedTimes.Add(maxIndex, i);
                }

                // Calculate best peak index
                if (tranPeakInfo != null)
                {
                    iLastStart = 0;
                    _bestPeakTimeIndex = GetMaxIndex(tranPeakInfo.StartRetentionTime, tranPeakInfo.EndRetentionTime, ref iLastStart);
                }
            }
        }

        public TransitionGroupDocNode TransitionGroupNode { get; private set; }
        public TransitionDocNode TransitionNode { get; private set; }
        public ChromatogramInfo Chromatogram { get; private set; }
        public TransitionChromInfo TransitionChromInfo { get; private set; }
        public IRegressionFunction TimeRegressionFunction { get; private set; }
        public ScaledRetentionTime ScaleRetentionTime(double measuredTime)
        {
            return new ScaledRetentionTime(measuredTime, MeasuredTimeToDisplayTime(measuredTime));
        }

        public double? RetentionPrediction { get; set; }
        public double RetentionWindow { get; set; }

        public double[] RetentionMsMs { get; set; }
        public double? SelectedRetentionMsMs { get; set; }

        public double[] AlignedRetentionMsMs { get; set; }

        public bool HideBest { get; set; }

        public double BestPeakTime { get { return _bestPeakTimeIndex != -1 ? _measuredTimes[_bestPeakTimeIndex] : 0; } }

        internal PeakBoundsDragInfo DragInfo
        {
            get { return _dragInfo; }
            set
            {
                _dragInfo = value;
                var tranPeakInfo = TransitionChromInfo;
                double startTime, endTime;
                if (_dragInfo != null)
                {
                    startTime = _dragInfo.StartTime.MeasuredTime;
                    endTime = _dragInfo.EndTime.MeasuredTime;
                }
                else if (tranPeakInfo != null)
                {
                    startTime = tranPeakInfo.StartRetentionTime;
                    endTime = tranPeakInfo.EndRetentionTime;                    
                }
                else
                {
                    // No best peak index
                    _bestPeakTimeIndex = -1;
                    return;
                }
                // Recalculate the best peak based on the changed information
                int iLastStart = 0;
                _bestPeakTimeIndex = GetMaxIndex(startTime, endTime, ref iLastStart);
            }
        }

        public override Color Color { get { return _color; } }
        public FontSpec FontSpec { get { return _fontSpec; } }

        public override void CustomizeCurve(CurveItem curveItem)
        {
            ((LineItem)curveItem).Line.Width = _width;
        }

        public static string GetTitle(TransitionDocNode nodeTran)
        {
            var tran = nodeTran.Transition;
            return string.Format("{0}{1} - {2:F04}{3}", nodeTran.FragmentIonName, // Not L10N
                                 Transition.GetMassIndexText(tran.MassIndex),
                                 nodeTran.Mz,
                                 Transition.GetChargeIndicator(tran.Charge));
        }

        public static string GetTitle(TransitionGroupDocNode nodeGroup)
        {
            var seq = nodeGroup.TransitionGroup.Peptide.Sequence;
            return string.Format("{0} - {1:F04}{2}{3}", seq, nodeGroup.PrecursorMz, // Not L10N
                                 Transition.GetChargeIndicator(nodeGroup.TransitionGroup.PrecursorCharge),
                                 nodeGroup.TransitionGroup.LabelTypeText);            
        }

        public override string Title
        {
            get
            {
                if (_step != 0)
                    return string.Format(Resources.ChromGraphItem_Title_Step__0_, _step);

                return (TransitionNode == null
                    ? GetTitle(TransitionGroupNode)
                    : GetTitle(TransitionNode));
            }
        }

        public override IPointList Points
        {
            get
            {
                return new PointPairList(_displayTimes, _intensities);
            }
        }

        public override void AddAnnotations(MSGraphPane graphPane, Graphics g,
                                            MSPointList pointList, GraphObjList annotations)
        {
            if (Chromatogram == null)
                return;

            // Calculate maximum y for potential retention time indicators
            double xTemp, yMax;
            PointF ptTop = new PointF(0, graphPane.Chart.Rect.Top);
            graphPane.ReverseTransform(ptTop, out xTemp, out yMax);

            if (GraphChromatogram.ShowRT != ShowRTChrom.none)
            {
                if (RetentionMsMs != null)
                {
                    foreach (double retentionTime in RetentionMsMs)
                    {
                        Color color = COLOR_MSMSID_TIME;
                        if (SelectedRetentionMsMs.HasValue && Equals((float) retentionTime, (float) SelectedRetentionMsMs))
                        {
                            color = ColorSelected;
                        }
                        AddRetentionTimeAnnotation(graphPane, g, annotations, ptTop, Resources.ChromGraphItem_AddAnnotations_ID, GraphObjType.ms_ms_id, color, ScaleRetentionTime(retentionTime));
                    }
                }
                if (AlignedRetentionMsMs != null)
                {
                    foreach (var time in AlignedRetentionMsMs)
                    {
                        var scaledTime = ScaleRetentionTime(time);
                        var line = new LineObj(COLOR_ALIGNED_MSMSID_TIME, scaledTime.DisplayTime, yMax, scaledTime.DisplayTime, 0)
                        {
                            ZOrder = ZOrder.E_BehindCurves,
                            IsClippedToChartRect = true,
                            Tag = new GraphObjTag(this, GraphObjType.aligned_ms_id, scaledTime),
                        };
                        annotations.Add(line);
                    }
                }
            }

            // Draw retention time indicator, if set
            if (RetentionPrediction.HasValue)
            {
                double time = RetentionPrediction.Value;

                // Create temporary label to calculate positions
                if (GraphChromatogram.ShowRT != ShowRTChrom.none)
                {
                    AddRetentionTimeAnnotation(graphPane, g, annotations,
                        ptTop, "Predicted", GraphObjType.predicted_rt_window, COLOR_RETENTION_TIME, ScaleRetentionTime(time));
                }

                // Draw background for retention time window
                if (RetentionWindow > 0)
                {
                    double boxHeight = yMax;
                    BoxObj box = new BoxObj(time - RetentionWindow / 2, boxHeight, RetentionWindow, boxHeight,
                                            COLOR_RETENTION_WINDOW, COLOR_RETENTION_WINDOW)
                                     {
                                         IsClippedToChartRect = true,
                                         ZOrder = ZOrder.F_BehindGrid
                                     };
                    annotations.Add(box);
                }
            }

            if ((DragInfo != null || (!HideBest && TransitionChromInfo != null)))
            {
                // Show text and arrow for the best peak
                double intensityBest = 0;
                if (_bestPeakTimeIndex != -1)
                {
                    ScaledRetentionTime timeBest = new ScaledRetentionTime(_measuredTimes[_bestPeakTimeIndex], _displayTimes[_bestPeakTimeIndex]);
                    float xBest = graphPane.XAxis.Scale.Transform(timeBest.DisplayTime);
                    intensityBest = _intensities[_bestPeakTimeIndex];
                    float yBest = graphPane.YAxis.Scale.Transform(intensityBest);

                    if (GraphChromatogram.ShowRT != ShowRTChrom.none || DragInfo != null)
                    {
                        // Best peak gets its own label to avoid curve overlap detection
                        double intensityLabel = graphPane.YAxis.Scale.ReverseTransform(yBest - 5);
                        string label = FormatTimeLabel(timeBest.DisplayTime, _dotProducts != null ? _bestProduct : 0);
                        TextObj text = new TextObj(label, timeBest.DisplayTime, intensityLabel,
                                                   CoordType.AxisXYScale, AlignH.Center, AlignV.Bottom)
                                           {
                                               ZOrder = ZOrder.A_InFront,
                                               IsClippedToChartRect = true,
                                               FontSpec = FontSpec,
                                               Tag = new GraphObjTag(this, GraphObjType.best_peak, timeBest)
                                           };
                        annotations.Add(text);
                    }

                    // Show the best peak arrow indicator
                    double timeArrow = graphPane.XAxis.Scale.ReverseTransform(xBest - 4);
                    double intensityArrow = graphPane.YAxis.Scale.ReverseTransform(yBest - 2);

                    ArrowObj arrow = new ArrowObj(COLOR_BEST_PEAK, 12f,
                                                  timeArrow, intensityArrow, timeArrow, intensityArrow)
                                         {
                                             Location = { CoordinateFrame = CoordType.AxisXYScale },
                                             IsArrowHead = true,
                                             IsClippedToChartRect = true,
                                             ZOrder = ZOrder.A_InFront
                                         };
                    annotations.Add(arrow);                    
                }

                // Show the best peak boundary lines
                double startTime, endTime;
                if (DragInfo != null)
                {
                    startTime = DragInfo.StartTime.MeasuredTime;
                    endTime = DragInfo.EndTime.MeasuredTime;
                }
                else
                {
                    var tranPeakInfo = TransitionChromInfo;
                    startTime = tranPeakInfo.StartRetentionTime;
                    endTime = tranPeakInfo.EndRetentionTime;                    
                }
                AddPeakBoundaries(graphPane, annotations, true,
                                  ScaleRetentionTime(startTime), ScaleRetentionTime(endTime), intensityBest);
            }

            for (int i = 0, len = Chromatogram.NumPeaks; i < len; i++)
            {
                if (_arrayLabelIndexes[i] == -1)
                    continue;

                ChromPeak peak = Chromatogram.GetPeak(i);
                if (peak.IsForcedIntegration)
                    continue;

                double maxIntensity = _intensities[_arrayLabelIndexes[i]];

                // Show peak extent indicators, if they are far enough apart
                AddPeakBoundaries(graphPane, annotations, false,
                                  ScaleRetentionTime(peak.StartTime), ScaleRetentionTime(peak.EndTime), maxIntensity);
            }
        }

        private void AddRetentionTimeAnnotation(MSGraphPane graphPane, Graphics g, GraphObjList annotations,
            PointF ptTop, string title, GraphObjType graphObjType, Color color, ScaledRetentionTime retentionTime)
        {
            double xTemp;
            string label = string.Format("{0}\n{1:F01}", title, retentionTime.DisplayTime);
            FontSpec fontLabel = CreateFontSpec(color, _fontSpec.Size);
            SizeF sizeLabel = fontLabel.MeasureString(g, label, graphPane.CalcScaleFactor());
            ptTop = new PointF(0, ptTop.Y + sizeLabel.Height + 10);

            double intensity;
            graphPane.ReverseTransform(ptTop, out xTemp, out intensity);

            LineObj stick = new LineObj(color, retentionTime.DisplayTime, intensity, retentionTime.DisplayTime, 0)
                                {
                                    IsClippedToChartRect = true,
                                    Location = { CoordinateFrame = CoordType.AxisXYScale },
                                    ZOrder = ZOrder.E_BehindCurves,
                                    Line = { Width = 1 },
                                    Tag = new GraphObjTag(this, graphObjType, retentionTime),
                                };
            annotations.Add(stick);

            ptTop = new PointF(0, ptTop.Y - 5);
            graphPane.ReverseTransform(ptTop, out xTemp, out intensity);
            TextObj text = new TextObj(label, retentionTime.DisplayTime, intensity,
                                       CoordType.AxisXYScale, AlignH.Center, AlignV.Bottom)
                               {
                                   IsClippedToChartRect = true,
                                   ZOrder = ZOrder.E_BehindCurves,
                                   FontSpec = CreateFontSpec(color, _fontSpec.Size),
                                   Tag = new GraphObjTag(this, graphObjType, retentionTime),
                               };
            annotations.Add(text);
        }

        public double GetMaxIntensity(double startTime, double endTime)
        {
            int iLastStart = 0;
            int iMax = GetMaxIndex(startTime, endTime, ref iLastStart);
            return (iMax != -1 ? _intensities[iMax] : 0);
        }

        private int GetMaxIndex(double startTime, double endTime, ref int iLastStart)
        {
            // Search forward for the start of the peak
            // This position is retained as the start of the next search, since
            // peaks are not guaranteed to be non-overlapping.
            while (iLastStart < _measuredTimes.Length && _measuredTimes[iLastStart] < startTime)
                iLastStart++;
            // Search forward for the maximum intensity until the end of the peak is reached
            int maxIndex = -1;
            double maxIntensity = 0;
            for (int iPoint = iLastStart; iPoint < _measuredTimes.Length && _measuredTimes[iPoint] < endTime; iPoint++)
            {
                if (_intensities[iPoint] > maxIntensity)
                {
                    maxIntensity = _intensities[iPoint];
                    maxIndex = iPoint;
                }
            }
            return maxIndex;
        }

        private void AddPeakBoundaries(GraphPane graphPane, ICollection<GraphObj> annotations,
                                       bool best, ScaledRetentionTime startTime, ScaledRetentionTime endTime, double maxIntensity)
        {
            // Only show boundaries for dragging when boundaries turned off
            if (!Settings.Default.ShowPeakBoundaries && (!best || DragInfo == null))
                return;
            float xStart = graphPane.XAxis.Scale.Transform(startTime.DisplayTime);
            float xEnd = graphPane.XAxis.Scale.Transform(endTime.DisplayTime);
            // Hide boundaries, if they are too close together
            if (xEnd - xStart <= MIN_BOUNDARY_DISPLAY_WIDTH)
            {
                // But not if they are currently part of a drag operation.
                if (DragInfo == null)
                    return;                
            }

            // Make sure the best borders are visible
            if (best)
            {
                float yMax = graphPane.YAxis.Scale.Transform(maxIntensity);
                float yZero = graphPane.YAxis.Scale.Transform(0);
                if (yZero - yMax < MIN_BEST_BOUNDARY_HEIGHT)
                    maxIntensity = graphPane.YAxis.Scale.ReverseTransform(yZero - MIN_BEST_BOUNDARY_HEIGHT);
            }

            Color colorBoundaries = (best ? COLOR_BOUNDARIES_BEST : COLOR_BOUNDARIES);
            GraphObjType graphObjType = best ? GraphObjType.best_peak : GraphObjType.peak;

            // Make sure to get maximum intensity within the peak range,
            // as this is not guaranteed to be the center of the peak
            LineObj stickStart = new LineObj(colorBoundaries, startTime.DisplayTime, maxIntensity, startTime.DisplayTime, 0)
                                     {
                                         IsClippedToChartRect = true,
                                         Location = { CoordinateFrame = CoordType.AxisXYScale },
                                         ZOrder = ZOrder.B_BehindLegend,
                                         Line = { Width = 1, Style = DashStyle.Dash },
                                         Tag = new GraphObjTag(this, graphObjType, startTime),
                                     };
            annotations.Add(stickStart);
            LineObj stickEnd = new LineObj(colorBoundaries, endTime.DisplayTime, maxIntensity, endTime.DisplayTime, 0)
                                   {
                                       IsClippedToChartRect = true,
                                       Location = { CoordinateFrame = CoordType.AxisXYScale },
                                       ZOrder = ZOrder.B_BehindLegend,
                                       Line = { Width = 1, Style = DashStyle.Dash },
                                       Tag = new GraphObjTag(this, graphObjType, endTime),
                                   };
            annotations.Add(stickEnd);
        }

        public override PointAnnotation AnnotatePoint(PointPair point)
        {
            int indexPeak;
            var showRT = GraphChromatogram.ShowRT;
            int timeIndex = Array.BinarySearch(_displayTimes, point.X);
            if ((showRT == ShowRTChrom.all ||
                 (showRT == ShowRTChrom.threshold && Settings.Default.ShowRetentionTimesThreshold <= point.Y))
                && _annotatedTimes.TryGetValue(timeIndex, out indexPeak))
            {
                string label = FormatTimeLabel(point.X, _dotProducts != null
                    ? _dotProducts[indexPeak] : 0);
                return new PointAnnotation(label, FontSpec);
            }

            return null;
        }

        public string FormatTimeLabel(double time, double dotProduct)
        {
            string label = string.Format("{0:F01}", time); // Not L10N
            if (dotProduct != 0)
                label += string.Format("\n({0} {1:F02})", _isFullScanMs ? "idotp" : "dotp", dotProduct); // L10N
//<<<<<<< .mine
            return label;
        }

        
        public ScaledRetentionTime FindPeakRetentionTime(TextObj label)
        {
            var tag = label.Tag as GraphObjTag;
            if (null != tag || !ReferenceEquals(FontSpec, label.FontSpec))
            {
                return ScaledRetentionTime.ZERO;
            }
            double time;
            if (double.TryParse(label.Text.Split('\n')[0], out time))
            {
                // Search for a time that corresponds with the label
                foreach (int iTime in _arrayLabelIndexes)
                {
                    if (iTime != -1 && Math.Abs(time - _displayTimes[iTime]) < 0.15)
                    {
                        return new ScaledRetentionTime(_measuredTimes[iTime], _displayTimes[iTime]);
                    }
                }
            }
            return ScaledRetentionTime.ZERO;
        }

        public ScaledRetentionTime FindSpectrumRetentionTime(GraphObj graphObj)
        {
            var tag = graphObj.Tag as GraphObjTag;
            if (null == tag || !ReferenceEquals(this, tag.ChromGraphItem))
            {
                return ScaledRetentionTime.ZERO;
            }
            if (GraphObjType.ms_ms_id != tag.GraphObjType)
            {
                return ScaledRetentionTime.ZERO;
            }
            return tag.RetentionTime;
        }

        public ScaledRetentionTime GetNearestBestPeakBoundary(double displayTime)
        {
            var tranPeakInfo = TransitionChromInfo;
            if (tranPeakInfo == null)
            {
                return ScaledRetentionTime.ZERO;
            }
            var peakStartTime = ScaleRetentionTime(tranPeakInfo.StartRetentionTime);
            var peakEndTime = ScaleRetentionTime(tranPeakInfo.EndRetentionTime);
            double deltaStart = Math.Abs(peakStartTime.DisplayTime - displayTime);
            double deltaEnd = Math.Abs(peakEndTime.DisplayTime - displayTime);
            return deltaStart < deltaEnd ? peakStartTime : peakEndTime;
        }

        public double MeasuredTimeToDisplayTime(double time)
        {
            if (TimeRegressionFunction == null)
            {
                return time;
            }
            return TimeRegressionFunction.GetY(time);
        }

        public ScaledRetentionTime GetNearestDisplayTime(double displayTime)
        {
            int index = NearestIndex(_displayTimes, displayTime);
            if (index < 0)
            {
                return ScaledRetentionTime.ZERO;
            }
            return new ScaledRetentionTime(_measuredTimes[index], _displayTimes[index]);
        }

        private static int NearestIndex(double[] sortedArray, double value)
        {
            int index = Array.BinarySearch(sortedArray, value);
            if (index < 0)
            {
                index = ~index;
                if (index == sortedArray.Length || (index > 0 && sortedArray[index] - value > value - sortedArray[index - 1]))
                {
                    index--;
                }
            }
            return index;
        }
        
        enum GraphObjType
        {
            invalid,
            ms_ms_id,
            predicted_rt_window,
            aligned_ms_id,
            best_peak,
            peak,
        }

        class GraphObjTag
        {
            public GraphObjTag(ChromGraphItem chromGraphItem, GraphObjType graphObjType, ScaledRetentionTime retentionTime)
            {
                ChromGraphItem = chromGraphItem;
                GraphObjType = graphObjType;
                RetentionTime = retentionTime;
                
            }

            public ChromGraphItem ChromGraphItem { get; private set; }
            public GraphObjType GraphObjType { get; private set; }
            public ScaledRetentionTime RetentionTime { get; private set; }
            public override string ToString()
            {
                return string.Format("{0}:{1}", GraphObjType, RetentionTime);
            }
        }
    }

    public sealed class FailedChromGraphItem : NoDataChromGraphItem
    {
        public FailedChromGraphItem(TransitionGroupDocNode nodeGroup, Exception x)
            : base(string.Format(Resources.FailedChromGraphItem_FailedChromGraphItem__0__load_failed__1__, ChromGraphItem.GetTitle(nodeGroup), x.Message))
        {            
        }
    }

    public sealed class NotFoundChromGraphItem : NoDataChromGraphItem
    {
        public NotFoundChromGraphItem(TransitionDocNode nodeTran)
            : base(string.Format(Resources.NotFoundChromGraphItem_NotFoundChromGraphItem__0__not_found, ChromGraphItem.GetTitle(nodeTran)))
        {
        }

        public NotFoundChromGraphItem(TransitionGroupDocNode nodeGroup)
            : base(string.Format(Resources.NotFoundChromGraphItem_NotFoundChromGraphItem__0__not_found, ChromGraphItem.GetTitle(nodeGroup)))
        {
        }
    }

    public sealed class UnavailableChromGraphItem : NoDataChromGraphItem
    {
        public UnavailableChromGraphItem() : base(Resources.UnavailableChromGraphItem_UnavailableChromGraphItem_Chromatogram_information_unavailable)
        {
        }
    }

    public class NoDataChromGraphItem : AbstractChromGraphItem
    {
        private readonly string _title;

        public NoDataChromGraphItem(string title)
        {
            _title = title;
        }

        public override string Title { get { return _title; } }

        public override PointAnnotation AnnotatePoint(PointPair point)
        {
            return null;
        }

        public override void AddAnnotations(MSGraphPane graphPane, Graphics g, MSPointList pointList, GraphObjList annotations)
        {
            // Do nothing
        }

        public override IPointList Points
        {
            get
            {
                return new PointPairList(new double[0], new double[0]);
            }
        }
    }

    public abstract class AbstractChromGraphItem : IMSGraphItemExtended
    {
        public abstract string Title { get; }
        public abstract PointAnnotation AnnotatePoint(PointPair point);
        public abstract void AddAnnotations(MSGraphPane graphPane, Graphics g,
                                            MSPointList pointList, GraphObjList annotations);
        public abstract IPointList Points { get; }

        public virtual Color Color
        {
            get { return Color.Gray; }
        }

        public virtual void CustomizeCurve(CurveItem curveItem)
        {
            // Do nothing by default
        }

        public MSGraphItemType GraphItemType
        {
            get { return MSGraphItemType.chromatogram; }
        }

        public MSGraphItemDrawMethod GraphItemDrawMethod
        {
            get { return MSGraphItemDrawMethod.line; }
        }

        public void CustomizeYAxis(Axis axis)
        {
            CustomizeAxis(axis, Resources.AbstractChromGraphItem_CustomizeYAxis_Intensity);
        }

        public void CustomizeXAxis(Axis axis)
        {
            CustomizeAxis(axis, Resources.AbstractChromGraphItem_CustomizeXAxis_Retention_Time);
        }

        private static void CustomizeAxis(Axis axis, string title)
        {
            axis.Title.FontSpec.Family = "Arial"; // Not L10N
            axis.Title.FontSpec.Size = 14;
            axis.Color = axis.Title.FontSpec.FontColor = Color.Black;
            axis.Title.FontSpec.Border.IsVisible = false;
            axis.Title.Text = title;
        }
    }
}