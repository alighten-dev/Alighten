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
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators
{
	public class AlightenButtonPanelV0001 : Indicator
	{
		
		private ChartTrader chartTrader;
		private System.Windows.Controls.Grid chartTraderGrid;
		private System.Windows.Controls.Grid chartTraderButtonsGrid;
		private System.Windows.Controls.RowDefinition addedRow;
		private System.Windows.Controls.Grid panelGrid;
		
		private NinjaTrader.Gui.Tools.AccountSelector    xAcSelector;
		private NinjaTrader.Gui.Tools.InstrumentSelector xInSelector;
	
	    private Button btnFlattenEverything; //btnBE, btnBEPlus, btnBracket, btnAddStop, btnHalf, btnDouble, btnPricePlus, btnEntryPlus, 
		
		private double lastClose = 0;
		private string lastOcoId;
		private double lastBracketTargetBase;
		private bool   lastBracketIsLong;
		private int    lastBracketQty;
		
		private volatile bool isFlatteningAll = false;

	
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
				Description									= @"Enter the description for your new custom Indicator here.";
				Name										= "AlightenButtonPanelV0001";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= true;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive					= true;
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
		
		    // 3) build our 6×2 panel
		    panelGrid = new Grid { Margin = new Thickness(4) };
		    // six rows 
			for (int row = 0; row < 6; row++)
			    panelGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
		    // two columns
		    for (int col = 0; col < 2; col++)
		        panelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		
		    // 4) create each button, passing in its configured Brush
			btnFlattenEverything = CreateButton("FLATTEN EVERYTHING", FlattenEverythingColor, BtnFlattenEverything_Click);
			
		    var bePlus1 = CreateButton(
		        $"BE + {Breakeven1PlusTicks}", 
		        Button1Color,    // Blue by default
		        BtnBEPlus1_Click
		    );
		    var bePlus2 = CreateButton(
		        $"BE + {Breakeven2PlusTicks}", 
		        Button2Color,    // Blue
		        BtnBEPlus2_Click
		    );
			var pricePlus = CreateButton(
		        $"Price + {PricePlusTicks}", 
		        Button3Color,    // Blue by default
		        BtnPricePlus_Click
		    );
		    var entryPlus = CreateButton(
		        $"Entry + {EntryPlusTicks}", 
		        Button4Color,    // Blue
		        BtnEntryPlus_Click
		    );
		    var bracket = CreateButton(
		        "Bracket", 
		        Button5Color,    // Blue
		        BtnBracket_Click
		    );
		    var addStop = CreateButton(
		        "Add Stop", 
		        Button6Color,    // Blue
		        BtnAddStop_Click
		    );
		    var half    = CreateButton("Half",   Button7Color, BtnHalf_Click);   // DimGray
		    var dbl     = CreateButton("Double", Button8Color, BtnDouble_Click); // DimGray
		    var naked   = CreateButton("Naked",  Button9Color, BtnNaked_Click);  // DimGray
		    var split   = CreateButton("Split",  Button10Color, BtnSplit_Click);  // DimGray
			
			// 5) Flatteb Everything Button
			Grid.SetRow(btnFlattenEverything, 0);
			Grid.SetColumn(btnFlattenEverything, 0);
			Grid.SetColumnSpan(btnFlattenEverything, 2);
			panelGrid.Children.Add(btnFlattenEverything);
		
		    // 6) position them in the 5×2 grid
		    Grid.SetRow(bePlus1,   1); Grid.SetColumn(bePlus1,   0);
			Grid.SetRow(bePlus2,   1); Grid.SetColumn(bePlus2,   1);
			Grid.SetRow(pricePlus, 2); Grid.SetColumn(pricePlus, 0);
			Grid.SetRow(entryPlus, 2); Grid.SetColumn(entryPlus, 1);
			Grid.SetRow(bracket,   3); Grid.SetColumn(bracket,   0);
			Grid.SetRow(addStop,   3); Grid.SetColumn(addStop,   1);
			Grid.SetRow(half,      4); Grid.SetColumn(half,      0);
			Grid.SetRow(dbl,       4); Grid.SetColumn(dbl,       1);
			Grid.SetRow(naked,     5); Grid.SetColumn(naked,     0);
			Grid.SetRow(split,     5); Grid.SetColumn(split,     1);
		
		    // 6) add to our panel
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
		
		    // 7) inject into the ChartTrader buttons grid
		    addedRow = new RowDefinition { Height = GridLength.Auto };
		    chartTraderButtonsGrid.RowDefinitions.Add(addedRow);
		    Grid.SetRow(panelGrid, chartTraderButtonsGrid.RowDefinitions.Count - 1);
		    Grid.SetColumnSpan(panelGrid, chartTraderButtonsGrid.ColumnDefinitions.Count);
		    chartTraderButtonsGrid.Children.Add(panelGrid);
		}
		
		private Button CreateButton(string text, Brush background, RoutedEventHandler handler)
		{
		    var b = new Button
		    {
		        Content    = text,
		        Background = background,
		        Foreground = Brushes.White,           // white text
		        FontSize   = 14,                      // larger font
		        FontWeight = FontWeights.Normal,
		        Height     = 30,                      // taller
		        MinWidth   = 80,                      // ensure wide enough
		        Margin     = new Thickness(2),
		        Padding    = new Thickness(4,2,4,2),
		        Style      = Application.Current.TryFindResource("Button") as Style
		    };
		    b.Click += handler;
		    return b;
		}


		private void RemoveWPFControls()
		{
		    if (chartTraderButtonsGrid == null || panelGrid == null || addedRow == null)
		        return; 
		
		    // remove the grid and its row
		    chartTraderButtonsGrid.Children.Remove(panelGrid);
		    chartTraderButtonsGrid.RowDefinitions.Remove(addedRow);
		
		    // clear references
		    panelGrid              = null;
		    addedRow               = null;
		    chartTraderGrid        = null;
		    chartTraderButtonsGrid = null;
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
		
		            int passes           = 0;
		            int maxPasses        = FlattenAllTries;   // ~6 * 250ms ≈ 1.5s worst case
		            int totalMarketSub   = 0;
		
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
		                            int qty   = Math.Abs(pos.Quantity);
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
		           (o.OrderState == OrderState.Working   || o.OrderState == OrderState.Accepted) &&
		           (o.OrderType == OrderType.Limit       ||
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
		        var stopLeg  = first.First(o => o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit);
		        var tgtLeg   = first.First(o => o.OrderType == OrderType.Limit);
		        double baseStop   = stopLeg.StopPrice;
		        double baseTarget = tgtLeg.LimitPrice;
		        bool   isLong     = stopLeg.OrderAction == OrderAction.Sell;
		        double tickSz     = instr.MasterInstrument.TickSize;
		
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
		                Name + "_Stop"  + (i+1),
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
		                Name + "_Split" + (i+1),
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
		        var stopLeg  = legs.First(o => o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit);
		        var limitLeg = legs.First(o => o.OrderType == OrderType.Limit);
		        int    qty             = limitLeg.Quantity;
		        double baseStopPrice   = stopLeg.StopPrice;
		        double baseTargetPrice = limitLeg.LimitPrice;
		        bool   isLong2         = stopLeg.OrderAction == OrderAction.Sell;
		        double tickSz2         = instr.MasterInstrument.TickSize;
		
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
		                Name + "_Stop"  + (i+1),
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
		                Name + "_Split" + (i+1),
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
		
		    bool isLong      = pos.MarketPosition == MarketPosition.Long;
		    int  currentQty  = pos.Quantity;
		
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
		    int totalContracts      = pos.Quantity;
		    int toRemoveContracts   = totalContracts / 2;
		
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
		
		        int bracketTotal    = groups.Sum(g => g.Contracts);
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
		            int keepHere   = grp.Contracts - removeHere;
		
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
		                var stops   = grp.Legs.Where(o => o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit);
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
		        o.OrderType     == OrderType.StopMarket &&
		       (o.OrderState    == OrderState.Accepted || o.OrderState == OrderState.Working));
		    if (hasStop)
		    {
		        Print("A stop already exists—skipping Add Stop");
		        return;
		    }
		
		    // 5) compute stop price off lastClose…
		    double basePrice  = instr.MasterInstrument.RoundToTickSize(lastClose);
		    double tickSz     = instr.MasterInstrument.TickSize;
		    bool   isLong     = pos.MarketPosition == MarketPosition.Long;
		    int    qty        = pos.Quantity;
		    double stopPx     = isLong
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
		    double tickSz    = instr.MasterInstrument.TickSize;
		    bool   isLong    = pos.MarketPosition == MarketPosition.Long;
		    int    qty       = pos.Quantity;
		
		    double stopPx   = isLong
		                     ? basePrice - stopTicks   * tickSz
		                     : basePrice + stopTicks   * tickSz;
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
		private AlightenButtonPanelV0001[] cacheAlightenButtonPanelV0001;
		public AlightenButtonPanelV0001 AlightenButtonPanelV0001(int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, Brush button1Color, Brush button2Color, Brush button3Color, Brush button4Color, Brush button5Color, Brush button6Color, Brush button7Color, Brush button8Color, Brush button9Color, Brush button10Color, Brush flattenEverythingColor)
		{
			return AlightenButtonPanelV0001(Input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, button1Color, button2Color, button3Color, button4Color, button5Color, button6Color, button7Color, button8Color, button9Color, button10Color, flattenEverythingColor);
		}

		public AlightenButtonPanelV0001 AlightenButtonPanelV0001(ISeries<double> input, int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, Brush button1Color, Brush button2Color, Brush button3Color, Brush button4Color, Brush button5Color, Brush button6Color, Brush button7Color, Brush button8Color, Brush button9Color, Brush button10Color, Brush flattenEverythingColor)
		{
			if (cacheAlightenButtonPanelV0001 != null)
				for (int idx = 0; idx < cacheAlightenButtonPanelV0001.Length; idx++)
					if (cacheAlightenButtonPanelV0001[idx] != null && cacheAlightenButtonPanelV0001[idx].Breakeven1PlusTicks == breakeven1PlusTicks && cacheAlightenButtonPanelV0001[idx].Breakeven2PlusTicks == breakeven2PlusTicks && cacheAlightenButtonPanelV0001[idx].PricePlusTicks == pricePlusTicks && cacheAlightenButtonPanelV0001[idx].EntryPlusTicks == entryPlusTicks && cacheAlightenButtonPanelV0001[idx].BracketStopTicks == bracketStopTicks && cacheAlightenButtonPanelV0001[idx].BracketProfitTicks == bracketProfitTicks && cacheAlightenButtonPanelV0001[idx].FlattenAllPause == flattenAllPause && cacheAlightenButtonPanelV0001[idx].FlattenAllTries == flattenAllTries && cacheAlightenButtonPanelV0001[idx].Button1Color == button1Color && cacheAlightenButtonPanelV0001[idx].Button2Color == button2Color && cacheAlightenButtonPanelV0001[idx].Button3Color == button3Color && cacheAlightenButtonPanelV0001[idx].Button4Color == button4Color && cacheAlightenButtonPanelV0001[idx].Button5Color == button5Color && cacheAlightenButtonPanelV0001[idx].Button6Color == button6Color && cacheAlightenButtonPanelV0001[idx].Button7Color == button7Color && cacheAlightenButtonPanelV0001[idx].Button8Color == button8Color && cacheAlightenButtonPanelV0001[idx].Button9Color == button9Color && cacheAlightenButtonPanelV0001[idx].Button10Color == button10Color && cacheAlightenButtonPanelV0001[idx].FlattenEverythingColor == flattenEverythingColor && cacheAlightenButtonPanelV0001[idx].EqualsInput(input))
						return cacheAlightenButtonPanelV0001[idx];
			return CacheIndicator<AlightenButtonPanelV0001>(new AlightenButtonPanelV0001(){ Breakeven1PlusTicks = breakeven1PlusTicks, Breakeven2PlusTicks = breakeven2PlusTicks, PricePlusTicks = pricePlusTicks, EntryPlusTicks = entryPlusTicks, BracketStopTicks = bracketStopTicks, BracketProfitTicks = bracketProfitTicks, FlattenAllPause = flattenAllPause, FlattenAllTries = flattenAllTries, Button1Color = button1Color, Button2Color = button2Color, Button3Color = button3Color, Button4Color = button4Color, Button5Color = button5Color, Button6Color = button6Color, Button7Color = button7Color, Button8Color = button8Color, Button9Color = button9Color, Button10Color = button10Color, FlattenEverythingColor = flattenEverythingColor }, input, ref cacheAlightenButtonPanelV0001);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AlightenButtonPanelV0001 AlightenButtonPanelV0001(int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, Brush button1Color, Brush button2Color, Brush button3Color, Brush button4Color, Brush button5Color, Brush button6Color, Brush button7Color, Brush button8Color, Brush button9Color, Brush button10Color, Brush flattenEverythingColor)
		{
			return indicator.AlightenButtonPanelV0001(Input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, button1Color, button2Color, button3Color, button4Color, button5Color, button6Color, button7Color, button8Color, button9Color, button10Color, flattenEverythingColor);
		}

		public Indicators.AlightenButtonPanelV0001 AlightenButtonPanelV0001(ISeries<double> input , int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, Brush button1Color, Brush button2Color, Brush button3Color, Brush button4Color, Brush button5Color, Brush button6Color, Brush button7Color, Brush button8Color, Brush button9Color, Brush button10Color, Brush flattenEverythingColor)
		{
			return indicator.AlightenButtonPanelV0001(input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, button1Color, button2Color, button3Color, button4Color, button5Color, button6Color, button7Color, button8Color, button9Color, button10Color, flattenEverythingColor);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AlightenButtonPanelV0001 AlightenButtonPanelV0001(int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, Brush button1Color, Brush button2Color, Brush button3Color, Brush button4Color, Brush button5Color, Brush button6Color, Brush button7Color, Brush button8Color, Brush button9Color, Brush button10Color, Brush flattenEverythingColor)
		{
			return indicator.AlightenButtonPanelV0001(Input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, button1Color, button2Color, button3Color, button4Color, button5Color, button6Color, button7Color, button8Color, button9Color, button10Color, flattenEverythingColor);
		}

		public Indicators.AlightenButtonPanelV0001 AlightenButtonPanelV0001(ISeries<double> input , int breakeven1PlusTicks, int breakeven2PlusTicks, int pricePlusTicks, int entryPlusTicks, int bracketStopTicks, int bracketProfitTicks, int flattenAllPause, int flattenAllTries, Brush button1Color, Brush button2Color, Brush button3Color, Brush button4Color, Brush button5Color, Brush button6Color, Brush button7Color, Brush button8Color, Brush button9Color, Brush button10Color, Brush flattenEverythingColor)
		{
			return indicator.AlightenButtonPanelV0001(input, breakeven1PlusTicks, breakeven2PlusTicks, pricePlusTicks, entryPlusTicks, bracketStopTicks, bracketProfitTicks, flattenAllPause, flattenAllTries, button1Color, button2Color, button3Color, button4Color, button5Color, button6Color, button7Color, button8Color, button9Color, button10Color, flattenEverythingColor);
		}
	}
}

#endregion
