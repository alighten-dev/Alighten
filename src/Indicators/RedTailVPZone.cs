#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
#endregion

// RedTail VP Zone - Fixed Range Volume Profile Zone Drawing Tool
// Created by RedTail Indicators - @_hawkeye_13
// Click start bar, click end bar -> draws VAH/VAL zone with POC, extends right indefinitely

namespace NinjaTrader.NinjaScript.DrawingTools
{
    public class RedTailVPZone : DrawingTool
    {
        #region Variables
        
        private List<double> volumes = new List<double>();
        private double highestPrice;
        private double lowestPrice;
        private double priceInterval;
        
        private bool isCalculated  = false;
        private int  buildingStep  = 0;
        
        // Store calculated prices so they survive property edits
        private double cahcedVAH = double.NaN;
        private double cachedPOC = double.NaN;
        private double cachedVAL = double.NaN;
        
        #endregion
        
        #region Anchors
        
        [Display(Order = 1)]
        public ChartAnchor StartAnchor { get; set; }
        
        [Display(Order = 2)]
        public ChartAnchor EndAnchor { get; set; }
        
        public override IEnumerable<ChartAnchor> Anchors
        {
            get { return new[] { StartAnchor, EndAnchor }; }
        }
        
        #endregion
        
        #region Properties
        
        [NinjaScriptProperty]
        [Range(10, 500)]
        [Display(Name = "Number of Volume Rows", Order = 1, GroupName = "1. Volume Profile")]
        public int NumberOfRows { get; set; }
        
        [NinjaScriptProperty]
        [Range(50, 90)]
        [Display(Name = "Value Area %", Order = 2, GroupName = "1. Volume Profile")]
        public int ValueAreaPercentage { get; set; }
        
        [XmlIgnore]
        [Display(Name = "Zone Fill Color", Order = 1, GroupName = "2. Zone Appearance")]
        public System.Windows.Media.Brush ZoneFillBrush { get; set; }
        
