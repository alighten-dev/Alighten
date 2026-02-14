#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using System.Windows.Automation.Provider;
// using System.Windows.Forms; // Removed to prevent "Button" ambiguity with WPF
using System.IO;
using NinjaTrader.Core;
using NinjaTrader.NinjaScript.Indicators;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators
{
    public class AlightenButtonPanelV0004 : Indicator
    {

        private ChartTrader chartTrader;
        private System.Windows.Controls.Grid chartTraderGrid;
        private System.Windows.Controls.Grid chartTraderButtonsGrid;
        private System.Windows.Controls.RowDefinition addedRow;
        private System.Windows.Controls.Grid panelGrid;

        private NinjaTrader.Gui.Tools.AccountSelector xAcSelector;
        private NinjaTrader.Gui.Tools.InstrumentSelector xInSelector;

        private Button btnFlattenEverything;

        private double lastClose = 0;
        private string lastOcoId;
        private double lastBracketTargetBase;
        private bool lastBracketIsLong;
        private int lastBracketQty;

        private volatile bool isFlatteningAll = false;

        // Order Flow Indicator Reference
        private AlightenOrderFlowToolsV0005 orderFlowIndicator;

        // Order Flow UI Controls
        private ComboBox presetComboBox;
        private Button btnNewPreset;
        private Button btnSavePreset;
        private Button btnDeletePreset;
        private Button btnUpdateOrderFlow;

        // Order Flow TextBoxes
        private TextBox txtMinVol;
        private TextBox txtMaxMarkerSize;
        private TextBox txtTrapLookBars;
        private TextBox txtTrapMoveTicks;
        private TextBox txtTrapMinVol;
        private TextBox txtTrapMinDom;
        private TextBox txtSITrapLookBars;
        private TextBox txtSITrapMoveTicks;
        private TextBox txtSIAggInterval;
        private TextBox txtSIImbFact;
        private TextBox txtSIStackedLevels;
        private TextBox txtSIMinRowVol;

        // Preset Management
        private Dictionary<string, Dictionary<string, object>> presets;

        [NinjaScriptProperty]
        [Display(Name = "Breakeven1 + X Ticks", Order = 0, GroupName = "Parameters")]
        public int Breakeven1PlusTicks { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name = "Breakeven2 + X Ticks", Order = 1, GroupName = "Parameters")]
        public int Breakeven2PlusTicks { get; set; } = 6;

        [NinjaScriptProperty]
        [Display(Name = "Price + X Ticks", Order = 2, GroupName = "Parameters")]
        public int PricePlusTicks { get; set; } = 40;

        [NinjaScriptProperty]
        [Display(Name = "Entry + X Ticks", Order = 3, GroupName = "Parameters")]
        public int EntryPlusTicks { get; set; } = 3;

        [NinjaScriptProperty]
        [Display(Name = "Bracket Stop (Ticks)", Order = 4, GroupName = "Parameters")]
        public int BracketStopTicks { get; set; } = 40;

        [NinjaScriptProperty]
        [Display(Name = "Bracket Profit (Ticks)", Order = 5, GroupName = "Parameters")]
        public int BracketProfitTicks { get; set; } = 40;

        [NinjaScriptProperty]
        [Display(Name = "Flatten All Pause (Milliseconds)", Order = 6, GroupName = "Parameters")]
        public int FlattenAllPause { get; set; } = 50;

        [NinjaScriptProperty]
        [Display(Name = "Flatten All Tries", Order = 7, GroupName = "Parameters")]
        public int FlattenAllTries { get; set; } = 6;

        #region Order Flow Properties

        [NinjaScriptProperty]
        [Display(Name = "OF Preset Name", Order = 0, GroupName = "Order Flow Parameters")]
        public string OFPresetName { get; set; } = "GC (Default)";

        [Browsable(false)]
        [NinjaScriptProperty]
        public string OFCustomPresets { get; set; } = "";

        // Large Trades
        [NinjaScriptProperty]
        [Range(0, int.MaxValue)]
        [Display(Name = "OF Min Volume For Marker", Order = 10, GroupName = "Order Flow Parameters")]
        public int OFMinimumVolumeForMarker { get; set; } = 10;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "OF Max Marker Size", Order = 11, GroupName = "Order Flow Parameters")]
        public int OFMaximumMarkerSize { get; set; } = 50;

        // Trapped Traders
        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "OF Trap Lookahead Bars", Order = 20, GroupName = "Order Flow Parameters")]
        public int OFTrapLookaheadBars { get; set; } = 1;

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "OF Trap Move Ticks", Order = 21, GroupName = "Order Flow Parameters")]
        public int OFTrapMoveTicks { get; set; } = 15;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "OF Trap Min Total Volume", Order = 22, GroupName = "Order Flow Parameters")]
        public int OFTrapMinTotalVolume { get; set; } = 10;

        [NinjaScriptProperty]
        [Range(50, 100)]
        [Display(Name = "OF Trap Min Dominance %", Order = 23, GroupName = "Order Flow Parameters")]
        public int OFTrapMinDominancePct { get; set; } = 70;

        // Stacked Imbalance
        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "OF SI Trap Lookahead Bars", Order = 30, GroupName = "Order Flow Parameters")]
        public int OFSITrapLookaheadBars { get; set; } = 1;

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "OF SI Trap Move Ticks", Order = 31, GroupName = "Order Flow Parameters")]
        public int OFSITrapMoveTicks { get; set; } = 20;

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "OF SI Aggregation Interval", Order = 32, GroupName = "Order Flow Parameters")]
        public int OFSIAggregationIntervalTicks { get; set; } = 1;

        [NinjaScriptProperty]
        [Range(1.01, 50)]
        [Display(Name = "OF SI Imbalance Factor", Order = 33, GroupName = "Order Flow Parameters")]
        public double OFSIImbFact { get; set; } = 3.0;

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "OF SI Stacked Levels", Order = 34, GroupName = "Order Flow Parameters")]
        public int OFSIStackedLevels { get; set; } = 2;

        [NinjaScriptProperty]
        [Range(0, 1000000)]
        [Display(Name = "OF SI Min Row Volume", Order = 35, GroupName = "Order Flow Parameters")]
        public double OFSIMinRowVolume { get; set; } = 0;

        #endregion

        #region Button Color Properties

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Button1 Color", GroupName = "Button Color Settings", Order = 1)]
        public Brush Button1Color { get; set; } = Brushes.Blue;
        [Browsable(false)]
        public string Button1ColorSerialize
        {
            get => Serialize.BrushToString(Button1Color);
            set => Button1Color = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Button2 Color", GroupName = "Button Color Settings", Order = 2)]
        public Brush Button2Color { get; set; } = Brushes.Blue;
        [Browsable(false)]
        public string Button2ColorSerialize
        {
            get => Serialize.BrushToString(Button2Color);
            set => Button2Color = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Button3 Color", GroupName = "Button Color Settings", Order = 3)]
        public Brush Button3Color { get; set; } = Brushes.Blue;
        [Browsable(false)]
        public string Button3ColorSerialize
        {
            get => Serialize.BrushToString(Button3Color);
            set => Button3Color = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Button4 Color", GroupName = "Button Color Settings", Order = 4)]
        public Brush Button4Color { get; set; } = Brushes.Blue;
        [Browsable(false)]
        public string Button4ColorSerialize
        {
            get => Serialize.BrushToString(Button4Color);
            set => Button4Color = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Button5 Color", GroupName = "Button Color Settings", Order = 5)]
        public Brush Button5Color { get; set; } = Brushes.Blue;
        [Browsable(false)]
        public string Button5ColorSerialize
        {
            get => Serialize.BrushToString(Button5Color);
            set => Button5Color = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Button6 Color", GroupName = "Button Color Settings", Order = 6)]
        public Brush Button6Color { get; set; } = Brushes.Blue;
        [Browsable(false)]
        public string Button6ColorSerialize
        {
            get => Serialize.BrushToString(Button6Color);
            set => Button6Color = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Button7 Color", GroupName = "Button Color Settings", Order = 7)]
        public Brush Button7Color { get; set; } = Brushes.DimGray;
        [Browsable(false)]
        public string Button7ColorSerialize
        {
            get => Serialize.BrushToString(Button7Color);
            set => Button7Color = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Button8 Color", GroupName = "Button Color Settings", Order = 8)]
        public Brush Button8Color { get; set; } = Brushes.DimGray;
        [Browsable(false)]
        public string Button8ColorSerialize
        {
            get => Serialize.BrushToString(Button8Color);
            set => Button8Color = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Button9 Color", GroupName = "Button Color Settings", Order = 9)]
        public Brush Button9Color { get; set; } = Brushes.DimGray;
        [Browsable(false)]
        public string Button9ColorSerialize
        {
            get => Serialize.BrushToString(Button9Color);
            set => Button9Color = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Button10 Color", GroupName = "Button Color Settings", Order = 10)]
        public Brush Button10Color { get; set; } = Brushes.DimGray;
        [Browsable(false)]
        public string Button10ColorSerialize
        {
            get => Serialize.BrushToString(Button10Color);
            set => Button10Color = Serialize.StringToBrush(value);
        }

        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Flatten Everything Color", GroupName = "Button Color Settings", Order = 0)]
        public Brush FlattenEverythingColor { get; set; } = Brushes.Red;

        [Browsable(false)]
        public string FlattenEverythingColorSerialize
        {
            get => Serialize.BrushToString(FlattenEverythingColor);
            set => FlattenEverythingColor = Serialize.StringToBrush(value);
        }


        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Button panel with Order Flow parameter controls";
                Name = "AlightenButtonPanelV0004";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;
            }
            else if (State == State.Configure)
            {
            }
            else if (State == State.DataLoaded)
            {
                // Defer UI creation until chart is ready
                ChartControl.Dispatcher.BeginInvoke(new Action(CreateWPFControls));


            }
            else if (State == State.Terminated)
            {
                // Clean up when indicator is removed
                ChartControl?.Dispatcher.BeginInvoke(new Action(RemoveWPFControls));

            }
        }

        private void CreateWPFControls()
        {
            // 1) grab the ChartTrader
            chartTrader = ChartControl.OwnerChart.ChartTrader;
            if (chartTrader == null || panelGrid != null)
                return;

            // 2) find its root grid and the built-in buttons grid
            chartTraderGrid = chartTrader.Content as Grid;
            if (chartTraderGrid == null) return;
            chartTraderButtonsGrid = chartTraderGrid.Children[0] as Grid;
            if (chartTraderButtonsGrid == null) return;

            // 3) Initialize presets
            InitializePresets();
            LoadCustomPresets();

            // 4) Try to locate the OrderFlow indicator
            orderFlowIndicator = ChartControl.Indicators
                .OfType<AlightenOrderFlowToolsV0005>()
                .FirstOrDefault();

            // 5) build our panel grid
            panelGrid = new Grid { Margin = new Thickness(4) };

            // Define rows: Flatten Everything + 6 button rows + Order Flow section
            for (int row = 0; row < 8; row++)
                panelGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // two columns
            for (int col = 0; col < 2; col++)
                panelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 6) Create trading buttons (same as V0002)
            btnFlattenEverything = CreateButton("FLATTEN EVERYTHING", FlattenEverythingColor, BtnFlattenEverything_Click);

            var bePlus1 = CreateButton($"BE + {Breakeven1PlusTicks}", Button1Color, BtnBEPlus1_Click);
            var bePlus2 = CreateButton($"BE + {Breakeven2PlusTicks}", Button2Color, BtnBEPlus2_Click);
            var pricePlus = CreateButton($"Price + {PricePlusTicks}", Button3Color, BtnPricePlus_Click);
            var entryPlus = CreateButton($"Entry + {EntryPlusTicks}", Button4Color, BtnEntryPlus_Click);
            var bracket = CreateButton("Bracket", Button5Color, BtnBracket_Click);
            var addStop = CreateButton("Add Stop", Button6Color, BtnAddStop_Click);
            var half = CreateButton("Half", Button7Color, BtnHalf_Click);
            var dbl = CreateButton("Double", Button8Color, BtnDouble_Click);
            var naked = CreateButton("Naked", Button9Color, BtnNaked_Click);
            var split = CreateButton("Split", Button10Color, BtnSplit_Click);

            // 7) Position Flatten Everything Button
            Grid.SetRow(btnFlattenEverything, 0);
            Grid.SetColumn(btnFlattenEverything, 0);
            Grid.SetColumnSpan(btnFlattenEverything, 2);
            panelGrid.Children.Add(btnFlattenEverything);

            // 8) position trading buttons in the grid
            Grid.SetRow(bePlus1, 1); Grid.SetColumn(bePlus1, 0);
            Grid.SetRow(bePlus2, 1); Grid.SetColumn(bePlus2, 1);
            Grid.SetRow(pricePlus, 2); Grid.SetColumn(pricePlus, 0);
            Grid.SetRow(entryPlus, 2); Grid.SetColumn(entryPlus, 1);
            Grid.SetRow(bracket, 3); Grid.SetColumn(bracket, 0);
            Grid.SetRow(addStop, 3); Grid.SetColumn(addStop, 1);
            Grid.SetRow(half, 4); Grid.SetColumn(half, 0);
            Grid.SetRow(dbl, 4); Grid.SetColumn(dbl, 1);
            Grid.SetRow(naked, 5); Grid.SetColumn(naked, 0);
            Grid.SetRow(split, 5); Grid.SetColumn(split, 1);

            panelGrid.Children.Add(bePlus1);
            panelGrid.Children.Add(bePlus2);
            panelGrid.Children.Add(pricePlus);
            panelGrid.Children.Add(entryPlus);
            panelGrid.Children.Add(bracket);
            panelGrid.Children.Add(addStop);
            panelGrid.Children.Add(half);
            panelGrid.Children.Add(dbl);
            panelGrid.Children.Add(naked);
            panelGrid.Children.Add(split);

            // 9) Create Order Flow Parameters section
            var orderFlowSection = CreateOrderFlowSection();
            Grid.SetRow(orderFlowSection, 6);
            Grid.SetColumn(orderFlowSection, 0);
            Grid.SetColumnSpan(orderFlowSection, 2);
            Grid.SetRowSpan(orderFlowSection, 2);
            panelGrid.Children.Add(orderFlowSection);

            // 10) inject into the ChartTrader buttons grid
            addedRow = new RowDefinition { Height = GridLength.Auto };
            chartTraderButtonsGrid.RowDefinitions.Add(addedRow);
            Grid.SetRow(panelGrid, chartTraderButtonsGrid.RowDefinitions.Count - 1);
            Grid.SetColumnSpan(panelGrid, chartTraderButtonsGrid.ColumnDefinitions.Count);
            chartTraderButtonsGrid.Children.Add(panelGrid);
        }

        private UIElement CreateOrderFlowSection()
        {
            var mainGrid = new Grid { Margin = new Thickness(2) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Row 0: Title
            var title = new TextBlock
            {
                Text = "Order Flow Parameters",
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Margin = new Thickness(2),
                Foreground = Brushes.White
            };
            Grid.SetRow(title, 0);
            mainGrid.Children.Add(title);

            // Row 1: Preset selector
            var presetPanel = CreatePresetPanel();
            Grid.SetRow(presetPanel, 1);
            mainGrid.Children.Add(presetPanel);

            // Row 2: Large Trades Expander
            var largeTrades = CreateLargeTradesExpander();
            Grid.SetRow(largeTrades, 2);
            mainGrid.Children.Add(largeTrades);

            // Row 3: Trapped Traders Expander
            var trappedTraders = CreateTrappedTradersExpander();
            Grid.SetRow(trappedTraders, 3);
            mainGrid.Children.Add(trappedTraders);

            // Row 4: Stacked Imbalance Expander
            var stackedImb = CreateStackedImbalanceExpander();
            Grid.SetRow(stackedImb, 4);
            mainGrid.Children.Add(stackedImb);

            // Row 5: Update Indicator Button (global, not nested in any expander)
            btnUpdateOrderFlow = CreateButton("Update Indicator", Brushes.Green, BtnUpdateOrderFlow_Click);
            btnUpdateOrderFlow.Height = 25;
            Grid.SetRow(btnUpdateOrderFlow, 5);
            mainGrid.Children.Add(btnUpdateOrderFlow);

            return mainGrid;
        }

        private UIElement CreatePresetPanel()
        {
            var grid = new Grid { Margin = new Thickness(2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Preset ComboBox
            presetComboBox = new ComboBox
            {
                Margin = new Thickness(2),
                Height = 25
            };
            foreach (var preset in presets.Keys)
                presetComboBox.Items.Add(preset);
            presetComboBox.SelectedItem = OFPresetName;
            presetComboBox.SelectionChanged += PresetComboBox_SelectionChanged;
            Grid.SetColumn(presetComboBox, 0);
            grid.Children.Add(presetComboBox);

            // New button
            btnNewPreset = CreateSmallButton("New", BtnNewPreset_Click);
            Grid.SetColumn(btnNewPreset, 1);
            grid.Children.Add(btnNewPreset);

            // Save button
            btnSavePreset = CreateSmallButton("Save", BtnSavePreset_Click);
            Grid.SetColumn(btnSavePreset, 2);
            grid.Children.Add(btnSavePreset);

            // Delete button
            btnDeletePreset = CreateSmallButton("Del", BtnDeletePreset_Click);
            Grid.SetColumn(btnDeletePreset, 3);
            grid.Children.Add(btnDeletePreset);

            return grid;
        }

        private Expander CreateLargeTradesExpander()
        {
            var expander = new Expander
            {
                Header = "Large Trades",
                IsExpanded = false,
                Margin = new Thickness(2),
                Foreground = Brushes.White
            };

            var grid = new Grid { Margin = new Thickness(2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Min Volume For Marker
            var label = CreateLabel("Min Volume For Marker:");
            Grid.SetRow(label, 0);
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            txtMinVol = CreateTextBox(OFMinimumVolumeForMarker.ToString());
            Grid.SetRow(txtMinVol, 0);
            Grid.SetColumn(txtMinVol, 1);
            grid.Children.Add(txtMinVol);

            // Max Marker Size
            var label2 = CreateLabel("Max Marker Size:");
            Grid.SetRow(label2, 1);
            Grid.SetColumn(label2, 0);
            grid.Children.Add(label2);

            txtMaxMarkerSize = CreateTextBox(OFMaximumMarkerSize.ToString());
            Grid.SetRow(txtMaxMarkerSize, 1);
            Grid.SetColumn(txtMaxMarkerSize, 1);
            grid.Children.Add(txtMaxMarkerSize);

            expander.Content = grid;
            return expander;
        }

        private Expander CreateTrappedTradersExpander()
        {
            var expander = new Expander
            {
                Header = "Trapped Traders",
                IsExpanded = false,
                Margin = new Thickness(2),
                Foreground = Brushes.White
            };

            var grid = new Grid { Margin = new Thickness(2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 4; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Trap Lookahead Bars
            var label1 = CreateLabel("Trap Lookahead Bars:");
            Grid.SetRow(label1, 0);
            Grid.SetColumn(label1, 0);
            grid.Children.Add(label1);

            txtTrapLookBars = CreateTextBox(OFTrapLookaheadBars.ToString());
            Grid.SetRow(txtTrapLookBars, 0);
            Grid.SetColumn(txtTrapLookBars, 1);
            grid.Children.Add(txtTrapLookBars);

            // Trap Move Ticks
            var label2 = CreateLabel("Trap Move Ticks:");
            Grid.SetRow(label2, 1);
            Grid.SetColumn(label2, 0);
            grid.Children.Add(label2);

            txtTrapMoveTicks = CreateTextBox(OFTrapMoveTicks.ToString());
            Grid.SetRow(txtTrapMoveTicks, 1);
            Grid.SetColumn(txtTrapMoveTicks, 1);
            grid.Children.Add(txtTrapMoveTicks);

            // Trap Min Total Volume
            var label3 = CreateLabel("Trap Min Total Volume:");
            Grid.SetRow(label3, 2);
            Grid.SetColumn(label3, 0);
            grid.Children.Add(label3);

            txtTrapMinVol = CreateTextBox(OFTrapMinTotalVolume.ToString());
            Grid.SetRow(txtTrapMinVol, 2);
            Grid.SetColumn(txtTrapMinVol, 1);
            grid.Children.Add(txtTrapMinVol);

            // Trap Min Dominance %
            var label4 = CreateLabel("Trap Min Dominance %:");
            Grid.SetRow(label4, 3);
            Grid.SetColumn(label4, 0);
            grid.Children.Add(label4);

            txtTrapMinDom = CreateTextBox(OFTrapMinDominancePct.ToString());
            Grid.SetRow(txtTrapMinDom, 3);
            Grid.SetColumn(txtTrapMinDom, 1);
            grid.Children.Add(txtTrapMinDom);

            expander.Content = grid;
            return expander;
        }

        private Expander CreateStackedImbalanceExpander()
        {
            var expander = new Expander
            {
                Header = "Stacked Imbalance",
                IsExpanded = false,
                Margin = new Thickness(2),
                Foreground = Brushes.White
            };

            var grid = new Grid { Margin = new Thickness(2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < 7; i++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // SI Trap Lookahead Bars
            var label1 = CreateLabel("SI Trap Lookahead Bars:");
            Grid.SetRow(label1, 0);
            Grid.SetColumn(label1, 0);
            grid.Children.Add(label1);

            txtSITrapLookBars = CreateTextBox(OFSITrapLookaheadBars.ToString());
            Grid.SetRow(txtSITrapLookBars, 0);
            Grid.SetColumn(txtSITrapLookBars, 1);
            grid.Children.Add(txtSITrapLookBars);

            // SI Trap Move Ticks
            var label2 = CreateLabel("SI Trap Move Ticks:");
            Grid.SetRow(label2, 1);
            Grid.SetColumn(label2, 0);
            grid.Children.Add(label2);

            txtSITrapMoveTicks = CreateTextBox(OFSITrapMoveTicks.ToString());
            Grid.SetRow(txtSITrapMoveTicks, 1);
            Grid.SetColumn(txtSITrapMoveTicks, 1);
            grid.Children.Add(txtSITrapMoveTicks);

            // SI Aggregation Interval
            var label3 = CreateLabel("SI Aggregation Interval:");
            Grid.SetRow(label3, 2);
            Grid.SetColumn(label3, 0);
            grid.Children.Add(label3);

            txtSIAggInterval = CreateTextBox(OFSIAggregationIntervalTicks.ToString());
            Grid.SetRow(txtSIAggInterval, 2);
            Grid.SetColumn(txtSIAggInterval, 1);
            grid.Children.Add(txtSIAggInterval);

            // SI Imbalance Factor
            var label4 = CreateLabel("SI Imbalance Factor:");
            Grid.SetRow(label4, 3);
            Grid.SetColumn(label4, 0);
            grid.Children.Add(label4);

            txtSIImbFact = CreateTextBox(OFSIImbFact.ToString());
            Grid.SetRow(txtSIImbFact, 3);
            Grid.SetColumn(txtSIImbFact, 1);
            grid.Children.Add(txtSIImbFact);

            // SI Stacked Levels
            var label5 = CreateLabel("SI Stacked Levels:");
            Grid.SetRow(label5, 4);
            Grid.SetColumn(label5, 0);
            grid.Children.Add(label5);

            txtSIStackedLevels = CreateTextBox(OFSIStackedLevels.ToString());
            Grid.SetRow(txtSIStackedLevels, 4);
            Grid.SetColumn(txtSIStackedLevels, 1);
            grid.Children.Add(txtSIStackedLevels);

            // SI Min Row Volume
            var label6 = CreateLabel("SI Min Row Volume:");
            Grid.SetRow(label6, 5);
            Grid.SetColumn(label6, 0);
            grid.Children.Add(label6);

            txtSIMinRowVol = CreateTextBox(OFSIMinRowVolume.ToString());
            Grid.SetRow(txtSIMinRowVol, 5);
            Grid.SetColumn(txtSIMinRowVol, 1);
            grid.Children.Add(txtSIMinRowVol);


            expander.Content = grid;
            return expander;
        }

        private TextBlock CreateLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                Margin = new Thickness(2),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White,
                FontSize = 10
            };
        }

        private TextBox CreateTextBox(string text)
        {
            var tb = new TextBox
            {
                Text = text,
                Margin = new Thickness(2),
                Height = 20,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 10
            };
            // Intercept PreviewKeyDown so chart never sees the keystroke
            // We must manually handle text because marking PreviewKeyDown
            // as handled prevents WPF from generating TextInput events.
            tb.PreviewKeyDown += TextBox_PreviewKeyDown;
            return tb;
        }

        private void TextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;

            // Allow Tab / Escape to pass through for normal navigation
            if (e.Key == System.Windows.Input.Key.Tab ||
                e.Key == System.Windows.Input.Key.Escape)
                return;

            // Enter → move focus away from the textbox
            if (e.Key == System.Windows.Input.Key.Enter ||
                e.Key == System.Windows.Input.Key.Return)
            {
                System.Windows.Input.Keyboard.ClearFocus();
                e.Handled = true;
                return;
            }

            // Mark handled NOW so chart never sees the key
            e.Handled = true;

            // Arrow keys for cursor movement
            if (e.Key == System.Windows.Input.Key.Left)
            { if (tb.CaretIndex > 0) tb.CaretIndex--; return; }
            if (e.Key == System.Windows.Input.Key.Right)
            { if (tb.CaretIndex < tb.Text.Length) tb.CaretIndex++; return; }
            if (e.Key == System.Windows.Input.Key.Home)
            { tb.CaretIndex = 0; return; }
            if (e.Key == System.Windows.Input.Key.End)
            { tb.CaretIndex = tb.Text.Length; return; }

            // Backspace
            if (e.Key == System.Windows.Input.Key.Back)
            {
                if (tb.SelectionLength > 0)
                {
                    int start = tb.SelectionStart;
                    tb.Text = tb.Text.Remove(start, tb.SelectionLength);
                    tb.CaretIndex = start;
                }
                else if (tb.CaretIndex > 0)
                {
                    int idx = tb.CaretIndex;
                    tb.Text = tb.Text.Remove(idx - 1, 1);
                    tb.CaretIndex = idx - 1;
                }
                return;
            }

            // Delete
            if (e.Key == System.Windows.Input.Key.Delete)
            {
                if (tb.SelectionLength > 0)
                {
                    int start = tb.SelectionStart;
                    tb.Text = tb.Text.Remove(start, tb.SelectionLength);
                    tb.CaretIndex = start;
                }
                else if (tb.CaretIndex < tb.Text.Length)
                {
                    tb.Text = tb.Text.Remove(tb.CaretIndex, 1);
                }
                return;
            }

            // Select-all (Ctrl+A)
            if (e.Key == System.Windows.Input.Key.A &&
                System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                tb.SelectAll();
                return;
            }

            // Convert key to a numeric character (digits, period, minus)
            string ch = KeyToNumericChar(e.Key);
            if (ch != null)
            {
                if (tb.SelectionLength > 0)
                {
                    int start = tb.SelectionStart;
                    tb.Text = tb.Text.Remove(start, tb.SelectionLength);
                    tb.CaretIndex = start;
                }
                int pos = tb.CaretIndex;
                tb.Text = tb.Text.Insert(pos, ch);
                tb.CaretIndex = pos + ch.Length;
            }
            // Any other key is simply swallowed (handled = true already set)
        }

        private string KeyToNumericChar(System.Windows.Input.Key key)
        {
            if (key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9)
                return ((char)('0' + (key - System.Windows.Input.Key.D0))).ToString();
            if (key >= System.Windows.Input.Key.NumPad0 && key <= System.Windows.Input.Key.NumPad9)
                return ((char)('0' + (key - System.Windows.Input.Key.NumPad0))).ToString();
            if (key == System.Windows.Input.Key.OemPeriod || key == System.Windows.Input.Key.Decimal)
                return ".";
            if (key == System.Windows.Input.Key.OemMinus || key == System.Windows.Input.Key.Subtract)
                return "-";
            return null;
        }

        private Button CreateButton(string text, Brush background, RoutedEventHandler handler)
        {
            var b = new Button
            {
                Content = text,
                Background = background,
                Foreground = Brushes.White,
                FontSize = 14,
                FontWeight = FontWeights.Normal,
                Height = 30,
                MinWidth = 80,
                Margin = new Thickness(2),
                Padding = new Thickness(4, 2, 4, 2),
                Style = Application.Current.TryFindResource("Button") as Style
            };
            b.Click += handler;
            return b;
        }

        private Button CreateSmallButton(string text, RoutedEventHandler handler)
        {
            var b = new Button
            {
                Content = text,
                Background = Brushes.DarkSlateGray,
                Foreground = Brushes.White,
                FontSize = 10,
                Height = 25,
                Margin = new Thickness(2),
                Padding = new Thickness(2),
                Style = Application.Current.TryFindResource("Button") as Style
            };
            b.Click += handler;
            return b;
        }

        #region Preset Management

        private void InitializePresets()
        {
            presets = new Dictionary<string, Dictionary<string, object>>
            {
                ["GC (Default)"] = new Dictionary<string, object>
                {
                    ["MinimumVolumeForMarker"] = 10,
                    ["MaximumMarkerSize"] = 50,
                    ["TrapLookaheadBars"] = 1,
                    ["TrapMoveTicks"] = 15,
                    ["TrapMinTotalVolume"] = 10,
                    ["TrapMinDominancePct"] = 70,
                    ["SITrapLookaheadBars"] = 1,
                    ["SITrapMoveTicks"] = 20,
                    ["SIAggregationIntervalTicks"] = 1,
                    ["SIImbFact"] = 3.0,
                    ["SIStackedLevels"] = 2,
                    ["SIMinRowVolume"] = 0.0
                },
                ["ES"] = new Dictionary<string, object>
                {
                    ["MinimumVolumeForMarker"] = 50,
                    ["MaximumMarkerSize"] = 80,
                    ["TrapLookaheadBars"] = 1,
                    ["TrapMoveTicks"] = 10,
                    ["TrapMinTotalVolume"] = 50,
                    ["TrapMinDominancePct"] = 70,
                    ["SITrapLookaheadBars"] = 1,
                    ["SITrapMoveTicks"] = 10,
                    ["SIAggregationIntervalTicks"] = 4,
                    ["SIImbFact"] = 3.0,
                    ["SIStackedLevels"] = 2,
                    ["SIMinRowVolume"] = 0.0
                }
            };
        }

        private void LoadCustomPresets()
        {
            // Load from local file for robust persistence across reloads
            string path = Path.Combine(Globals.UserDataDir, "bin", "Custom", "AlightenPresets.txt");

            if (!File.Exists(path)) return; // No custom presets yet

            try
            {
                string data = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(data)) return;

                var customPresetsList = data.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var item in customPresetsList)
                {
                    var parts = item.Split(':');
                    if (parts.Length < 2) continue;

                    string name = parts[0];
                    string[] values = parts[1].Split(',');

                    // Ensure we have enough values (handle backward compatibility)
                    if (values.Length < 9) continue;

                    presets[name] = new Dictionary<string, object>
                    {
                        ["MinimumVolumeForMarker"] = int.Parse(values[0]),
                        ["MaximumMarkerSize"] = int.Parse(values.Length >= 12 ? values[1] : "50"), // Handle new field
                        ["TrapLookaheadBars"] = int.Parse(values.Length >= 12 ? values[2] : values[1]),
                        ["TrapMoveTicks"] = int.Parse(values.Length >= 12 ? values[3] : values[2]),
                        ["TrapMinTotalVolume"] = int.Parse(values.Length >= 12 ? values[4] : values[3]),
                        ["TrapMinDominancePct"] = int.Parse(values.Length >= 12 ? values[5] : values[4]),
                        ["SITrapLookaheadBars"] = int.Parse(values.Length >= 12 ? values[6] : values[5]),
                        ["SITrapMoveTicks"] = int.Parse(values.Length >= 12 ? values[7] : values[6]),
                        ["SIAggregationIntervalTicks"] = int.Parse(values.Length >= 12 ? values[8] : values[7]),
                        ["SIImbFact"] = double.Parse(values.Length >= 12 ? values[9] : values[8]),
                        ["SIStackedLevels"] = int.Parse(values.Length >= 12 ? values[10] : values[9]),
                        ["SIMinRowVolume"] = double.Parse(values.Length >= 12 ? values[11] : values[10])
                    };
                }
            }
            catch (Exception ex)
            {
                Print($"Error loading custom presets from file: {ex.Message}");
            }
        }


        private void SaveCustomPresetsToProperty()
        {
            // Save ALL presets to external file
            var customPresets = presets.ToList();

            var sb = new StringBuilder();
            foreach (var preset in customPresets)
            {
                var vals = preset.Value;
                sb.Append($"{preset.Key}:");
                sb.Append($"{vals["MinimumVolumeForMarker"]},");
                sb.Append($"{vals["MaximumMarkerSize"]},");
                sb.Append($"{vals["TrapLookaheadBars"]},");
                sb.Append($"{vals["TrapMoveTicks"]},");
                sb.Append($"{vals["TrapMinTotalVolume"]},");
                sb.Append($"{vals["TrapMinDominancePct"]},");
                sb.Append($"{vals["SITrapLookaheadBars"]},");
                sb.Append($"{vals["SITrapMoveTicks"]},");
                sb.Append($"{vals["SIAggregationIntervalTicks"]},");
                sb.Append($"{vals["SIImbFact"]},");
                sb.Append($"{vals["SIStackedLevels"]},");
                sb.Append($"{vals["SIMinRowVolume"]}");
                sb.Append("|");
            }

            string data = sb.ToString().TrimEnd('|');

            // Save to file
            try
            {
                string dir = Path.Combine(Globals.UserDataDir, "bin", "Custom");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string path = Path.Combine(dir, "AlightenPresets.txt");
                File.WriteAllText(path, data);
            }
            catch (Exception ex)
            {
                Print($"Error saving presets to file: {ex.Message}");
            }

            OFCustomPresets = data; // Keep property updated just in case, but file is primary
        }

        private void ApplyPreset(string presetName)
        {
            if (!presets.ContainsKey(presetName)) return;

            var preset = presets[presetName];
            txtMinVol.Text = preset["MinimumVolumeForMarker"].ToString();
            txtMaxMarkerSize.Text = preset.ContainsKey("MaximumMarkerSize") ? preset["MaximumMarkerSize"].ToString() : "50";
            txtTrapLookBars.Text = preset["TrapLookaheadBars"].ToString();
            txtTrapMoveTicks.Text = preset["TrapMoveTicks"].ToString();
            txtTrapMinVol.Text = preset["TrapMinTotalVolume"].ToString();
            txtTrapMinDom.Text = preset["TrapMinDominancePct"].ToString();
            txtSITrapLookBars.Text = preset["SITrapLookaheadBars"].ToString();
            txtSITrapMoveTicks.Text = preset["SITrapMoveTicks"].ToString();
            txtSIAggInterval.Text = preset["SIAggregationIntervalTicks"].ToString();
            txtSIImbFact.Text = preset["SIImbFact"].ToString();
            txtSIStackedLevels.Text = preset["SIStackedLevels"].ToString();
            txtSIMinRowVol.Text = preset["SIMinRowVolume"].ToString();
        }

        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (presetComboBox.SelectedItem == null) return;
            string selectedPreset = presetComboBox.SelectedItem.ToString();
            ApplyPreset(selectedPreset);
            OFPresetName = selectedPreset;
        }

        private void BtnNewPreset_Click(object sender, RoutedEventArgs e)
        {
            // Use NTWindow for dark theme consistent with NinjaTrader
            var dialog = new NinjaTrader.Gui.Tools.NTWindow
            {
                Title = "New Preset",
                Width = 300,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };

            // We need to set the content manually since XAML isn't easily available here
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock { Text = "Preset Name:", Margin = new Thickness(10), Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            var textBox = new TextBox { Margin = new Thickness(10), Height = 25 };
            Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10) };
            var okButton = new Button { Content = "OK", Width = 75, Margin = new Thickness(5), Style = Application.Current.TryFindResource("Button") as Style };
            var cancelButton = new Button { Content = "Cancel", Width = 75, Margin = new Thickness(5), Style = Application.Current.TryFindResource("Button") as Style };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            Grid.SetRow(buttonPanel, 3);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;

            bool? result = null;
            okButton.Click += (s, args) => { result = true; dialog.Close(); };
            cancelButton.Click += (s, args) => { result = false; dialog.Close(); };

            dialog.ShowDialog();

            if (result == true && !string.IsNullOrWhiteSpace(textBox.Text))
            {
                string presetName = textBox.Text.Trim();
                if (presets.ContainsKey(presetName))
                {
                    System.Windows.MessageBox.Show($"Preset '{presetName}' already exists. Use 'Save' to overwrite.", "Error");
                    return;
                }

                SaveCurrentValuesToPreset(presetName);
            }
        }

        private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
        {
            if (presetComboBox.SelectedItem == null) return;
            string currentPreset = presetComboBox.SelectedItem.ToString();

            // Allow overwriting built-in presets if user desires (requested feature)
            // if (currentPreset == "GC (Default)" || currentPreset == "ES") ...

            // Overwrite existing custom preset without popup
            SaveCurrentValuesToPreset(currentPreset);
        }

        private void SaveCurrentValuesToPreset(string presetName)
        {
            try
            {
                presets[presetName] = new Dictionary<string, object>
                {
                    ["MinimumVolumeForMarker"] = int.Parse(txtMinVol.Text),
                    ["MaximumMarkerSize"] = int.Parse(txtMaxMarkerSize.Text),
                    ["TrapLookaheadBars"] = int.Parse(txtTrapLookBars.Text),
                    ["TrapMoveTicks"] = int.Parse(txtTrapMoveTicks.Text),
                    ["TrapMinTotalVolume"] = int.Parse(txtTrapMinVol.Text),
                    ["TrapMinDominancePct"] = int.Parse(txtTrapMinDom.Text),
                    ["SITrapLookaheadBars"] = int.Parse(txtSITrapLookBars.Text),
                    ["SITrapMoveTicks"] = int.Parse(txtSITrapMoveTicks.Text),
                    ["SIAggregationIntervalTicks"] = int.Parse(txtSIAggInterval.Text),
                    ["SIImbFact"] = double.Parse(txtSIImbFact.Text),
                    ["SIStackedLevels"] = int.Parse(txtSIStackedLevels.Text),
                    ["SIMinRowVolume"] = double.Parse(txtSIMinRowVol.Text)
                };

                if (!presetComboBox.Items.Contains(presetName))
                {
                    presetComboBox.Items.Add(presetName);
                    presetComboBox.SelectedItem = presetName;
                }

                OFPresetName = presetName;
                SaveCustomPresetsToProperty();
                Print($"Preset '{presetName}' saved.");
            }
            catch (Exception ex)
            {
                Print($"Error saving preset: {ex.Message}");
            }
        }

        private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (presetComboBox.SelectedItem == null) return;
            string selectedPreset = presetComboBox.SelectedItem.ToString();

            if (selectedPreset == "GC (Default)" || selectedPreset == "ES")
            {
                System.Windows.MessageBox.Show("Cannot delete built-in presets.", "Restricted");
                return;
            }

            if (presets.ContainsKey(selectedPreset))
            {
                presets.Remove(selectedPreset);
                presetComboBox.Items.Remove(selectedPreset);
                presetComboBox.SelectedItem = "GC (Default)";
                SaveCustomPresetsToProperty();
                Print($"Preset '{selectedPreset}' deleted");
            }
        }

        private void BtnUpdateOrderFlow_Click(object sender, RoutedEventArgs e)
        {
            if (orderFlowIndicator == null)
            {
                Print("OrderFlow indicator not found on chart");
                return;
            }

            try
            {
                // Validate and parse all textbox values
                int minVol = ValidateInt(txtMinVol.Text, "MinimumVolumeForMarker", 0, int.MaxValue);
                int maxMarker = ValidateInt(txtMaxMarkerSize.Text, "MaximumMarkerSize", 1, int.MaxValue);
                int trapLookBars = ValidateInt(txtTrapLookBars.Text, "TrapLookaheadBars", 1, 50);
                int trapMoveTicks = ValidateInt(txtTrapMoveTicks.Text, "TrapMoveTicks", 1, 200);
                int trapMinVol = ValidateInt(txtTrapMinVol.Text, "TrapMinTotalVolume", 1, int.MaxValue);
                int trapMinDom = ValidateInt(txtTrapMinDom.Text, "TrapMinDominancePct", 50, 100);
                int siTrapLookBars = ValidateInt(txtSITrapLookBars.Text, "SITrapLookaheadBars", 1, 200);
                int siTrapMoveTicks = ValidateInt(txtSITrapMoveTicks.Text, "SITrapMoveTicks", 1, 500);
                int siAggInterval = ValidateInt(txtSIAggInterval.Text, "SIAggregationIntervalTicks", 1, 200);
                double siImbFact = ValidateDouble(txtSIImbFact.Text, "SIImbFact", 1.01, 50);
                int siStackedLevels = ValidateInt(txtSIStackedLevels.Text, "SIStackedLevels", 1, 20);
                double siMinRowVol = ValidateDouble(txtSIMinRowVol.Text, "SIMinRowVolume", 0, 1000000);

                // Update indicator properties
                orderFlowIndicator.MinimumVolumeForMarker = minVol;
                orderFlowIndicator.MaximumMarkerSize = maxMarker;
                orderFlowIndicator.TrapLookaheadBars = trapLookBars;
                orderFlowIndicator.TrapMoveTicks = trapMoveTicks;
                orderFlowIndicator.TrapMinTotalVolume = trapMinVol;
                orderFlowIndicator.TrapMinDominancePct = trapMinDom;
                orderFlowIndicator.SITrapLookaheadBars = siTrapLookBars;
                orderFlowIndicator.SITrapMoveTicks = siTrapMoveTicks;
                orderFlowIndicator.SIAggregationIntervalTicks = siAggInterval;
                orderFlowIndicator.SIImbFact = siImbFact;
                orderFlowIndicator.SIStackedImbalanceLookback = siStackedLevels;
                orderFlowIndicator.SIMinRowVolume = siMinRowVol;

                // Update local properties
                OFMinimumVolumeForMarker = minVol;
                OFMaximumMarkerSize = maxMarker;
                OFTrapLookaheadBars = trapLookBars;
                OFTrapMoveTicks = trapMoveTicks;
                OFTrapMinTotalVolume = trapMinVol;
                OFTrapMinDominancePct = trapMinDom;
                OFSITrapLookaheadBars = siTrapLookBars;
                OFSITrapMoveTicks = siTrapMoveTicks;
                OFSIAggregationIntervalTicks = siAggInterval;
                OFSIImbFact = siImbFact;
                OFSIStackedLevels = siStackedLevels;
                OFSIMinRowVolume = siMinRowVol;

                // Force indicator to re-render with new parameters
                orderFlowIndicator.ForceRefresh();

                // Force full NinjaScript reload to ensure OnBarUpdate historical data is recalculated
                // (This addresses "it updates indicator but doesn't refresh chart data")
                // Force full NinjaScript reload to ensure OnBarUpdate historical data is recalculated
                ChartControl.Dispatcher.InvokeAsync(() =>
                {
                    // Send F5 key to trigger NinjaTrader's built-in reload mechanism
                    // this is the standard workaround when ReloadAll/CommandFactory are not available
                    try
                    {
                        // Ensure chart has focus so F5 works on it
                        if (ChartControl != null)
                            ChartControl.Focus();

                        System.Windows.Forms.SendKeys.SendWait("{F5}");
                    }
                    catch (Exception ex)
                    {
                        Print("Auto-refresh failed: " + ex.Message);
                    }
                });

                Print("OrderFlow parameters applied (Auto-Reload Triggered)");
            }
            catch (Exception ex)
            {
                Print($"Error updating OrderFlow: {ex.Message}");
            }
        }

        private int ValidateInt(string text, string fieldName, int min, int max)
        {
            if (!int.TryParse(text, out int result))
                throw new ArgumentException($"{fieldName} must be an integer");
            if (result < min || result > max)
                throw new ArgumentException($"{fieldName} must be between {min} and {max}");
            return result;
        }

        private double ValidateDouble(string text, string fieldName, double min, double max)
        {
            if (!double.TryParse(text, out double result))
                throw new ArgumentException($"{fieldName} must be a number");
            if (result < min || result > max)
                throw new ArgumentException($"{fieldName} must be between {min} and {max}");
            return result;
        }

        #endregion

        private void RemoveWPFControls()
        {
            if (chartTraderButtonsGrid == null || panelGrid == null || addedRow == null)
                return;

            // remove the grid and its row
            chartTraderButtonsGrid.Children.Remove(panelGrid);
            chartTraderButtonsGrid.RowDefinitions.Remove(addedRow);

            // clear references
            panelGrid = null;
            addedRow = null;
            chartTraderGrid = null;
            chartTraderButtonsGrid = null;
            orderFlowIndicator = null;
            presets?.Clear();
            presets = null;
        }

        private void BtnBEPlus1_Click(object sender, RoutedEventArgs e)
        {
            StopsToBreakeven(Breakeven1PlusTicks);
        }

        private void BtnBEPlus2_Click(object sender, RoutedEventArgs e)
        {
            StopsToBreakeven(Breakeven2PlusTicks);
        }

        private void BtnPricePlus_Click(object sender, RoutedEventArgs e)
        {
            TargetToPricePlus(PricePlusTicks);
        }

        private void BtnEntryPlus_Click(object sender, RoutedEventArgs e)
        {
            TargetToEntryPlus(EntryPlusTicks);
        }
        private void BtnBracket_Click(object sender, RoutedEventArgs e)
        {
            BracketOrder(BracketStopTicks, BracketProfitTicks);
        }

        private void BtnAddStop_Click(object sender, RoutedEventArgs e)
        {
            AddStopOrder(BracketStopTicks);
        }

        private void BtnHalf_Click(object sender, RoutedEventArgs e)
        {
            RemoveHalfPosition();
        }

        private void BtnDouble_Click(object sender, RoutedEventArgs e)
        {
            DoublePosition();
        }

        private void BtnNaked_Click(object sender, RoutedEventArgs e)
        {
            RemoveStopsAndTargets();
        }

        private void BtnSplit_Click(object sender, RoutedEventArgs e)
        {
            SplitStopsAndTargets();
        }

        private void BtnFlattenEverything_Click(object sender, RoutedEventArgs e)
        {
            FlattenEverythingAllAccounts();
        }

        // Trading methods from V0002 - see AlightenButtonPanelV0002.cs lines 437-1404 for full implementation
        // Key methods: FlattenEverythingAllAccounts, TargetToEntryPlus, TargetToPricePlus, 
        // StopsToBreakeven, BracketOrder, AddStopOrder, RemoveHalfPosition, DoublePosition,
        // RemoveStopsAndTargets, SplitStopsAndTargets

        #region Trading Methods (from V0002)

        private void FlattenEverythingAllAccounts()
        {
            if (isFlatteningAll)
            {
                Print("Flatten ▶ already in progress…");
                return;
            }

            isFlatteningAll = true;

            // Run off UI thread so we can wait briefly between passes without freezing the chart
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var allAccts = NinjaTrader.Cbi.Account.All?.ToList() ?? new List<NinjaTrader.Cbi.Account>();
                    if (allAccts.Count == 0)
                    {
                        Print("Flatten ▶ No accounts found.");
                        return;
                    }

                    int totalCancelled = 0;

                    // 1) Cancel all working/accepted/part-filled orders on every account
                    foreach (var acct in allAccts)
                    {
                        try
                        {
                            var cancels = acct.Orders
                                .Where(o => o != null &&
                                           (o.OrderState == OrderState.Working ||
                                            o.OrderState == OrderState.Accepted ||
                                            o.OrderState == OrderState.PartFilled))
                                .ToArray();

                            if (cancels.Length > 0)
                            {
                                acct.Cancel(cancels);
                                totalCancelled += cancels.Length;
                                Print($"Flatten ▶ {acct.Name}: cancelled {cancels.Length} order(s).");
                            }
                        }
                        catch (Exception ex)
                        {
                            Print($"Flatten ▶ Cancel error on {acct?.Name}: {ex.Message}");
                        }
                    }

                    // Give providers/copiers a moment to propagate cancellations
                    System.Threading.Thread.Sleep(FlattenAllPause);

                    int passes = 0;
                    int maxPasses = FlattenAllTries;   // ~6 * 250ms ≈ 1.5s worst case
                    int totalMarketSub = 0;

                    // 2) Retry loop: re-check positions and market-out until flat or max passes
                    while (passes < maxPasses)
                    {
                        passes++;
                        int thisPassSubmitted = 0;
                        int remainingPositions = 0;

                        foreach (var acct in allAccts)
                        {
                            try
                            {
                                // Snapshot CURRENT positions
                                var open = acct.Positions.Where(p => p != null && p.Quantity != 0).ToList();
                                remainingPositions += open.Count;
                                if (open.Count == 0)
                                {
                                    //Print($"Flatten ▶ {acct.Name}: no open positions (pass {passes}).");
                                    continue;
                                }

                                var outs = new List<NinjaTrader.Cbi.Order>(open.Count);
                                foreach (var pos in open)
                                {
                                    var instr = pos.Instrument;
                                    int qty = Math.Abs(pos.Quantity);
                                    if (qty <= 0 || instr == null) continue;

                                    OrderAction action = pos.MarketPosition == MarketPosition.Long
                                                       ? OrderAction.Sell
                                                       : OrderAction.BuyToCover;

                                    var mkt = acct.CreateOrder(
                                        instr,
                                        action,
                                        OrderType.Market,
                                        OrderEntry.Automated,
                                        TimeInForce.Gtc,
                                        qty,
                                        0, 0,
                                        "",
                                        Name + "_GlobalFlatten_" + instr.FullName,
                                        Core.Globals.MaxDate,
                                        null
                                    );
                                    outs.Add(mkt);
                                }

                                if (outs.Count > 0)
                                {
                                    acct.Submit(outs.ToArray());
                                    thisPassSubmitted += outs.Count;
                                    Print($"Flatten ▶ {acct.Name}: submitted {outs.Count} market order(s) (pass {passes}).");
                                }
                            }
                            catch (Exception ex)
                            {
                                Print($"Flatten ▶ Market-out error on {acct?.Name} (pass {passes}): {ex.Message}");
                            }
                        }

                        totalMarketSub += thisPassSubmitted;

                        // If nothing left open, we’re done
                        if (remainingPositions == 0)
                            break;

                        // Brief pause to allow fills/position updates to settle, then re-check
                        System.Threading.Thread.Sleep(250);
                    }

                    // Final audit log
                    foreach (var acct in allAccts)
                    {
                        var stillOpen = acct.Positions.Where(p => p != null && p.Quantity != 0).ToList();
                        if (stillOpen.Count > 0)
                        {
                            var list = string.Join(", ", stillOpen.Select(p => $"{p.Instrument?.FullName}:{p.Quantity}"));
                            Print($"Flatten ▶ WARNING: {acct.Name} still open after retries → {list}");
                        }
                        else
                        {
                            Print($"Flatten ▶ {acct.Name} flat.");
                        }
                    }

                    Print($"Flatten ▶ SUMMARY: cancelled {totalCancelled} order(s), submitted {totalMarketSub} market order(s) across {allAccts.Count} account(s) in {passes} pass(es).");
                }
                catch (Exception ex)
                {
                    Print("Flatten ▶ ERROR: " + ex.Message);
                }
                finally
                {
                    isFlatteningAll = false;

                    // (Optional) re-enable the button on UI thread if you disabled it visually
                    if (ChartControl != null)
                    {
                        ChartControl.Dispatcher.InvokeAsync(() =>
                        {
                            try { if (btnFlattenEverything != null) btnFlattenEverything.IsEnabled = true; } catch { }
                        });
                    }
                }
            });
        }


        private void TargetToEntryPlus(int entryPlusTicks)
        {
            // 1) resolve account
            xAcSelector = Window
                .GetWindow(ChartControl.Parent)
                .FindFirst("ChartTraderControlAccountSelector") as NinjaTrader.Gui.Tools.AccountSelector;
            if (xAcSelector == null) return;
            string acctName = xAcSelector.SelectedAccount?.ToString();
            if (string.IsNullOrEmpty(acctName)) return;
            var acct = NinjaTrader.Cbi.Account.All.FirstOrDefault(a => acctName.Contains(a.Name));
            if (acct == null) return;

            // 2) resolve instrument
            xInSelector = Window
                .GetWindow(ChartControl.OwnerChart)
                .FindFirst("ChartWindowInstrumentSelector") as NinjaTrader.Gui.Tools.InstrumentSelector;
            if (xInSelector == null) return;
            var instr = xInSelector.Instrument;
            if (instr == null) return;

            // 3) find all working limit orders (targets)
            var targets = acct.Orders
                .Where(o =>
                    o.Instrument.FullName == instr.FullName &&
                    o.OrderType == OrderType.Limit &&
                    (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted))
                .ToList();
            if (targets.Count == 0)
            {
                Print("EntryPlus ▶ no target orders to move");
                return;
            }

            // 4) get current position and its average entry price
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity == 0)
            {
                Print("EntryPlus ▶ no open position");
                return;
            }
            bool isLong = pos.MarketPosition == MarketPosition.Long;
            double avgEntry = pos.AveragePrice;
            double tickSize = instr.MasterInstrument.TickSize;

            // 5) compute the new price: avg entry ± entryPlusTicks * tickSize
            double newPrice = isLong
                ? avgEntry + entryPlusTicks * tickSize
                : avgEntry - entryPlusTicks * tickSize;

            // 6) apply to all targets and submit change
            foreach (var o in targets)
                o.LimitPriceChanged = newPrice;

            acct.Change(targets.ToArray());
            Print($"EntryPlus ▶ moved {targets.Count} target(s) to {newPrice}");
        }


        private void TargetToPricePlus(int pricePlusTicks)
        {
            // 1) resolve account
            xAcSelector = Window
                .GetWindow(ChartControl.Parent)
                .FindFirst("ChartTraderControlAccountSelector") as NinjaTrader.Gui.Tools.AccountSelector;
            if (xAcSelector == null) return;
            string acctName = xAcSelector.SelectedAccount?.ToString();
            if (string.IsNullOrEmpty(acctName)) return;
            var acct = NinjaTrader.Cbi.Account.All.FirstOrDefault(a => acctName.Contains(a.Name));
            if (acct == null) return;

            // 2) resolve instrument
            xInSelector = Window
                .GetWindow(ChartControl.OwnerChart)
                .FindFirst("ChartWindowInstrumentSelector") as NinjaTrader.Gui.Tools.InstrumentSelector;
            if (xInSelector == null) return;
            var instr = xInSelector.Instrument;
            if (instr == null) return;

            // 3) find all working limit orders (your current targets)
            var targets = acct.Orders
                .Where(o =>
                    o.Instrument.FullName == instr.FullName &&
                    o.OrderType == OrderType.Limit &&
                    (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted))
                .ToList();

            if (targets.Count == 0)
            {
                Print("PricePlus ▶ no target orders to move");
                return;
            }

            // 4) determine direction & compute new price
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity == 0)
            {
                Print("PricePlus ▶ no open position");
                return;
            }
            bool isLong = pos.MarketPosition == MarketPosition.Long;
            double tickSize = instr.MasterInstrument.TickSize;
            double basePrice = instr.MasterInstrument.RoundToTickSize(lastClose);
            double newPrice = isLong
                ? basePrice + pricePlusTicks * tickSize
                : basePrice - pricePlusTicks * tickSize;

            // 5) apply to all targets and submit change
            foreach (var o in targets)
                o.LimitPriceChanged = newPrice;

            acct.Change(targets.ToArray());
            Print($"PricePlus ▶ moved {targets.Count} target(s) to {newPrice}");
        }


        private void SplitStopsAndTargets()
        {
            // 1) resolve account & instrument (unchanged)
            xAcSelector = Window
              .GetWindow(ChartControl.Parent)
              .FindFirst("ChartTraderControlAccountSelector")
                as NinjaTrader.Gui.Tools.AccountSelector;
            if (xAcSelector == null) return;
            var acct = NinjaTrader.Cbi.Account.All
                .FirstOrDefault(a => xAcSelector.SelectedAccount.ToString().Contains(a.Name));
            if (acct == null) return;
            xInSelector = Window
              .GetWindow(ChartControl.OwnerChart)
              .FindFirst("ChartWindowInstrumentSelector")
                as NinjaTrader.Gui.Tools.InstrumentSelector;
            if (xInSelector == null) return;
            var instr = xInSelector.Instrument;
            if (instr == null) return;

            // 2) pull all stop & limit legs that belong to any OCO
            var bracketLegs = acct.Orders
                .Where(o =>
                    o.Instrument.FullName == instr.FullName &&
                    !string.IsNullOrEmpty(o.Oco) &&
                   (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted) &&
                   (o.OrderType == OrderType.Limit ||
                    o.OrderType == OrderType.StopMarket ||
                    o.OrderType == OrderType.StopLimit))
                .ToList();
            if (bracketLegs.Count == 0)
            {
                Print("Split ▶ no bracket exits to split");
                return;
            }

            // 3) group by OCO to inspect quantities
            var groups = bracketLegs.GroupBy(o => o.Oco).ToList();

            // 4) handle "multi-ATM" scenario: several 1-contract OCOs
            bool allSingle = groups.All(g => g.All(o => o.Quantity == 1));
            if (allSingle && groups.Count > 1)
            {
                int totalQty = groups.Count;
                // base prices from first group
                var first = groups[0].ToList();
                var stopLeg = first.First(o => o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit);
                var tgtLeg = first.First(o => o.OrderType == OrderType.Limit);
                double baseStop = stopLeg.StopPrice;
                double baseTarget = tgtLeg.LimitPrice;
                bool isLong = stopLeg.OrderAction == OrderAction.Sell;
                double tickSz = instr.MasterInstrument.TickSize;

                // cancel every ATM exit
                acct.Cancel(bracketLegs.ToArray());
                Print($"Split ▶ cancelled {bracketLegs.Count} legs from {groups.Count} ATM brackets");

                // rebuild per-contract stop+target stepping out by ticks
                var newOrders = new List<NinjaTrader.Cbi.Order>(totalQty * 2);
                for (int i = 0; i < totalQty; i++)
                {
                    string legOco = Guid.NewGuid().ToString();

                    // stop market for 1 contract
                    newOrders.Add(acct.CreateOrder(
                        instr,
                        isLong ? OrderAction.Sell : OrderAction.BuyToCover,
                        OrderType.StopMarket,
                        OrderEntry.Automated,
                        TimeInForce.Gtc,
                        1,
                        0,
                        baseStop,
                        legOco,
                        Name + "_Stop" + (i + 1),
                        Core.Globals.MaxDate,
                        null
                    ));

                    // limit target one tick further each time
                    double stepped = isLong
                        ? baseTarget + i * tickSz
                        : baseTarget - i * tickSz;

                    newOrders.Add(acct.CreateOrder(
                        instr,
                        isLong ? OrderAction.Sell : OrderAction.BuyToCover,
                        OrderType.Limit,
                        OrderEntry.Automated,
                        TimeInForce.Gtc,
                        1,
                        stepped,
                        0,
                        legOco,
                        Name + "_Split" + (i + 1),
                        Core.Globals.MaxDate,
                        null
                    ));
                }

                acct.Submit(newOrders.ToArray());
                Print($"Split ▶ created {totalQty} stop+target pairs across combined ATMs");
                return;
            }

            // 5) fallback to your existing “aggregated” logic
            foreach (var group in groups)
            {
                var legs = group.ToList();
                string oco = group.Key;

                bool isAggregated = legs.Any(o => o.Quantity > 1);
                if (!isAggregated)
                {
                    Print($"Split ▶ OCO={oco} is already per-contract, skipping");
                    continue;
                }

                // your original cancel & rebuild logic for multi-contract brackets...
                // (unchanged)
                var stopLeg = legs.First(o => o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit);
                var limitLeg = legs.First(o => o.OrderType == OrderType.Limit);
                int qty = limitLeg.Quantity;
                double baseStopPrice = stopLeg.StopPrice;
                double baseTargetPrice = limitLeg.LimitPrice;
                bool isLong2 = stopLeg.OrderAction == OrderAction.Sell;
                double tickSz2 = instr.MasterInstrument.TickSize;

                acct.Cancel(legs.ToArray());
                Print($"Split ▶ cancelled {legs.Count} aggregated legs for OCO={oco}");

                var newOrders = new List<NinjaTrader.Cbi.Order>(qty * 2);
                for (int i = 0; i < qty; i++)
                {
                    string legOco2 = Guid.NewGuid().ToString();

                    newOrders.Add(acct.CreateOrder(
                        instr,
                        isLong2 ? OrderAction.Sell : OrderAction.BuyToCover,
                        OrderType.StopMarket,
                        OrderEntry.Automated,
                        TimeInForce.Gtc,
                        1,
                        0,
                        baseStopPrice,
                        legOco2,
                        Name + "_Stop" + (i + 1),
                        Core.Globals.MaxDate,
                        null
                    ));

                    double tgtPrice = isLong2
                        ? baseTargetPrice + i * tickSz2
                        : baseTargetPrice - i * tickSz2;

                    newOrders.Add(acct.CreateOrder(
                        instr,
                        isLong2 ? OrderAction.Sell : OrderAction.BuyToCover,
                        OrderType.Limit,
                        OrderEntry.Automated,
                        TimeInForce.Gtc,
                        1,
                        tgtPrice,
                        0,
                        legOco2,
                        Name + "_Split" + (i + 1),
                        Core.Globals.MaxDate,
                        null
                    ));
                }

                acct.Submit(newOrders.ToArray());
                Print($"Split ▶ created {qty} stop+target pairs for OCO={oco}");
            }
        }

        private void RemoveStopsAndTargets()
        {
            // 1) resolve account
            xAcSelector = Window
              .GetWindow(ChartControl.Parent)
              .FindFirst("ChartTraderControlAccountSelector") as NinjaTrader.Gui.Tools.AccountSelector;
            if (xAcSelector == null) return;
            string acctName = xAcSelector.SelectedAccount?.ToString();
            if (string.IsNullOrEmpty(acctName)) return;
            var acct = NinjaTrader.Cbi.Account.All.FirstOrDefault(a => acctName.Contains(a.Name));
            if (acct == null) return;

            // 2) resolve instrument
            xInSelector = Window
              .GetWindow(ChartControl.OwnerChart)
              .FindFirst("ChartWindowInstrumentSelector") as NinjaTrader.Gui.Tools.InstrumentSelector;
            if (xInSelector == null) return;
            var instr = xInSelector.Instrument;
            if (instr == null) return;

            // 3) find all working or accepted stops & limits
            var exitOrders = acct.Orders
                .Where(o =>
                    o.Instrument.FullName == instr.FullName &&
                   (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.Limit) &&
                   (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted))
                .ToArray();

            if (exitOrders.Length == 0)
            {
                Print("Naked ▶ no exit orders to remove");
                return;
            }

            // 4) cancel them
            acct.Cancel(exitOrders);
            Print($"Naked ▶ removed {exitOrders.Length} stops/targets");
        }


        private void DoublePosition()
        {
            // 1) resolve account & instrument
            xAcSelector = Window
              .GetWindow(ChartControl.Parent)
              .FindFirst("ChartTraderControlAccountSelector") as NinjaTrader.Gui.Tools.AccountSelector;
            if (xAcSelector == null) return;
            string acctName = xAcSelector.SelectedAccount?.ToString() ?? "";
            var acct = NinjaTrader.Cbi.Account.All
                .FirstOrDefault(a => acctName.Contains(a.Name));
            if (acct == null) return;

            xInSelector = Window
              .GetWindow(ChartControl.OwnerChart)
              .FindFirst("ChartWindowInstrumentSelector") as NinjaTrader.Gui.Tools.InstrumentSelector;
            if (xInSelector == null) return;
            var instr = xInSelector.Instrument;

            // 2) pull your current filled position
            var pos = acct.Positions
                .FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity <= 0)
            {
                Print("Double ▶ no open position");
                return;
            }

            bool isLong = pos.MarketPosition == MarketPosition.Long;
            int currentQty = pos.Quantity;

            // 3) send one market order to add currentQty (doubling total)
            var dblOrder = acct.CreateOrder(
                instr,
                isLong ? OrderAction.Buy : OrderAction.SellShort,
                OrderType.Market,
                OrderEntry.Automated,
                TimeInForce.Gtc,
                currentQty,
                0, 0,
                "",                      // no OCO on the market leg
                Name + "_Double",
                Core.Globals.MaxDate,
                null
            );
            acct.Submit(new[] { dblOrder });
            Print($"Double ▶ submitted market for {currentQty}");

            // 4) collect **all** exit orders in any OCO group
            var bracketOrders = acct.Orders
                .Where(o =>
                    o.Instrument.FullName == instr.FullName &&
                    !string.IsNullOrEmpty(o.Oco) &&
                   (o.OrderState == OrderState.Accepted || o.OrderState == OrderState.Working) &&
                   (o.OrderType == OrderType.Limit ||
                    o.OrderType == OrderType.StopMarket ||
                    o.OrderType == OrderType.StopLimit))
                .ToList();

            if (bracketOrders.Count == 0)
            {
                Print("Double ▶ no bracket exits to resize");
                return;
            }

            // 5) group by OCO and double each group
            foreach (var group in bracketOrders.GroupBy(o => o.Oco))
            {
                // what each leg was sized at before doubling
                int preQty = group.Min(o => o.Quantity);
                int newQty = preQty * 2;

                foreach (var o in group)
                    o.QuantityChanged = newQty;

                acct.Change(group.ToArray());
                Print($"Double ▶ resized OCO={group.Key} from {preQty} to {newQty}");
            }
        }


        private void RemoveHalfPosition()
        {
            // 1) resolve account & instrument
            xAcSelector = Window
              .GetWindow(ChartControl.Parent)
              .FindFirst("ChartTraderControlAccountSelector") as NinjaTrader.Gui.Tools.AccountSelector;
            if (xAcSelector == null) return;
            string acctName = xAcSelector.SelectedAccount?.ToString() ?? "";
            var acct = NinjaTrader.Cbi.Account.All
                .FirstOrDefault(a => acctName.Contains(a.Name));
            if (acct == null) return;
            xInSelector = Window
              .GetWindow(ChartControl.OwnerChart)
              .FindFirst("ChartWindowInstrumentSelector") as NinjaTrader.Gui.Tools.InstrumentSelector;
            if (xInSelector == null) return;
            var instr = xInSelector.Instrument;

            // 2) collect all exit orders (stops & limits), regardless of OCO
            var exitOrders = acct.Orders
                .Where(o =>
                    o.Instrument.FullName == instr.FullName &&
                    (o.OrderType == OrderType.Limit ||
                     o.OrderType == OrderType.StopMarket ||
                     o.OrderType == OrderType.StopLimit) &&
                    (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted))
                .ToList();

            // 3) get current position and compute removal amount
            var pos = acct.Positions.FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity <= 1)
            {
                Print($"Half ▶ only {pos?.Quantity ?? 0} contract(s), nothing to remove");
                return;
            }
            int totalContracts = pos.Quantity;
            int toRemoveContracts = totalContracts / 2;

            // 4) branch: do we have any bracketed (OCO’d) exit legs?
            var bracketOrders = exitOrders.Where(o => !string.IsNullOrEmpty(o.Oco)).ToList();
            if (bracketOrders.Any())
            {
                // — your original bracket logic —
                var groups = bracketOrders
                    .GroupBy(o => o.Oco)
                    .Select(g =>
                    {
                        bool isAgg = g.Any(o => o.Quantity > 1);
                        int contracts = isAgg
                            ? g.Min(o => o.Quantity)    // multi‐contract bracket
                            : g.Count() / 2;            // ATM‐style (2 legs per 1 contract)
                        return new { Oco = g.Key, Legs = g.ToList(), IsAggregated = isAgg, Contracts = contracts };
                    })
                    .ToList();

                int bracketTotal = groups.Sum(g => g.Contracts);
                int bracketToRemove = bracketTotal / 2;
                if (bracketToRemove == 0)
                {
                    Print($"Half ▶ only {bracketTotal} contract(s) in brackets, nothing to remove");
                    return;
                }

                // a) send market order to remove half
                var mktAction = pos.MarketPosition == MarketPosition.Long
                              ? OrderAction.Sell
                              : OrderAction.BuyToCover;
                var mktOrder = acct.CreateOrder(
                    instr,
                    mktAction,
                    OrderType.Market,
                    OrderEntry.Automated,
                    TimeInForce.Gtc,
                    bracketToRemove,
                    0, 0,
                    "",
                    Name + "_Half",
                    Core.Globals.MaxDate,
                    null
                );
                acct.Submit(new[] { mktOrder });
                Print($"Half ▶ market remove {bracketToRemove} of {bracketTotal}");

                // b) adjust each OCO group
                int remaining = bracketToRemove;
                foreach (var grp in groups)
                {
                    if (remaining == 0) break;
                    int removeHere = Math.Min(grp.Contracts, remaining);
                    int keepHere = grp.Contracts - removeHere;

                    if (grp.IsAggregated)
                    {
                        if (keepHere > 0)
                        {
                            foreach (var o in grp.Legs)
                                o.QuantityChanged = keepHere;
                            acct.Change(grp.Legs.ToArray());
                            Print($"Half ▶ OCO={grp.Oco} resized from {grp.Contracts} to {keepHere}");
                        }
                        else
                        {
                            acct.Cancel(grp.Legs.ToArray());
                            Print($"Half ▶ OCO={grp.Oco} fully removed");
                        }
                    }
                    else
                    {
                        var stops = grp.Legs.Where(o => o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit);
                        var targets = grp.Legs.Where(o => o.OrderType == OrderType.Limit);
                        var toCancel = new List<NinjaTrader.Cbi.Order>();

                        if (pos.MarketPosition == MarketPosition.Long)
                        {
                            toCancel.AddRange(stops.OrderByDescending(o => o.StopPrice).Take(removeHere));
                            toCancel.AddRange(targets.OrderBy(o => o.LimitPrice).Take(removeHere));
                        }
                        else
                        {
                            toCancel.AddRange(stops.OrderBy(o => o.StopPrice).Take(removeHere));
                            toCancel.AddRange(targets.OrderByDescending(o => o.LimitPrice).Take(removeHere));
                        }

                        if (toCancel.Any())
                        {
                            acct.Cancel(toCancel.ToArray());
                            Print($"Half ▶ OCO={grp.Oco} cancelled {toCancel.Count} ATM legs");
                        }
                    }

                    remaining -= removeHere;
                }
                return;
            }

            // 5) no OCO’s: naked or stop‐only position
            // a) remove half via market
            var mktAction2 = pos.MarketPosition == MarketPosition.Long
                           ? OrderAction.Sell
                           : OrderAction.BuyToCover;
            var mkt2 = acct.CreateOrder(
                instr,
                mktAction2,
                OrderType.Market,
                OrderEntry.Automated,
                TimeInForce.Gtc,
                toRemoveContracts,
                0, 0,
                "",
                Name + "_Half",
                Core.Globals.MaxDate,
                null
            );
            acct.Submit(new[] { mkt2 });
            Print($"Half ▶ naked remove {toRemoveContracts} of {totalContracts}");

            // b) shrink any standalone stops/limits
            var standalone = exitOrders.Where(o => string.IsNullOrEmpty(o.Oco)).ToList();
            if (standalone.Any())
            {
                int newQty = totalContracts - toRemoveContracts;
                foreach (var o in standalone)
                    o.QuantityChanged = newQty;
                acct.Change(standalone.ToArray());
                Print($"Half ▶ adjusted {standalone.Count} standalone stop/targets to {newQty}");
            }
        }


        private void AddStopOrder(int stopTicks)
        {
            // 1) find the ChartTrader’s account-selector
            xAcSelector = Window
              .GetWindow(ChartControl.Parent)
              .FindFirst("ChartTraderControlAccountSelector")
                as NinjaTrader.Gui.Tools.AccountSelector;
            if (xAcSelector == null) return;
            string acctName = xAcSelector.SelectedAccount?.ToString();
            if (string.IsNullOrEmpty(acctName)) return;

            // 2) look up the real Cbi.Account
            var acct = NinjaTrader.Cbi.Account.All
                       .FirstOrDefault(a => acctName.Contains(a.Name));
            if (acct == null) return;

            // 3) find the instrument selector on the Chart window
            xInSelector = Window
              .GetWindow(ChartControl.OwnerChart)
              .FindFirst("ChartWindowInstrumentSelector")
                as NinjaTrader.Gui.Tools.InstrumentSelector;
            if (xInSelector == null) return;
            var instr = xInSelector.Instrument;
            if (instr == null) return;

            // 4) pull your filled market position
            var pos = acct.Positions
                          .FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity == 0)
            {
                Print("No open position to add a stop");
                return;
            }

            // — NEW: skip if you already have a StopMarket working on this instrument
            bool hasStop = acct.Orders.Any(o =>
                o.Instrument.FullName == instr.FullName &&
                o.OrderType == OrderType.StopMarket &&
               (o.OrderState == OrderState.Accepted || o.OrderState == OrderState.Working));
            if (hasStop)
            {
                Print("A stop already exists—skipping Add Stop");
                return;
            }

            // 5) compute stop price off lastClose…
            double basePrice = instr.MasterInstrument.RoundToTickSize(lastClose);
            double tickSz = instr.MasterInstrument.TickSize;
            bool isLong = pos.MarketPosition == MarketPosition.Long;
            int qty = pos.Quantity;
            double stopPx = isLong
                                ? basePrice - stopTicks * tickSz
                                : basePrice + stopTicks * tickSz;

            // 6) create & submit the stop
            var stopOrder = acct.CreateOrder(
                instr,
                isLong ? OrderAction.Sell : OrderAction.BuyToCover,
                OrderType.StopMarket,
                OrderEntry.Automated,
                TimeInForce.Gtc,
                qty,
                0,
                stopPx,
                "",                    // no OCO
                Name + "_AddStop",
                Core.Globals.MaxDate,
                null
            );
            acct.Submit(new[] { stopOrder });
            Print($"Add Stop placed ▶ Stop={stopPx}");
        }

        private void BracketOrder(int stopTicks, int profitTicks)
        {
            // 1) grab the selected account name
            xAcSelector = Window
              .GetWindow(ChartControl.Parent)
              .FindFirst("ChartTraderControlAccountSelector")
                as NinjaTrader.Gui.Tools.AccountSelector;
            if (xAcSelector == null) return;
            string acctName = xAcSelector.SelectedAccount?.ToString();
            if (string.IsNullOrEmpty(acctName)) return;

            // 2) look up the real Cbi.Account
            var acct = NinjaTrader.Cbi.Account.All
                       .FirstOrDefault(a => acctName.Contains(a.Name));
            if (acct == null) return;

            // 3) grab the chart’s instrument
            xInSelector = Window
              .GetWindow(ChartControl.OwnerChart)
              .FindFirst("ChartWindowInstrumentSelector")
                as NinjaTrader.Gui.Tools.InstrumentSelector;
            if (xInSelector == null) return;
            var instr = xInSelector.Instrument;
            if (instr == null) return;

            // 4) guard: don’t bracket if you already have any OCO’d exit legs
            bool hasBracket = acct.Orders.Any(o =>
                o.Instrument.FullName == instr.FullName &&
                !string.IsNullOrEmpty(o.Oco) &&
               (o.OrderState == OrderState.Accepted || o.OrderState == OrderState.Working));
            if (hasBracket)
            {
                Print("Bracket already exists—skipping");
                return;
            }

            // 5) pull your filled market position
            var pos = acct.Positions
                          .FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity == 0)
            {
                Print("No open position to bracket");
                return;
            }

            // 6) compute basePrice off pos.AveragePrice
            double basePrice = instr.MasterInstrument.RoundToTickSize(lastClose);
            double tickSz = instr.MasterInstrument.TickSize;
            bool isLong = pos.MarketPosition == MarketPosition.Long;
            int qty = pos.Quantity;

            double stopPx = isLong
                             ? basePrice - stopTicks * tickSz
                             : basePrice + stopTicks * tickSz;
            double targetPx = isLong
                             ? basePrice + profitTicks * tickSz
                             : basePrice - profitTicks * tickSz;

            // 7) assign OCO and submit two legs
            string ocoId = Guid.NewGuid().ToString();

            var stopOrder = acct.CreateOrder(
                instr,
                isLong ? OrderAction.Sell : OrderAction.BuyToCover,
                OrderType.StopMarket,
                OrderEntry.Automated,
                TimeInForce.Gtc,
                qty,
                0,
                stopPx,
                ocoId,
                Name + "_SL",
                Core.Globals.MaxDate,
                null
            );

            var targetOrder = acct.CreateOrder(
                instr,
                isLong ? OrderAction.Sell : OrderAction.BuyToCover,
                OrderType.Limit,
                OrderEntry.Automated,
                TimeInForce.Gtc,
                qty,
                targetPx,
                0,
                ocoId,
                Name + "_TP",
                Core.Globals.MaxDate,
                null
            );

            acct.Submit(new[] { stopOrder, targetOrder });
            Print($"Bracket placed ▶ Entry={basePrice}, SL={stopPx}, TP={targetPx}, OCO={ocoId}");
        }


        private void StopsToBreakeven(int ticks)
        {
            // 1) find the ChartTrader’s account-selector and grab the selected name
            xAcSelector = Window
              .GetWindow(ChartControl.Parent)
              .FindFirst("ChartTraderControlAccountSelector")
                as NinjaTrader.Gui.Tools.AccountSelector;
            if (xAcSelector == null) return;
            string acctName = xAcSelector.SelectedAccount?.ToString();
            if (string.IsNullOrEmpty(acctName)) return;

            // 2) look up the real Cbi.Account object
            var acct = NinjaTrader.Cbi.Account.All
                       .FirstOrDefault(a => acctName.Contains(a.Name));
            if (acct == null) return;

            // 3) find the instrument selector on the Chart window
            xInSelector = Window
              .GetWindow(ChartControl.OwnerChart)
              .FindFirst("ChartWindowInstrumentSelector")
                as NinjaTrader.Gui.Tools.InstrumentSelector;
            if (xInSelector == null) return;
            var instr = xInSelector.Instrument;
            if (instr == null) return;

            // 4) get your live position for this instrument
            var pos = acct.Positions
                      .FirstOrDefault(p => p.Instrument.FullName == instr.FullName);
            if (pos == null || pos.Quantity == 0) return;

            // 5) loop every working stop (market or limit) for this instrument
            foreach (var order in acct.Orders
              .Where(o =>
                 o.Instrument.FullName == instr.FullName &&
                (o.OrderType == OrderType.StopMarket ||
                 o.OrderType == OrderType.StopLimit) &&
                 o.OrderState != OrderState.Cancelled &&
                 o.OrderState != OrderState.Filled))
            {
                // compute new stop price
                double newStop = pos.AveragePrice
                               + (pos.MarketPosition == MarketPosition.Long
                                  ? +ticks
                                  : -ticks)
                                 * instr.MasterInstrument.TickSize;

                order.StopPriceChanged = newStop;
                acct.Change(new[] { order });
            }
        }

        #endregion

        protected override void OnBarUpdate()
        {
            lastClose = Close[0];
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AlightenButtonPanelV0004[] cacheAlightenButtonPanelV0004;
		public AlightenButtonPanelV0004 AlightenButtonPanelV0004(int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, string oFPresetName, string oFCustomPresets, int oFMinimumVolumeForMarker, int oFMaximumMarkerSize, int oFTrapLookaheadBars, int oFTrapMoveTicks, int oFTrapMinTotalVolume, int oFTrapMinDominancePct, int oFSITrapLookaheadBars, int oFSITrapMoveTicks, int oFSIAggregationIntervalTicks, double oFSIImbFact, int oFSIStackedLevels, double oFSIMinRowVolume, Brush button1Color, Brush button2Color, Brush button3Color, Brush button4Color, Brush button5Color, Brush button6Color, Brush button7Color, Brush button8Color, Brush button9Color, Brush button10Color, Brush flattenEverythingColor)
		{
			return AlightenButtonPanelV0004(Input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, oFPresetName, oFCustomPresets, oFMinimumVolumeForMarker, oFMaximumMarkerSize, oFTrapLookaheadBars, oFTrapMoveTicks, oFTrapMinTotalVolume, oFTrapMinDominancePct, oFSITrapLookaheadBars, oFSITrapMoveTicks, oFSIAggregationIntervalTicks, oFSIImbFact, oFSIStackedLevels, oFSIMinRowVolume, button1Color, button2Color, button3Color, button4Color, button5Color, button6Color, button7Color, button8Color, button9Color, button10Color, flattenEverythingColor);
		}

		public AlightenButtonPanelV0004 AlightenButtonPanelV0004(ISeries<double> input, int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, string oFPresetName, string oFCustomPresets, int oFMinimumVolumeForMarker, int oFMaximumMarkerSize, int oFTrapLookaheadBars, int oFTrapMoveTicks, int oFTrapMinTotalVolume, int oFTrapMinDominancePct, int oFSITrapLookaheadBars, int oFSITrapMoveTicks, int oFSIAggregationIntervalTicks, double oFSIImbFact, int oFSIStackedLevels, double oFSIMinRowVolume, Brush button1Color, Brush button2Color, Brush button3Color, Brush button4Color, Brush button5Color, Brush button6Color, Brush button7Color, Brush button8Color, Brush button9Color, Brush button10Color, Brush flattenEverythingColor)
		{
			if (cacheAlightenButtonPanelV0004 != null)
				for (int idx = 0; idx < cacheAlightenButtonPanelV0004.Length; idx++)
					if (cacheAlightenButtonPanelV0004[idx] != null && cacheAlightenButtonPanelV0004[idx].Breakeven1PlusTicks == breakeven1PlusTicks && cacheAlightenButtonPanelV0004[idx].Breakeven2PlusTicks == breakeven2PlusTicks && cacheAlightenButtonPanelV0004[idx].PricePlusTicks == pricePlusTicks && cacheAlightenButtonPanelV0004[idx].EntryPlusTicks == entryPlusTicks && cacheAlightenButtonPanelV0004[idx].BracketStopTicks == bracketStopTicks && cacheAlightenButtonPanelV0004[idx].BracketProfitTicks == bracketProfitTicks && cacheAlightenButtonPanelV0004[idx].FlattenAllPause == flattenAllPause && cacheAlightenButtonPanelV0004[idx].FlattenAllTries == flattenAllTries && cacheAlightenButtonPanelV0004[idx].OFPresetName == oFPresetName && cacheAlightenButtonPanelV0004[idx].OFCustomPresets == oFCustomPresets && cacheAlightenButtonPanelV0004[idx].OFMinimumVolumeForMarker == oFMinimumVolumeForMarker && cacheAlightenButtonPanelV0004[idx].OFMaximumMarkerSize == oFMaximumMarkerSize && cacheAlightenButtonPanelV0004[idx].OFTrapLookaheadBars == oFTrapLookaheadBars && cacheAlightenButtonPanelV0004[idx].OFTrapMoveTicks == oFTrapMoveTicks && cacheAlightenButtonPanelV0004[idx].OFTrapMinTotalVolume == oFTrapMinTotalVolume && cacheAlightenButtonPanelV0004[idx].OFTrapMinDominancePct == oFTrapMinDominancePct && cacheAlightenButtonPanelV0004[idx].OFSITrapLookaheadBars == oFSITrapLookaheadBars && cacheAlightenButtonPanelV0004[idx].OFSITrapMoveTicks == oFSITrapMoveTicks && cacheAlightenButtonPanelV0004[idx].OFSIAggregationIntervalTicks == oFSIAggregationIntervalTicks && cacheAlightenButtonPanelV0004[idx].OFSIImbFact == oFSIImbFact && cacheAlightenButtonPanelV0004[idx].OFSIStackedLevels == oFSIStackedLevels && cacheAlightenButtonPanelV0004[idx].OFSIMinRowVolume == oFSIMinRowVolume && cacheAlightenButtonPanelV0004[idx].Button1Color == button1Color && cacheAlightenButtonPanelV0004[idx].Button2Color == button2Color && cacheAlightenButtonPanelV0004[idx].Button3Color == button3Color && cacheAlightenButtonPanelV0004[idx].Button4Color == button4Color && cacheAlightenButtonPanelV0004[idx].Button5Color == button5Color && cacheAlightenButtonPanelV0004[idx].Button6Color == button6Color && cacheAlightenButtonPanelV0004[idx].Button7Color == button7Color && cacheAlightenButtonPanelV0004[idx].Button8Color == button8Color && cacheAlightenButtonPanelV0004[idx].Button9Color == button9Color && cacheAlightenButtonPanelV0004[idx].Button10Color == button10Color && cacheAlightenButtonPanelV0004[idx].FlattenEverythingColor == flattenEverythingColor && cacheAlightenButtonPanelV0004[idx].EqualsInput(input))
						return cacheAlightenButtonPanelV0004[idx];
			return CacheIndicator<AlightenButtonPanelV0004>(new AlightenButtonPanelV0004(){ Breakeven1PlusTicks = breakeven1PlusTicks, Breakeven2PlusTicks = breakeven2PlusTicks, PricePlusTicks = pricePlusTicks, EntryPlusTicks = entryPlusTicks, BracketStopTicks = bracketStopTicks, BracketProfitTicks = bracketProfitTicks, FlattenAllPause = flattenAllPause, FlattenAllTries = flattenAllTries, OFPresetName = oFPresetName, OFCustomPresets = oFCustomPresets, OFMinimumVolumeForMarker = oFMinimumVolumeForMarker, OFMaximumMarkerSize = oFMaximumMarkerSize, OFTrapLookaheadBars = oFTrapLookaheadBars, OFTrapMoveTicks = oFTrapMoveTicks, OFTrapMinTotalVolume = oFTrapMinTotalVolume, OFTrapMinDominancePct = oFTrapMinDominancePct, OFSITrapLookaheadBars = oFSITrapLookaheadBars, OFSITrapMoveTicks = oFSITrapMoveTicks, OFSIAggregationIntervalTicks = oFSIAggregationIntervalTicks, OFSIImbFact = oFSIImbFact, OFSIStackedLevels = oFSIStackedLevels, OFSIMinRowVolume = oFSIMinRowVolume, Button1Color = button1Color, Button2Color = button2Color, Button3Color = button3Color, Button4Color = button4Color, Button5Color = button5Color, Button6Color = button6Color, Button7Color = button7Color, Button8Color = button8Color, Button9Color = button9Color, Button10Color = button10Color, FlattenEverythingColor = flattenEverythingColor }, input, ref cacheAlightenButtonPanelV0004);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenButtonPanelV0004 AlightenButtonPanelV0004(int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, string oFPresetName, string oFCustomPresets, int oFMinimumVolumeForMarker, int oFMaximumMarkerSize, int oFTrapLookaheadBars, int oFTrapMoveTicks, int oFTrapMinTotalVolume, int oFTrapMinDominancePct, int oFSITrapLookaheadBars, int oFSITrapMoveTicks, int oFSIAggregationIntervalTicks, double oFSIImbFact, int oFSIStackedLevels, double oFSIMinRowVolume, Brush button1Color, Brush button2Color, Brush button3Color, Brush button4Color, Brush button5Color, Brush button6Color, Brush button7Color, Brush button8Color, Brush button9Color, Brush button10Color, Brush flattenEverythingColor)
		{
			return indicator.AlightenButtonPanelV0004(Input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, oFPresetName, oFCustomPresets, oFMinimumVolumeForMarker, oFMaximumMarkerSize, oFTrapLookaheadBars, oFTrapMoveTicks, oFTrapMinTotalVolume, oFTrapMinDominancePct, oFSITrapLookaheadBars, oFSITrapMoveTicks, oFSIAggregationIntervalTicks, oFSIImbFact, oFSIStackedLevels, oFSIMinRowVolume, button1Color, button2Color, button3Color, button4Color, button5Color, button6Color, button7Color, button8Color, button9Color, button10Color, flattenEverythingColor);
		}

		public Indicators.AlightenButtonPanelV0004 AlightenButtonPanelV0004(ISeries<double> input , int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, string oFPresetName, string oFCustomPresets, int oFMinimumVolumeForMarker, int oFMaximumMarkerSize, int oFTrapLookaheadBars, int oFTrapMoveTicks, int oFTrapMinTotalVolume, int oFTrapMinDominancePct, int oFSITrapLookaheadBars, int oFSITrapMoveTicks, int oFSIAggregationIntervalTicks, double oFSIImbFact, int oFSIStackedLevels, double oFSIMinRowVolume, Brush button1Color, Brush button2Color, Brush button3Color, Brush button4Color, Brush button5Color, Brush button6Color, Brush button7Color, Brush button8Color, Brush button9Color, Brush button10Color, Brush flattenEverythingColor)
		{
			return indicator.AlightenButtonPanelV0004(input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, oFPresetName, oFCustomPresets, oFMinimumVolumeForMarker, oFMaximumMarkerSize, oFTrapLookaheadBars, oFTrapMoveTicks, oFTrapMinTotalVolume, oFTrapMinDominancePct, oFSITrapLookaheadBars, oFSITrapMoveTicks, oFSIAggregationIntervalTicks, oFSIImbFact, oFSIStackedLevels, oFSIMinRowVolume, button1Color, button2Color, button3Color, button4Color, button5Color, button6Color, button7Color, button8Color, button9Color, button10Color, flattenEverythingColor);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenButtonPanelV0004 AlightenButtonPanelV0004(int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, string oFPresetName, string oFCustomPresets, int oFMinimumVolumeForMarker, int oFMaximumMarkerSize, int oFTrapLookaheadBars, int oFTrapMoveTicks, int oFTrapMinTotalVolume, int oFTrapMinDominancePct, int oFSITrapLookaheadBars, int oFSITrapMoveTicks, int oFSIAggregationIntervalTicks, double oFSIImbFact, int oFSIStackedLevels, double oFSIMinRowVolume, Brush button1Color, Brush button2Color, Brush button3Color, Brush button4Color, Brush button5Color, Brush button6Color, Brush button7Color, Brush button8Color, Brush button9Color, Brush button10Color, Brush flattenEverythingColor)
		{
			return indicator.AlightenButtonPanelV0004(Input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, oFPresetName, oFCustomPresets, oFMinimumVolumeForMarker, oFMaximumMarkerSize, oFTrapLookaheadBars, oFTrapMoveTicks, oFTrapMinTotalVolume, oFTrapMinDominancePct, oFSITrapLookaheadBars, oFSITrapMoveTicks, oFSIAggregationIntervalTicks, oFSIImbFact, oFSIStackedLevels, oFSIMinRowVolume, button1Color, button2Color, button3Color, button4Color, button5Color, button6Color, button7Color, button8Color, button9Color, button10Color, flattenEverythingColor);
		}

		public Indicators.AlightenButtonPanelV0004 AlightenButtonPanelV0004(ISeries<double> input , int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, string oFPresetName, string oFCustomPresets, int oFMinimumVolumeForMarker, int oFMaximumMarkerSize, int oFTrapLookaheadBars, int oFTrapMoveTicks, int oFTrapMinTotalVolume, int oFTrapMinDominancePct, int oFSITrapLookaheadBars, int oFSITrapMoveTicks, int oFSIAggregationIntervalTicks, double oFSIImbFact, int oFSIStackedLevels, double oFSIMinRowVolume, Brush button1Color, Brush button2Color, Brush button3Color, Brush button4Color, Brush button5Color, Brush button6Color, Brush button7Color, Brush button8Color, Brush button9Color, Brush button10Color, Brush flattenEverythingColor)
		{
			return indicator.AlightenButtonPanelV0004(input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, oFPresetName, oFCustomPresets, oFMinimumVolumeForMarker, oFMaximumMarkerSize, oFTrapLookaheadBars, oFTrapMoveTicks, oFTrapMinTotalVolume, oFTrapMinDominancePct, oFSITrapLookaheadBars, oFSITrapMoveTicks, oFSIAggregationIntervalTicks, oFSIImbFact, oFSIStackedLevels, oFSIMinRowVolume, button1Color, button2Color, button3Color, button4Color, button5Color, button6Color, button7Color, button8Color, button9Color, button10Color, flattenEverythingColor);
		}
	}
}

#endregion