        [Browsable(false)]
        public string ZoneFillBrushSerializable
        {
            get { return Serialize.BrushToString(ZoneFillBrush); }
            set { ZoneFillBrush = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Zone Opacity %", Order = 2, GroupName = "2. Zone Appearance")]
        public int ZoneOpacity { get; set; }
        

        
        [XmlIgnore]
        [Display(Name = "POC Line Color", Order = 1, GroupName = "3. POC Line")]
        public System.Windows.Media.Brush POCBrush { get; set; }
        
        [Browsable(false)]
        public string POCBrushSerializable
        {
            get { return Serialize.BrushToString(POCBrush); }
            set { POCBrush = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Range(1, 5)]
        [Display(Name = "POC Line Thickness", Order = 2, GroupName = "3. POC Line")]
        public int POCThickness { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "POC Line Style", Order = 3, GroupName = "3. POC Line")]
        public DashStyleHelper POCLineStyle { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Show VAH/VAL Lines", Order = 1, GroupName = "4. VAH/VAL Lines")]
        public bool ShowVALines { get; set; }
        
        [XmlIgnore]
        [Display(Name = "VAH Line Color", Order = 2, GroupName = "4. VAH/VAL Lines")]
        public System.Windows.Media.Brush VAHBrush { get; set; }
        
        [Browsable(false)]
        public string VAHBrushSerializable
        {
            get { return Serialize.BrushToString(VAHBrush); }
            set { VAHBrush = Serialize.StringToBrush(value); }
        }
        
        [XmlIgnore]
        [Display(Name = "VAL Line Color", Order = 3, GroupName = "4. VAH/VAL Lines")]
        public System.Windows.Media.Brush VALBrush { get; set; }
        
        [Browsable(false)]
        public string VALBrushSerializable
        {
            get { return Serialize.BrushToString(VALBrush); }
            set { VALBrush = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Range(1, 5)]
        [Display(Name = "VAH/VAL Line Thickness", Order = 4, GroupName = "4. VAH/VAL Lines")]
        public int VALineThickness { get; set; }
        
        #endregion
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description  = "Fixed Range Volume Profile Zone - Draws VAH/VAL rectangle with POC line";
                Name         = "RedTail VP Zone";
                IsAutoScale  = false;
                DrawingState = DrawingState.Building;
                
                NumberOfRows        = 100;
                ValueAreaPercentage = 68;
                
                ZoneFillBrush       = System.Windows.Media.Brushes.DodgerBlue;
                ZoneOpacity         = 15;
                POCBrush     = System.Windows.Media.Brushes.Red;
                POCThickness = 2;
                POCLineStyle = DashStyleHelper.Solid;
                
                ShowVALines     = true;
                VAHBrush        = System.Windows.Media.Brushes.DodgerBlue;
                VALBrush        = System.Windows.Media.Brushes.DodgerBlue;
                VALineThickness = 1;
                
                StartAnchor = new ChartAnchor
                {
                    IsEditing   = true,
                    DrawingTool = this,
                    DisplayName = NinjaTrader.Custom.Resource.NinjaScriptDrawingToolAnchor,
                };
                EndAnchor = new ChartAnchor
                {
                    IsEditing   = true,
                    DrawingTool = this,
                    DisplayName = NinjaTrader.Custom.Resource.NinjaScriptDrawingToolAnchorEnd,
                };
            }
            else if (State == State.Configure)
            {
                buildingStep = 0;
            }
        }
        
        #region Mouse Interaction
        
        public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, System.Windows.Point point)
        {
            if (DrawingState == DrawingState.Building)
                return Cursors.Pen;
            
            if (DrawingState == DrawingState.Moving || DrawingState == DrawingState.Editing)
                return Cursors.SizeAll;
            
            if (isCalculated && !double.IsNaN(cahcedVAH) && !double.IsNaN(cachedVAL))
            {
                float vahY    = chartScale.GetYByValue(cahcedVAH);
                float valY    = chartScale.GetYByValue(cachedVAL);
                float topY    = Math.Min(vahY, valY);
                float bottomY = Math.Max(vahY, valY);
                
                System.Windows.Point startPt = StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
                System.Windows.Point endPt   = EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
                float leftX = (float)Math.Min(startPt.X, endPt.X);
                
                if (point.Y >= topY && point.Y <= bottomY && point.X >= leftX)
                    return Cursors.SizeAll;
            }
            
            return null;
        }
        
        public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (DrawingState == DrawingState.Building)
            {
                if (buildingStep == 0)
                {
                    dataPoint.CopyDataValues(StartAnchor);
                    StartAnchor.IsEditing = false;
                    
                    dataPoint.CopyDataValues(EndAnchor);
                    EndAnchor.IsEditing = true;
                    
                    buildingStep = 1;
                }
                else if (buildingStep == 1)
                {
                    dataPoint.CopyDataValues(EndAnchor);
                    EndAnchor.IsEditing = false;
                    
                    DrawingState = DrawingState.Normal;
                    IsSelected   = false;
                    buildingStep = 0;
                    
                    CalculateVolumeProfile(chartControl);
                }
            }
            else if (DrawingState == DrawingState.Normal)
            {
                System.Windows.Point clickPt = dataPoint.GetPoint(chartControl, chartPanel, chartScale);
                System.Windows.Point sPt     = StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
                System.Windows.Point ePt     = EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
                
                if ((clickPt - sPt).Length < 15)
                {
                    DrawingState = DrawingState.Editing;
                    StartAnchor.IsEditing = true;
                }
                else if ((clickPt - ePt).Length < 15)
                {
                    DrawingState = DrawingState.Editing;
                    EndAnchor.IsEditing = true;
                }
                else
                {
                    DrawingState = DrawingState.Moving;
                }
            }
        }
        
        public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (DrawingState == DrawingState.Building && buildingStep == 1)
            {
                dataPoint.CopyDataValues(EndAnchor);
                CalculateVolumeProfile(chartControl);
            }
            else if (DrawingState == DrawingState.Editing)
            {
                if (StartAnchor.IsEditing)
                {
                    dataPoint.CopyDataValues(StartAnchor);
                    CalculateVolumeProfile(chartControl);
                }
                else if (EndAnchor.IsEditing)
                {
                    dataPoint.CopyDataValues(EndAnchor);
                    CalculateVolumeProfile(chartControl);
                }
            }
        }
        
        public override void OnMouseUp(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
        {
            if (DrawingState == DrawingState.Building)
                return;
            
            if (DrawingState == DrawingState.Editing || DrawingState == DrawingState.Moving)
            {
                StartAnchor.IsEditing = false;
                EndAnchor.IsEditing   = false;
                DrawingState = DrawingState.Normal;
                CalculateVolumeProfile(chartControl);
            }
        }
        
        #endregion
        
        #region Volume Profile Calculation
        
        private void CalculateVolumeProfile(ChartControl chartControl)
        {
            isCalculated = false;
            
            try
            {
                if (chartControl == null || chartControl.BarsArray == null || chartControl.BarsArray.Count == 0)
                    return;
                
                ChartBars chartBars = chartControl.BarsArray[0];
                if (chartBars == null || chartBars.Bars == null || chartBars.Bars.Count < 2)
                    return;
                
                NinjaTrader.Data.Bars bars = chartBars.Bars;
                
                if (StartAnchor.Time == DateTime.MinValue || EndAnchor.Time == DateTime.MinValue)
                    return;
                if (StartAnchor.Time == EndAnchor.Time)
                    return;
                
                int startIdx = bars.GetBar(StartAnchor.Time);
                int endIdx   = bars.GetBar(EndAnchor.Time);
                
                int fromBar = Math.Min(startIdx, endIdx);
                int toBar   = Math.Max(startIdx, endIdx);
                
                fromBar = Math.Max(0, fromBar);
                toBar   = Math.Min(bars.Count - 1, toBar);
                
                if (fromBar >= toBar)
                    return;
                
                highestPrice = double.MinValue;
                lowestPrice  = double.MaxValue;
                
                for (int i = fromBar; i <= toBar; i++)
                {
                    highestPrice = Math.Max(highestPrice, bars.GetHigh(i));
                    lowestPrice  = Math.Min(lowestPrice, bars.GetLow(i));
                }
                
                if (highestPrice <= lowestPrice)
                    return;
                
                priceInterval = (highestPrice - lowestPrice) / (NumberOfRows - 1);
                if (priceInterval <= 0)
                    return;
                
                volumes.Clear();
                for (int i = 0; i < NumberOfRows; i++)
                    volumes.Add(0);
                
                for (int i = fromBar; i <= toBar; i++)
                {
                    double barLow    = bars.GetLow(i);
                    double barHigh   = bars.GetHigh(i);
                    double barVolume = bars.GetVolume(i);
                    
                    int minPriceIndex = (int)Math.Floor((barLow - lowestPrice) / priceInterval);
                    int maxPriceIndex = (int)Math.Ceiling((barHigh - lowestPrice) / priceInterval);
                    
                    minPriceIndex = Math.Max(0, Math.Min(minPriceIndex, NumberOfRows - 1));
                    maxPriceIndex = Math.Max(0, Math.Min(maxPriceIndex, NumberOfRows - 1));
                    
                    int touchedLevels = maxPriceIndex - minPriceIndex + 1;
                    if (touchedLevels > 0)
                    {
                        double volumePerLevel = barVolume / touchedLevels;
                        for (int j = minPriceIndex; j <= maxPriceIndex; j++)
                            volumes[j] += volumePerLevel;
                    }
                }
                
                double maxVolume = 0;
                int maxIndex = 0;
                for (int i = 0; i < volumes.Count; i++)
                {
                    if (volumes[i] > maxVolume)
                    {
                        maxVolume = volumes[i];
                        maxIndex  = i;
                    }
                }
                
                if (maxVolume == 0)
                    return;
                
                double sumVolume = 0;
                for (int i = 0; i < volumes.Count; i++)
                    sumVolume += volumes[i];
                
                double vaVolume = sumVolume * ValueAreaPercentage / 100.0;
                int vaUp   = maxIndex;
                int vaDown = maxIndex;
                double vaSum = maxVolume;
                
                while (vaSum < vaVolume)
                {
                    double vUp   = (vaUp < NumberOfRows - 1) ? volumes[vaUp + 1] : 0.0;
                    double vDown = (vaDown > 0) ? volumes[vaDown - 1] : 0.0;
                    
                    if (vUp == 0 && vDown == 0) break;
                    
                    if (vUp >= vDown) { vaSum += vUp; vaUp++; }
                    else              { vaSum += vDown; vaDown--; }
                }
                
                cachedPOC = lowestPrice + priceInterval * maxIndex;
                cahcedVAH = lowestPrice + priceInterval * vaUp;
                cachedVAL = lowestPrice + priceInterval * vaDown;
                
                isCalculated = true;
            }
            catch (Exception)
            {
                isCalculated = false;
            }
        }
        
        #endregion
        
        #region Rendering
        
        public override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            // Auto-recalculate if we lost state (e.g. after property dialog Apply)
            // but still have valid anchors
            if (!isCalculated && DrawingState != DrawingState.Building
                && StartAnchor.Time != DateTime.MinValue 
                && EndAnchor.Time != DateTime.MinValue
                && StartAnchor.Time != EndAnchor.Time)
            {
                CalculateVolumeProfile(chartControl);
            }
            
            // Also recalc during live preview while building
            if (DrawingState == DrawingState.Building && buildingStep == 1 && !isCalculated)
            {
                CalculateVolumeProfile(chartControl);
            }
            
            if (!isCalculated || double.IsNaN(cahcedVAH) || double.IsNaN(cachedVAL) || double.IsNaN(cachedPOC))
                return;
            
            if (RenderTarget == null || chartControl == null || chartScale == null)
                return;
            
            ChartPanel chartPanel = chartControl.ChartPanels[chartScale.PanelIndex];
            if (chartPanel == null)
                return;
            
            float vahY = chartScale.GetYByValue(cahcedVAH);
            float valY = chartScale.GetYByValue(cachedVAL);
            float pocY = chartScale.GetYByValue(cachedPOC);
            
            float startX     = (float)StartAnchor.GetPoint(chartControl, chartPanel, chartScale).X;
            float endDrawX   = (float)EndAnchor.GetPoint(chartControl, chartPanel, chartScale).X;
            float zoneStartX = Math.Min(startX, endDrawX);
            float rightEdge  = chartPanel.X + chartPanel.W;
            
            float topY    = Math.Min(vahY, valY);
            float bottomY = Math.Max(vahY, valY);
            
            if (bottomY - topY < 1) return;
            
            SharpDX.Direct2D1.SolidColorBrush dxFillBrush   = null;
            SharpDX.Direct2D1.SolidColorBrush dxPocBrush    = null;
            SharpDX.Direct2D1.SolidColorBrush dxVahBrush    = null;
            SharpDX.Direct2D1.SolidColorBrush dxValBrush    = null;
            StrokeStyle pocStrokeStyle = null;
            
            try
            {
                // Zone fill
                dxFillBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,
                    WpfToColor4(ZoneFillBrush, ZoneOpacity / 100.0f));
                
                SharpDX.RectangleF zoneRect = new SharpDX.RectangleF(
                    zoneStartX, topY, rightEdge - zoneStartX, bottomY - topY);
                RenderTarget.FillRectangle(zoneRect, dxFillBrush);
                
                // POC line
                dxPocBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, WpfToColor4(POCBrush));
                
                SharpDX.Vector2 pocStart = new SharpDX.Vector2(zoneStartX, pocY);
                SharpDX.Vector2 pocEnd   = new SharpDX.Vector2(rightEdge, pocY);
                
                if (POCLineStyle != DashStyleHelper.Solid)
                {
                    StrokeStyleProperties strokeProps = new StrokeStyleProperties();
                    switch (POCLineStyle)
                    {
                        case DashStyleHelper.Dash:       strokeProps.DashStyle = SharpDX.Direct2D1.DashStyle.Dash; break;
                        case DashStyleHelper.Dot:        strokeProps.DashStyle = SharpDX.Direct2D1.DashStyle.Dot; break;
                        case DashStyleHelper.DashDot:    strokeProps.DashStyle = SharpDX.Direct2D1.DashStyle.DashDot; break;
                        case DashStyleHelper.DashDotDot: strokeProps.DashStyle = SharpDX.Direct2D1.DashStyle.DashDotDot; break;
                    }
                    pocStrokeStyle = new StrokeStyle(RenderTarget.Factory, strokeProps);
                    RenderTarget.DrawLine(pocStart, pocEnd, dxPocBrush, POCThickness, pocStrokeStyle);
                }
                else
                {
                    RenderTarget.DrawLine(pocStart, pocEnd, dxPocBrush, POCThickness);
                }
                
                // VAH/VAL lines
                if (ShowVALines)
                {
                    dxVahBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, WpfToColor4(VAHBrush));
                    dxValBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, WpfToColor4(VALBrush));
                    
                    RenderTarget.DrawLine(new SharpDX.Vector2(zoneStartX, vahY), new SharpDX.Vector2(rightEdge, vahY), dxVahBrush, VALineThickness);
                    RenderTarget.DrawLine(new SharpDX.Vector2(zoneStartX, valY), new SharpDX.Vector2(rightEdge, valY), dxValBrush, VALineThickness);
                }
            }
            finally
            {
                if (dxFillBrush != null)   dxFillBrush.Dispose();
                if (dxPocBrush != null)    dxPocBrush.Dispose();
                if (dxVahBrush != null)    dxVahBrush.Dispose();
                if (dxValBrush != null)    dxValBrush.Dispose();
                if (pocStrokeStyle != null) pocStrokeStyle.Dispose();
            }
        }
        
        private static SharpDX.Color4 WpfToColor4(System.Windows.Media.Brush wpfBrush, float opacity = 1.0f)
        {
            System.Windows.Media.SolidColorBrush scb = wpfBrush as System.Windows.Media.SolidColorBrush;
            if (scb != null)
            {
                return new SharpDX.Color4(
                    scb.Color.R / 255.0f,
                    scb.Color.G / 255.0f,
                    scb.Color.B / 255.0f,
                    opacity * (scb.Color.A / 255.0f));
            }
            return new SharpDX.Color4(1f, 1f, 1f, opacity);
        }
        
        #endregion
        
        #region Overrides
        
        public override bool IsVisibleOnChart(ChartControl chartControl, ChartScale chartScale, DateTime firstTimeOnChart, DateTime lastTimeOnChart)
        {
            if (DrawingState == DrawingState.Building)
                return true;
            
            // Even if isCalculated is false, return true if anchors are valid
            // so OnRender gets a chance to recalculate
            if (StartAnchor.Time != DateTime.MinValue && EndAnchor.Time != DateTime.MinValue
                && StartAnchor.Time != EndAnchor.Time)
                return true;
            
            return isCalculated;
        }
        
        public override System.Windows.Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
        {
            if (!isCalculated)
                return new System.Windows.Point[0];
            
            ChartPanel chartPanel = chartControl.ChartPanels[chartScale.PanelIndex];
            
            System.Windows.Point startPoint = StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
            System.Windows.Point endPoint   = EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
            
            float pocY = chartScale.GetYByValue(cachedPOC);
            
            return new[]
            {
                startPoint,
                endPoint,
                new System.Windows.Point((startPoint.X + endPoint.X) / 2, pocY)
            };
        }
        
        #endregion
    }
}
